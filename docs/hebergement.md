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
