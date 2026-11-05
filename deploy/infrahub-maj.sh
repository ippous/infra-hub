#!/usr/bin/env bash
# Mise à jour d'Infra Hub sur le VPS (lancée toutes les 5 minutes par infrahub-maj.timer).
# Récupère les dernières images publiées sur ghcr.io ; si la version a changé, télécharge la
# configuration (docker-compose.yml, Caddyfile) du même commit puis relance les conteneurs.
set -euo pipefail

DOSSIER="${INFRAHUB_DIR:-/opt/infrahub}"
DEPOT="ippous/infra-hub"
IMAGE_WEB="ghcr.io/ippous/infra-hub-web:latest"
IMAGE_AGENT="ghcr.io/ippous/infra-hub-agent:latest"

exec 9>"$DOSSIER/.maj.lock"
flock -n 9 || { echo "Mise à jour déjà en cours."; exit 0; }
cd "$DOSSIER"

docker pull -q "$IMAGE_WEB" >/dev/null
docker pull -q "$IMAGE_AGENT" >/dev/null

REVISION=$(docker image inspect "$IMAGE_WEB" --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' 2>/dev/null || true)
[ -n "$REVISION" ] || REVISION=main
ACTUELLE=$(cat .revision 2>/dev/null || true)

if [ "$REVISION" = "$ACTUELLE" ] && [ "${1:-}" != "--force" ] \
   && [ "$(docker compose ps --status running -q 2>/dev/null | wc -l)" -ge 5 ]; then
  exit 0
fi

echo "Déploiement de la version $REVISION (précédente : ${ACTUELLE:-aucune})"
BASE="https://raw.githubusercontent.com/$DEPOT/$REVISION"
mkdir -p deploy
curl -fsSL "$BASE/docker-compose.yml" -o docker-compose.yml.nouveau
curl -fsSL "$BASE/deploy/Caddyfile" -o deploy/Caddyfile.nouveau
curl -fsSL "$BASE/deploy/infrahub-maj.sh" -o infrahub-maj.sh.nouveau
mv docker-compose.yml.nouveau docker-compose.yml
mv deploy/Caddyfile.nouveau deploy/Caddyfile

if [ "$REVISION" != main ]; then
  # Version figée sur le commit : les images « latest » peuvent avancer pendant le déploiement.
  docker tag "$IMAGE_WEB" "ghcr.io/ippous/infra-hub-web:$REVISION"
  docker tag "$IMAGE_AGENT" "ghcr.io/ippous/infra-hub-agent:$REVISION"
fi
export INFRAHUB_TAG="$REVISION"
docker compose pull -q db caddy sauvegarde
docker compose up -d --no-build --remove-orphans
docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile >/dev/null 2>&1 || true

# Attendre que le web réponde (migrations comprises)
for _ in $(seq 1 30); do
  if docker compose exec -T caddy wget -qO- http://web:8080/health 2>/dev/null | grep -q Healthy; then
    echo "$REVISION" > .revision
    sed -i "s/^INFRAHUB_TAG=.*/INFRAHUB_TAG=$REVISION/" .env
    grep -q '^INFRAHUB_TAG=' .env || echo "INFRAHUB_TAG=$REVISION" >> .env
    # Le script de mise à jour se met lui-même à jour, en dernier.
    if [ -w /usr/local/bin/infrahub-maj ] && ! cmp -s infrahub-maj.sh.nouveau /usr/local/bin/infrahub-maj; then
      install -m 755 infrahub-maj.sh.nouveau /usr/local/bin/infrahub-maj
    fi
    rm -f infrahub-maj.sh.nouveau
    docker image prune -f >/dev/null
    echo "Infra Hub $REVISION en service."
    exit 0
  fi
  sleep 4
done

rm -f infrahub-maj.sh.nouveau
echo "Le service web ne répond pas après le déploiement de $REVISION." >&2
docker compose logs --tail 60 web >&2
exit 1
