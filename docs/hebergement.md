# Hébergement sur le VPS Hostinger

Architecture : GitHub Actions construit les images `web` et `agent`, les publie sur `ghcr.io`, puis se connecte au VPS en SSH pour lancer `docker compose pull && up -d`. Le VPS ne compile rien.

```
push main ─► build (tests) ─► deploy : images ghcr.io ─► SSH VPS ─► docker compose up
```

Sur le VPS, dans `/opt/infrahub` : `docker-compose.yml`, `deploy/Caddyfile` (copiés à chaque déploiement), `.env` (créé une fois, jamais commité) et `sauvegardes/`.

Services : `caddy` (TLS automatique, mot de passe provisoire), `web`, `agent`, `db` (PostgreSQL 17), `sauvegarde` (pg_dump quotidien, 14 jours).

## 1. VPS

Dans hPanel → VPS : système **Ubuntu 24.04** (le modèle « Ubuntu 24.04 with Docker » convient aussi), et ta clé SSH personnelle ajoutée à root.
Si le pare-feu hPanel est activé, ouvrir 22, 80 et 443 (TCP) et 443 (UDP).

## 2. Clé de déploiement

Sur ton poste, une clé dédiée à GitHub Actions (sans phrase de passe) :

```bash
ssh-keygen -t ed25519 -N "" -C "infra-hub deploy" -f ~/.ssh/infrahub_deploy
```

## 3. Préparer le serveur

```bash
ssh root@<IP_DU_VPS> "curl -fsSL https://raw.githubusercontent.com/ippous/infra-hub/main/deploy/preparer-vps.sh | bash -s -- '$(cat ~/.ssh/infrahub_deploy.pub)'"
```

Le script installe Docker, crée l'utilisateur `deploy`, ferme tout sauf SSH/HTTP/HTTPS (ufw), interdit la connexion SSH par mot de passe, active fail2ban et les mises à jour de sécurité automatiques. Il peut être relancé sans risque.

> Avant de le lancer, vérifie que ta propre clé SSH fonctionne pour root : après le script, les mots de passe SSH sont refusés.

## 4. DNS

Dans la zone DNS de `jaume-eric.com` : enregistrement **A** `infra` → IP du VPS (et **AAAA** si le VPS a une IPv6). Caddy obtiendra le certificat au premier démarrage.

## 5. Fichier .env

```bash
docker run --rm caddy:2 caddy hash-password          # tape le mot de passe, copie le hash
ssh deploy@<IP_DU_VPS>
nano /opt/infrahub/.env                              # modèle : .env.example
chmod 600 /opt/infrahub/.env
```

Garde les apostrophes autour du hash (il contient des `$`). Génère le mot de passe PostgreSQL avec `openssl rand -base64 32` et range-le dans ton gestionnaire de mots de passe.

## 6. GitHub

Dépôt → Settings → Secrets and variables → Actions :

| Onglet | Nom | Valeur |
|---|---|---|
| Variables | `VPS_HOST` | IP du VPS |
| Variables | `VPS_USER` | `deploy` |
| Secrets | `VPS_SSH_KEY` | contenu de `~/.ssh/infrahub_deploy` (clé privée) |
| Secrets | `VPS_KNOWN_HOSTS` | sortie de `ssh-keyscan <IP_DU_VPS>` |

Puis Actions → **deploy** → *Run workflow* (les déploiements suivants partent seuls après chaque build réussi sur `main`). Tant que `VPS_HOST` n'existe pas, le workflow publie seulement les images.

## Exploitation

```bash
cd /opt/infrahub
docker compose ps
docker compose logs -f web
gunzip -c sauvegardes/infrahub-AAAAMMJJ-HHMM.sql.gz | docker compose exec -T db psql -U infrahub -d infrahub   # restauration
```

Revenir à une version précédente : relancer le workflow **deploy** sur un ancien commit, ou sur le VPS `INFRAHUB_TAG=<sha> docker compose up -d`.

## À savoir

- **Mot de passe provisoire** : `basic_auth` de Caddy protège tout le site. Il sera remplacé par l'authentification de l'application (indispensable avant d'ouvrir le MCP à claude.ai).
- **Sauvegardes** : elles restent sur le VPS. Les copier ailleurs (NAS via Tailscale, étape 4) est prévu.
