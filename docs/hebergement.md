# Hébergement sur le VPS Hostinger

VPS : `srv1763947.hstgr.cloud`.

GitHub Actions construit les images `web` et `agent` après chaque build réussi sur `main` et les publie sur `ghcr.io` (publiques, comme le dépôt). Le VPS vérifie toutes les 5 minutes s'il existe une nouvelle version ; si oui, il télécharge la configuration du même commit et relance les conteneurs. GitHub n'a aucun accès au serveur : pas de clé SSH ni de secret à configurer.

```
push main ─► build (tests) ─► deploy : images ghcr.io ◄── VPS (infrahub-maj, toutes les 5 min)
```

Sur le VPS, dans `/opt/infrahub` : `docker-compose.yml` et `deploy/Caddyfile` (mis à jour automatiquement), `.env` (généré à l'installation, jamais commité), `sauvegardes/`.

Services : `caddy` (TLS automatique, mot de passe provisoire), `web`, `agent`, `db` (PostgreSQL 17), `sauvegarde` (pg_dump quotidien, 14 jours).

## Installation (une fois)

1. **hPanel → VPS → Terminal** (terminal dans le navigateur, connecté en root).
2. Coller :

   ```bash
   curl -fsSL https://raw.githubusercontent.com/ippous/infra-hub/main/deploy/installer.sh | bash -s -- infra.jaume-eric.com
   ```

   Le script vérifie que les ports 80 et 443 sont libres (il s'arrête sinon), installe Docker, fail2ban et les mises à jour de sécurité automatiques, génère le `.env` (mot de passe PostgreSQL et accès web aléatoires), installe le minuteur de mise à jour et lance la première version. Il ne modifie ni SSH ni le pare-feu, et peut être relancé sans risque.

3. **Noter le mot de passe d'accès** affiché à la fin (utilisateur `eric`) dans le gestionnaire de mots de passe : il n'est affiché qu'une fois.
4. **DNS** : le script affiche l'IP du VPS. Dans la zone DNS de `jaume-eric.com`, ajouter un enregistrement **A** `infra` → cette IP. Caddy obtient le certificat dès que le DNS répond.

## Mode derrière un proxy existant (cas de srv1763947)

Sur ce VPS, les ports sont déjà pris : 443 par le Caddy de `shetland-registre`, 80 par le site PHP de `programmeelevage`. Infra Hub passe donc par le Caddy de shetland, qui termine le TLS (certificat obtenu sur 443, sans le port 80) et transmet à `infrahub-caddy:80` sur le réseau Docker `shetland-registre_default`. Le Caddy d'Infra Hub ne publie aucun port et garde le mot de passe et les en-têtes.

```
Internet ─► :443 Caddy shetland ─┬─► shetland-registre.jaume-eric.com (fichiers)
                                 └─► infra.jaume-eric.com ─► infrahub-caddy:80 (mot de passe) ─► web:8080
```

Installation (ou passage à ce mode) :

```bash
curl -fsSL https://raw.githubusercontent.com/ippous/infra-hub/main/deploy/installer.sh | bash -s -- infra.jaume-eric.com --derriere-proxy shetland-registre_default
```

Côté shetland, un bloc ajouté à `/opt/shetland-registre/Caddyfile` (une sauvegarde est faite avant) :

```
infra.jaume-eric.com {
    reverse_proxy infrahub-caddy:80
    tls {
        issuer acme {
            disable_http_challenge
        }
    }
}
```

puis `docker exec shetland-registre-web-1 caddy reload --config /etc/caddy/Caddyfile`. Pour revenir en arrière : remettre `Caddyfile.avant-infrahub` et recharger.

## Mises à jour du système

`deploy/maj-systeme.sh` (lancé par l'installateur, ou seul : `curl -fsSL https://raw.githubusercontent.com/ippous/infra-hub/main/deploy/maj-systeme.sh | bash`) configure `unattended-upgrades` :

- correctifs de sécurité Ubuntu et paquets Docker (`download.docker.com`) installés automatiquement ;
- la nuit : listes rafraîchies vers 03:00, installation vers 03:30 (une mise à jour de Docker redémarre brièvement tous les conteneurs) ;
- redémarrage automatique à 04:30, seulement si une mise à jour l'exige (`/var/run/reboot-required`) ;
- anciens noyaux et dépendances inutiles supprimés.

Les mises à jour Ubuntu hors sécurité (`noble-updates`) ne sont pas installées automatiquement. Contrôle :

```bash
cat /var/log/unattended-upgrades/unattended-upgrades.log | tail -20   # dernières installations
systemctl list-timers apt-daily-upgrade.timer                         # prochaine exécution
ls /var/run/reboot-required 2>/dev/null && cat /var/run/reboot-required.pkgs
```

## Mise à jour des images Docker (chaque dimanche à 02:30)

`deploy/maj-images.sh`, installé en `/usr/local/bin/infrahub-maj-images`, ne traite que les projets Compose listés dans `/etc/infrahub/maj-images.conf`. Pour chacun :

1. sauvegarde des bases en marche (PostgreSQL : `pg_dumpall` ; MariaDB/MySQL : dump complet avec le mot de passe root du conteneur) dans `/var/backups/infrahub-maj-images/<projet>/`, 8 conservées par base. **Si une sauvegarde échoue, le projet n'est pas touché** ;
2. téléchargement des images, dans leur étiquette actuelle (`postgres:17` reste en 17) ;
3. reconstruction des images construites sur place avec une image de base fraîche (`build --pull`). En cas d'échec, les images actuelles sont conservées ;
4. redémarrage et vérification (conteneurs en marche et « healthy »).

Les images d'Infra Hub (`web`, `agent`) restent gérées par `infrahub-maj` (toutes les 5 minutes). Les images sans version fixée (`:latest` ou sans étiquette) sont signalées.

```bash
infrahub-maj-images --installer infrahub shetland-registre programmeelevage   # choix des projets + minuteur
infrahub-maj-images --essai                                                   # ce qui changerait, sans rien toucher
journalctl -u infrahub-maj-images -n 50 --no-pager                            # dernier passage
cat /var/lib/infrahub/maj-images.json                                         # résumé par projet
```

## Exploitation

```bash
cd /opt/infrahub
docker compose ps
docker compose logs -f web
journalctl -u infrahub-maj -n 50          # historique des mises à jour
infrahub-maj --force                      # forcer une mise à jour maintenant
```

Restaurer une sauvegarde :

```bash
gunzip -c sauvegardes/infrahub-AAAAMMJJ-HHMM.sql.gz | docker compose exec -T db psql -U infrahub -d infrahub
```

Revenir à une version précédente : `systemctl stop infrahub-maj.timer`, puis `INFRAHUB_TAG=<sha du commit> docker compose up -d --no-build` (après `docker pull ghcr.io/ippous/infra-hub-web:<sha>` et `…-agent:<sha>`). Relancer le minuteur une fois corrigé.

Changer le mot de passe d'accès : `docker run --rm caddy:2 caddy hash-password`, remplacer `INFRAHUB_MOT_DE_PASSE_HASH` dans `.env` (entre apostrophes), puis `docker compose up -d caddy`.

## À savoir

- **Mot de passe provisoire** : `basic_auth` de Caddy protège tout le site. Il sera remplacé par l'authentification de l'application (indispensable avant d'ouvrir le MCP à claude.ai).
- **Sauvegardes** : elles restent sur le VPS. Les copier ailleurs (NAS via Tailscale, étape 4) est prévu.
- **Pare-feu** : Docker publie lui-même les ports 80 et 443 ; le pare-feu de hPanel peut rester tel quel s'il laisse passer 22, 80 et 443.
