#!/usr/bin/env bash
# Mises à jour automatiques du système (Ubuntu/Debian), en root. Idempotent.
#   curl -fsSL https://raw.githubusercontent.com/ippous/infra-hub/main/deploy/maj-systeme.sh | bash
#
# - correctifs de sécurité Ubuntu (déjà le comportement par défaut) + paquets Docker (download.docker.com)
# - exécution la nuit (03:30 ± 15 min) : une mise à jour de Docker redémarre brièvement tous les conteneurs
# - redémarrage automatique à 04:30, uniquement si une mise à jour l'exige (noyau, libc…)
# - suppression des anciens noyaux et dépendances inutiles
set -euo pipefail

[ "$(id -u)" -eq 0 ] || { echo "Lancer en root." >&2; exit 1; }
command -v apt-get >/dev/null || { echo "Système non pris en charge (Ubuntu ou Debian attendu)." >&2; exit 1; }

export DEBIAN_FRONTEND=noninteractive
dpkg -s unattended-upgrades >/dev/null 2>&1 || { apt-get update -q >/dev/null; apt-get install -yq unattended-upgrades >/dev/null; }

# Activation (équivalent de « dpkg-reconfigure unattended-upgrades »)
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'CONF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
APT::Periodic::AutocleanInterval "7";
CONF

# Réglages Infra Hub : les listes s'ajoutent à celles de 50unattended-upgrades, les valeurs les remplacent.
cat > /etc/apt/apt.conf.d/52infrahub-maj-auto <<'CONF'
// Géré par Infra Hub (deploy/maj-systeme.sh). Ne pas modifier à la main : relancer le script.
Unattended-Upgrade::Origins-Pattern {
        "site=download.docker.com";
};
Unattended-Upgrade::Automatic-Reboot "true";
Unattended-Upgrade::Automatic-Reboot-WithUsers "true";
Unattended-Upgrade::Automatic-Reboot-Time "04:30";
Unattended-Upgrade::Remove-Unused-Kernel-Packages "true";
Unattended-Upgrade::Remove-New-Unused-Dependencies "true";
Unattended-Upgrade::Remove-Unused-Dependencies "true";
Unattended-Upgrade::SyslogEnable "true";
CONF

# Horaire de nuit : la liste des paquets est rafraîchie vers 03:00, l'installation part vers 03:30.
if command -v systemctl >/dev/null && [ -d /run/systemd/system ]; then
  install -d /etc/systemd/system/apt-daily.timer.d /etc/systemd/system/apt-daily-upgrade.timer.d
  cat > /etc/systemd/system/apt-daily.timer.d/infrahub.conf <<'UNIT'
[Timer]
OnCalendar=
OnCalendar=*-*-* 03:00
RandomizedDelaySec=10m
UNIT
  cat > /etc/systemd/system/apt-daily-upgrade.timer.d/infrahub.conf <<'UNIT'
[Timer]
OnCalendar=
OnCalendar=*-*-* 03:30
RandomizedDelaySec=15m
UNIT
  systemctl daemon-reload
  systemctl restart apt-daily.timer apt-daily-upgrade.timer
fi

# Les conteneurs doivent revenir seuls après un redémarrage de Docker ou du serveur.
if command -v docker >/dev/null && docker info >/dev/null 2>&1; then
  SANS_REDEMARRAGE=$(docker ps --format '{{.Names}}' | while read -r c; do
    p=$(docker inspect -f '{{.HostConfig.RestartPolicy.Name}}' "$c")
    [ "$p" = "no" ] || [ -z "$p" ] && echo "$c"
  done || true)
fi

echo "Origines mises à jour automatiquement :"
timeout 300 unattended-upgrade --dry-run --verbose 2>&1 | sed -n 's/^Allowed origins are: //p' | sed 's/, /\n/g' | sed 's/^/  /'
if command -v systemctl >/dev/null && [ -d /run/systemd/system ]; then
  echo "Prochaines exécutions :"
  systemctl list-timers apt-daily.timer apt-daily-upgrade.timer --no-pager | sed -n '2,3p' | awk '{print "  " $1, $2, $3, "→", $NF}'
fi
[ -f /var/run/reboot-required ] && echo "Un redémarrage est déjà en attente : il aura lieu cette nuit à 04:30."
if [ -n "${SANS_REDEMARRAGE:-}" ]; then
  echo "Attention, ces conteneurs ne redémarreront pas seuls après un redémarrage du serveur :"
  echo "$SANS_REDEMARRAGE" | sed 's/^/  /'
fi
echo "Mises à jour système configurées."
