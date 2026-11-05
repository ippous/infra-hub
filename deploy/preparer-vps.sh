#!/usr/bin/env bash
# Préparation d'un VPS Hostinger (Ubuntu 24.04) pour Infra Hub. À lancer une fois, en root :
#   curl -fsSL https://raw.githubusercontent.com/ippous/infra-hub/main/deploy/preparer-vps.sh | bash -s -- "<clé SSH publique de déploiement>"
# Idempotent : peut être relancé sans risque.
set -euo pipefail

CLE_DEPLOIEMENT="${1:-}"
DOSSIER=/opt/infrahub
UTILISATEUR=deploy

echo "==> Paquets système"
export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get upgrade -yq
apt-get install -yq ca-certificates curl ufw fail2ban unattended-upgrades

echo "==> Docker"
if ! command -v docker >/dev/null; then
  curl -fsSL https://get.docker.com | sh
fi
systemctl enable --now docker

echo "==> Utilisateur de déploiement « $UTILISATEUR »"
id "$UTILISATEUR" >/dev/null 2>&1 || useradd --create-home --shell /bin/bash "$UTILISATEUR"
usermod -aG docker "$UTILISATEUR"
install -d -m 700 -o "$UTILISATEUR" -g "$UTILISATEUR" "/home/$UTILISATEUR/.ssh"
if [ -n "$CLE_DEPLOIEMENT" ]; then
  touch "/home/$UTILISATEUR/.ssh/authorized_keys"
  grep -qxF "$CLE_DEPLOIEMENT" "/home/$UTILISATEUR/.ssh/authorized_keys" \
    || echo "$CLE_DEPLOIEMENT" >> "/home/$UTILISATEUR/.ssh/authorized_keys"
  chown "$UTILISATEUR:$UTILISATEUR" "/home/$UTILISATEUR/.ssh/authorized_keys"
  chmod 600 "/home/$UTILISATEUR/.ssh/authorized_keys"
fi

echo "==> Dossier $DOSSIER"
install -d -o "$UTILISATEUR" -g "$UTILISATEUR" "$DOSSIER" "$DOSSIER/deploy" "$DOSSIER/sauvegardes"

echo "==> Pare-feu (SSH, HTTP, HTTPS)"
ufw default deny incoming
ufw default allow outgoing
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable

echo "==> SSH : connexion par clé uniquement"
cat > /etc/ssh/sshd_config.d/10-infrahub.conf <<'CONF'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
CONF
systemctl reload ssh || systemctl reload sshd

echo "==> Mises à jour de sécurité automatiques et fail2ban"
dpkg-reconfigure -f noninteractive unattended-upgrades
systemctl enable --now fail2ban

echo
echo "Terminé. Il reste à créer $DOSSIER/.env (modèle : .env.example), en tant que $UTILISATEUR."
