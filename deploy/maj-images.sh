#!/usr/bin/env bash
# Mise à jour hebdomadaire des images Docker des projets Compose du VPS.
#
#   maj-images.sh --installer infrahub shetland-registre programmeelevage   installe le minuteur (dimanche 02:30)
#   maj-images.sh --essai                                                   montre ce qui changerait, sans rien toucher
#   maj-images.sh                                                           exécute (c'est ce que lance le minuteur)
#
# Seuls les projets listés à l'installation sont traités (/etc/infrahub/maj-images.conf).
# Pour chacun : sauvegarde des bases (PostgreSQL, MariaDB/MySQL) → téléchargement des images
# → reconstruction des images construites sur place avec une image de base fraîche → redémarrage → vérification.
# Si la sauvegarde ou la reconstruction échoue, le projet est laissé tel quel.
# Les étiquettes en place sont respectées (postgres:17 reste en 17) ; les images sans version fixée sont signalées.
set -uo pipefail

CONF=/etc/infrahub/maj-images.conf
SAUVEGARDES=/var/backups/infrahub-maj-images
ETAT=/var/lib/infrahub/maj-images.json
GARDER=8   # sauvegardes conservées par base
BRUT="https://raw.githubusercontent.com/ippous/infra-hub/main"

journal() { printf '%s %s\n' "$(date '+%F %T')" "$*"; }

