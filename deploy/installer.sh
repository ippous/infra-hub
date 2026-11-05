#!/usr/bin/env bash
# Installation d'Infra Hub sur un VPS Ubuntu/Debian. À coller dans le terminal du VPS (hPanel → Terminal), en root :
#
#   curl -fsSL https://raw.githubusercontent.com/ippous/infra-hub/main/deploy/installer.sh | bash -s -- infra.jaume-eric.com
#
# Si un reverse proxy occupe déjà 80/443 sur le serveur, ajouter le réseau Docker où il tourne :
#
#   … | bash -s -- infra.jaume-eric.com --derriere-proxy shetland-registre_default
#
# Le proxy doit alors transmettre le domaine à infrahub-caddy:80 (voir docs/hebergement.md).
# Ne touche ni à la configuration SSH ni aux services existants. En mode autonome, s'arrête si 80/443 sont pris.
# Peut être relancé sans risque : les secrets du .env existant sont conservés.
set -euo pipefail

DOMAINE="infra.jaume-eric.com"
RESEAU_PROXY=""
while [ $# -gt 0 ]; do
  case "$1" in
    --derriere-proxy) RESEAU_PROXY="${2:?nom du réseau Docker du proxy attendu}"; shift 2 ;;
    -*) echo "Option inconnue : $1" >&2; exit 1 ;;
    *) DOMAINE="$1"; shift ;;
  esac
done
DOSSIER="${INFRAHUB_DIR:-/opt/infrahub}"
UTILISATEUR_WEB="${INFRAHUB_UTILISATEUR:-eric}"
DEPOT="ippous/infra-hub"
BRUT="https://raw.githubusercontent.com/$DEPOT/main"