installer() {
  [ $# -gt 0 ] || { echo "Indiquer au moins un projet Compose (docker compose ls)." >&2; exit 1; }
  for p in "$@"; do
    docker ps -a --filter "label=com.docker.compose.project=$p" -q | grep -q . \
      || { echo "Projet Compose « $p » introuvable (docker compose ls)." >&2; exit 1; }
  done
  install -d /etc/infrahub /var/lib/infrahub "$SAUVEGARDES"
  printf '# Projets Compose mis à jour chaque semaine par infrahub-maj-images (un par ligne)\n%s\n' "$(printf '%s\n' "$@")" > "$CONF"
  if [ "$(readlink -f "$0")" != /usr/local/bin/infrahub-maj-images ]; then
    if [ -f "$0" ]; then install -m 755 "$0" /usr/local/bin/infrahub-maj-images
    else curl -fsSL "$BRUT/deploy/maj-images.sh" -o /usr/local/bin/infrahub-maj-images && chmod 755 /usr/local/bin/infrahub-maj-images
    fi
  fi
  cat > /etc/systemd/system/infrahub-maj-images.service <<'UNIT'
[Unit]
Description=Mise à jour hebdomadaire des images Docker (sauvegarde, téléchargement, redémarrage)
After=docker.service network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=/usr/local/bin/infrahub-maj-images
TimeoutStartSec=1h
UNIT
  cat > /etc/systemd/system/infrahub-maj-images.timer <<'UNIT'
[Unit]
Description=Mise à jour des images Docker, chaque dimanche à 02:30

[Timer]
OnCalendar=Sun *-*-* 02:30
Persistent=true
RandomizedDelaySec=10m

[Install]
WantedBy=timers.target
UNIT
  systemctl daemon-reload
  systemctl enable --now infrahub-maj-images.timer >/dev/null
  echo "Projets suivis : $*"
  echo "Prochaine exécution : $(systemctl list-timers infrahub-maj-images.timer --no-pager | sed -n 2p | awk '{print $1, $2, $3}')"
  echo "Essai à blanc : infrahub-maj-images --essai"
}

# Fichiers et dossier du projet, lus sur ses conteneurs.
contexte() {
  local c; c=$(docker ps -a --filter "label=com.docker.compose.project=$1" -q | head -1)
  [ -n "$c" ] || return 1
  DOSSIER=$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project.working_dir"}}' "$c")
  FICHIERS=$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project.config_files"}}' "$c")
  COMPOSE=(docker compose -p "$1" --project-directory "$DOSSIER")
  local f; IFS=',' read -ra liste <<< "$FICHIERS"
  for f in "${liste[@]}"; do COMPOSE+=(-f "$f"); done
}

# Sauvegarde de chaque service de base de données en marche. Échec → code ≠ 0.
sauvegarder() {
  local projet=$1 dest="$SAUVEGARDES/$1" s image fichier rc
  install -d -m 700 "$dest"
  for s in $("${COMPOSE[@]}" ps --services --status running); do
    image=$("${COMPOSE[@]}" ps --format '{{.Image}}' "$s" | head -1)
    fichier="$dest/$(date +%Y%m%d-%H%M)-$s.sql.gz"
    case "$image" in
      postgres*|*/postgres*|postgis*|*/postgis*)
        "${COMPOSE[@]}" exec -T "$s" sh -c 'pg_dumpall -U "${POSTGRES_USER:-postgres}"' | gzip > "$fichier.tmp"; rc=${PIPESTATUS[0]} ;;
      mariadb*|*/mariadb*|mysql*|*/mysql*)
        "${COMPOSE[@]}" exec -T "$s" sh -c 'p="${MARIADB_ROOT_PASSWORD:-${MYSQL_ROOT_PASSWORD:-}}"; [ -n "$p" ] || { echo "mot de passe root absent des variables du conteneur" >&2; exit 1; }; d=$(command -v mariadb-dump || command -v mysqldump); MYSQL_PWD="$p" "$d" -uroot --all-databases --single-transaction --routines --events' \
          | gzip > "$fichier.tmp"; rc=${PIPESTATUS[0]} ;;
      *) continue ;;
    esac
    if [ "$rc" -ne 0 ] || [ "$(gzip -dc "$fichier.tmp" | head -c 1 | wc -c)" -eq 0 ]; then
      rm -f "$fichier.tmp"; journal "  ÉCHEC sauvegarde $s ($image)"; return 1
    fi
    mv "$fichier.tmp" "$fichier"
    journal "  sauvegarde $s → $fichier ($(du -h "$fichier" | cut -f1))"
    ls -1t "$dest"/*-"$s".sql.gz 2>/dev/null | tail -n +$((GARDER + 1)) | xargs -r rm -f
  done
}

# Attend que tous les conteneurs du projet tournent (et soient « healthy » s'ils ont un contrôle de santé).
verifier() {
  local i ko
  for i in $(seq 1 24); do
    ko=$("${COMPOSE[@]}" ps -a --format '{{.Name}} {{.State}} {{.Health}}' | awk '$2!="running" || ($3!="" && $3!="healthy")')
    [ -z "$ko" ] && return 0
    sleep 5
  done
  journal "  conteneurs en difficulté : $(echo "$ko" | tr '\n' ';')"
  return 1
}

traiter() {
  local projet=$1 avant apres flottantes construits
  journal "== $projet"
  contexte "$projet" || { journal "  projet introuvable, ignoré"; RESULTATS+=("\"$projet\":\"introuvable\""); return 1; }
  journal "  dossier $DOSSIER"

  flottantes=$( { "${COMPOSE[@]}" config --images 2>/dev/null | grep -vE ':[^/]+$' | grep -v "^$projet[-_]"; "${COMPOSE[@]}" config --images 2>/dev/null | grep -E ':latest$'; } | grep -v '^ghcr.io/ippous/infra-hub-' | sort -u)
  [ -z "$flottantes" ] || journal "  sans version fixée (peut changer de version majeure) : $(echo $flottantes)"
  construits=$("${COMPOSE[@]}" config --format json 2>/dev/null | python3 -c 'import json,sys; print(" ".join(n for n,s in json.load(sys.stdin)["services"].items() if "build" in s and "/" not in s.get("image","")))' 2>/dev/null)

  if [ "$ESSAI" = 1 ]; then
    "${COMPOSE[@]}" --dry-run pull --ignore-buildable 2>&1 | grep -iE 'pull|download|skip' | sed 's/^/  /' | head -20
    [ -z "$construits" ] || journal "  à reconstruire avec une image de base fraîche : $construits"
    local s image
    for s in $("${COMPOSE[@]}" ps --services --status running); do
      image=$("${COMPOSE[@]}" ps --format '{{.Image}}' "$s" | head -1)
      case "$image" in
        postgres*|*/postgres*|postgis*|*/postgis*) journal "  base $s ($image) : sauvegarde pg_dumpall possible" ;;
        mariadb*|*/mariadb*|mysql*|*/mysql*)
          if "${COMPOSE[@]}" exec -T "$s" sh -c '[ -n "${MARIADB_ROOT_PASSWORD:-${MYSQL_ROOT_PASSWORD:-}}" ]'; then
            journal "  base $s ($image) : sauvegarde possible (mot de passe root disponible)"
          else
            journal "  base $s ($image) : SAUVEGARDE IMPOSSIBLE (mot de passe root absent) → le projet ne sera pas mis à jour"
          fi ;;
      esac
    done
    RESULTATS+=("\"$projet\":\"essai\""); return 0
  fi

  sauvegarder "$projet" || { RESULTATS+=("\"$projet\":\"sauvegarde en échec, non mis à jour\""); return 1; }

  avant=$("${COMPOSE[@]}" images -q 2>/dev/null | sort -u)
  "${COMPOSE[@]}" pull --quiet --ignore-buildable 2>&1 | sed 's/^/  /'
  if [ -n "$construits" ]; then
    journal "  reconstruction : $construits"
    if ! "${COMPOSE[@]}" build --pull --quiet $construits 2>&1 | tail -5 | sed 's/^/  /'; then
      journal "  ÉCHEC reconstruction : images actuelles conservées"
      RESULTATS+=("\"$projet\":\"reconstruction en échec, non mis à jour\""); return 1
    fi
  fi
  "${COMPOSE[@]}" up -d --quiet-pull 2>&1 | grep -vE 'Running|Healthy|Waiting' | sed 's/^/  /'
  apres=$("${COMPOSE[@]}" images -q 2>/dev/null | sort -u)

  if verifier; then
    if [ "$avant" = "$apres" ]; then journal "  déjà à jour"; RESULTATS+=("\"$projet\":\"à jour\"")
    else journal "  mis à jour et en service"; RESULTATS+=("\"$projet\":\"mis à jour\""); fi
  else
    RESULTATS+=("\"$projet\":\"redémarré mais en difficulté\""); return 1
  fi
}

ESSAI=0
case "${1:-}" in
  --installer) shift; [ "$(id -u)" -eq 0 ] || { echo "Lancer en root." >&2; exit 1; }; installer "$@"; exit $? ;;
  --essai) ESSAI=1 ;;
  "") ;;
  *) echo "Usage : $0 [--essai | --installer projet…]" >&2; exit 1 ;;
esac

[ -f "$CONF" ] || { echo "Aucun projet configuré : $0 --installer projet…" >&2; exit 1; }
mapfile -t PROJETS < <(grep -vE '^\s*(#|$)' "$CONF")
RESULTATS=(); ECHECS=0
for p in "${PROJETS[@]}"; do traiter "$p" || ECHECS=$((ECHECS + 1)); done

[ "$ESSAI" = 1 ] && exit 0
docker image prune -f >/dev/null 2>&1
install -d "$(dirname "$ETAT")"
printf '{"horodatage":"%s","echecs":%d,"projets":{%s}}\n' "$(date -Is)" "$ECHECS" "$(IFS=,; echo "${RESULTATS[*]}")" > "$ETAT"
journal "Terminé : ${#PROJETS[@]} projet(s), $ECHECS en échec."
exit $(( ECHECS > 0 ))