titre() { printf '\n\033[1m==> %s\033[0m\n' "$1"; }
stop() { printf '\n\033[31mArrêt : %s\033[0m\n' "$1" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || stop "lancer en root."
command -v apt-get >/dev/null || stop "système non pris en charge (Ubuntu ou Debian attendu)."

titre "Vérifications"
command -v ss >/dev/null || { apt-get update -q >/dev/null && DEBIAN_FRONTEND=noninteractive apt-get install -yq iproute2 >/dev/null; }
command -v ss >/dev/null || stop "commande ss introuvable : impossible de vérifier les ports."
. /etc/os-release && echo "Système : $PRETTY_NAME"
echo "Mémoire : $(free -h | awk '/Mem:/ {print $2}') — disque libre : $(df -h / | awk 'NR==2 {print $4}')"
PORTS=$(ss -Htlnp 2>/dev/null | awk '{print $4, $6}' || true)
for p in $([ -z "$RESEAU_PROXY" ] && echo 80 443); do
  if echo "$PORTS" | grep -E "[:.]$p " | grep -vq docker-proxy; then
    echo "$PORTS" | grep -E "[:.]$p "
    stop "le port $p est déjà utilisé par un autre service (voir ci-dessus). Infra Hub a besoin de 80 et 443."
  fi
  # Port publié par Docker : accepté seulement s'il appartient déjà à Infra Hub.
  if command -v docker >/dev/null; then
    AUTRE=$(docker ps --filter "publish=$p" --format '{{.Names}} ({{.Image}}) projet={{.Label "com.docker.compose.project"}}' | grep -v 'projet=infrahub$' || true)
    if [ -n "$AUTRE" ]; then
      echo "$AUTRE"
      stop "le port $p est publié par un autre conteneur Docker (voir ci-dessus). Infra Hub a besoin de 80 et 443."
    fi
  fi
done
AUTRES=$(echo "$PORTS" | awk '{print $1}' | grep -vE '(^127\.|^\[::1\]|:22$|:53$|:80$|:443$)' | sort -u || true)
[ -z "$AUTRES" ] || { echo "Autres ports ouverts (laissés tels quels) :"; echo "$AUTRES"; }

titre "Paquets système"
export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get install -yq ca-certificates curl openssl fail2ban unattended-upgrades >/dev/null
if ! command -v docker >/dev/null; then
  curl -fsSL https://get.docker.com | sh
fi
docker compose version >/dev/null || stop "Docker Compose introuvable."
if command -v systemctl >/dev/null && [ -d /run/systemd/system ]; then
  systemctl enable --now docker fail2ban >/dev/null 2>&1 || true
fi
dpkg-reconfigure -f noninteractive unattended-upgrades >/dev/null 2>&1 || true

titre "Dossier $DOSSIER"
install -d -m 750 "$DOSSIER" "$DOSSIER/deploy" "$DOSSIER/sauvegardes"
cd "$DOSSIER"

MOT_DE_PASSE=""
if [ ! -f .env ]; then
  MOT_DE_PASSE=$(openssl rand -base64 18 | tr -d '/+=' | cut -c1-20)
  HASH=$(docker run --rm caddy:2 caddy hash-password --plaintext "$MOT_DE_PASSE")
  umask 077
  cat > .env <<ENV
INFRAHUB_DOMAIN=$DOMAINE
INFRAHUB_UTILISATEUR=$UTILISATEUR_WEB
INFRAHUB_MOT_DE_PASSE_HASH='$HASH'
POSTGRES_DB=infrahub
POSTGRES_USER=infrahub
POSTGRES_PASSWORD=$(openssl rand -hex 24)
INFRAHUB_TAG=latest
ENV
  echo ".env créé (mot de passe PostgreSQL et accès web générés)."
else
  echo ".env existant conservé (secrets inchangés)."
fi

# Mode de publication (réglé à chaque passage, secrets inchangés)
regler() { sed -i "/^$1=/d" .env; echo "$1=$2" >> .env; }
regler INFRAHUB_DOMAIN "$DOMAINE"
if [ -n "$RESEAU_PROXY" ]; then
  docker network inspect "$RESEAU_PROXY" >/dev/null 2>&1 \
    || stop "réseau Docker « $RESEAU_PROXY » introuvable (docker network ls)."
  regler COMPOSE_FILE docker-compose.yml:docker-compose.proxy.yml
  regler INFRAHUB_RESEAU_PROXY "$RESEAU_PROXY"
  echo "Mode : derrière le proxy du réseau « $RESEAU_PROXY » (cible : infrahub-caddy:80)."
else
  regler COMPOSE_FILE docker-compose.yml:docker-compose.tls.yml
  sed -i '/^INFRAHUB_RESEAU_PROXY=/d' .env
  echo "Mode : Caddy autonome sur 80/443."
fi

titre "Mise à jour automatique"
curl -fsSL "$BRUT/deploy/infrahub-maj.sh" -o /usr/local/bin/infrahub-maj
chmod 755 /usr/local/bin/infrahub-maj
if command -v systemctl >/dev/null && [ -d /run/systemd/system ]; then
  cat > /etc/systemd/system/infrahub-maj.service <<'UNIT'
[Unit]
Description=Mise à jour d'Infra Hub depuis ghcr.io
After=docker.service network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=/usr/local/bin/infrahub-maj
UNIT
  cat > /etc/systemd/system/infrahub-maj.timer <<'UNIT'
[Unit]
Description=Vérifie toutes les 5 minutes s'il existe une nouvelle version d'Infra Hub

[Timer]
OnBootSec=2min
OnUnitActiveSec=5min
RandomizedDelaySec=30

[Install]
WantedBy=timers.target
UNIT
  systemctl daemon-reload
  systemctl enable --now infrahub-maj.timer >/dev/null
  echo "Minuteur infrahub-maj.timer actif (journal : journalctl -u infrahub-maj)."
else
  echo "systemd absent : lancer /usr/local/bin/infrahub-maj à la main ou via cron."
fi

titre "Premier déploiement"
INFRAHUB_DIR="$DOSSIER" /usr/local/bin/infrahub-maj --force

titre "DNS"
IP=$(curl -fsS -4 https://api.ipify.org 2>/dev/null || hostname -I | awk '{print $1}')
RESOLU=$(getent ahostsv4 "$DOMAINE" 2>/dev/null | awk 'NR==1 {print $1}' || true)
echo "IP du VPS : $IP"
if [ "$RESOLU" = "$IP" ]; then
  echo "$DOMAINE pointe bien vers ce VPS : https://$DOMAINE"
else
  echo "À faire : enregistrement A « ${DOMAINE%%.*} » → $IP dans la zone DNS de ${DOMAINE#*.}"
  echo "(actuellement : ${RESOLU:-aucune réponse}). Caddy obtiendra le certificat dès que le DNS sera en place."
fi

if [ -n "$MOT_DE_PASSE" ]; then
  printf '\n\033[1mAccès à https://%s\033[0m\n  utilisateur : %s\n  mot de passe : %s\n' "$DOMAINE" "$UTILISATEUR_WEB" "$MOT_DE_PASSE"
  echo "Range ce mot de passe dans ton gestionnaire : il n'est affiché qu'une fois et n'est stocké nulle part en clair."
fi
echo
echo "Installation terminée."
