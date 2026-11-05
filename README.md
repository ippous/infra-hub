# Infra Hub

Inventaire vivant de mes **environnements** (VPS, NAS, PaaS, bases managées, domaines…) et de mes **développements** (sites, applis, code source, outils), avec leurs relations, leur état et un assistant IA.

> État : schéma de données ([docs/schema.md](docs/schema.md)) et écrans d'inventaire en place ; déploiement Hostinger prêt ([docs/hebergement.md](docs/hebergement.md)). Prochaine étape : authentification, puis MCP en lecture seule.

## Architecture

```
            ┌── Interface Blazor ──┐
Clients ────┼── API REST (/api)  ──┼──► InfraHub.Core ──► PostgreSQL
            └── MCP (/mcp)       ──┘          ▲
                                              │
                         InfraHub.Agent ──────┘  (collectes planifiées)
```

- **InfraHub.Core** : couche métier partagée (entités, services). L'interface, l'API, le MCP et l'agent l'appellent tous ; aucune logique n'est dupliquée.
- **InfraHub.Web** : ASP.NET Core — interface Blazor, API REST et serveur MCP dans le même processus.
- **InfraHub.Agent** : Worker Service — collectes GitHub, Vercel, Hostinger, Synology DSM, sondes HTTP / TLS, conteneurs Docker du VPS.
- **Assistant intégré** : API Claude + connecteur MCP pointant vers notre propre `/mcp` (même principe que Carnet). Une seule surface d'outils pour l'assistant, Claude Desktop et claude.ai.

### Décisions

| Sujet | Choix |
|---|---|
| Plateforme | .NET 11 / C# 15 |
| Hébergement | VPS Hostinger, Docker Compose |
| Base | PostgreSQL (EF Core, `jsonb` pour les attributs propres à chaque type) |
| TLS / reverse proxy | Caddy |
| Accès aux NAS | Tailscale (VPS ↔ DS920+), l'agent tourne sur le VPS |
| Secrets | Jamais stockés : référence vers le gestionnaire de mots de passe |
| Actions sensibles via MCP | Mises en attente, validées uniquement depuis l'interface |
| Authentification MCP | Jeton de service pour l'assistant, OAuth pour Claude Desktop / claude.ai |

## Structure

```
src/
  InfraHub.Core/     couche métier
  InfraHub.Web/      interface + API + MCP
  InfraHub.Agent/    collectes planifiées
tests/
  InfraHub.Tests/
deploy/
  Dockerfile.web, Dockerfile.agent, Caddyfile
docker-compose.yml
```

## Démarrer en local

Prérequis : SDK .NET 11 (RC3 ou version finale) et un PostgreSQL local (`infrahub` / `infrahub`, voir `appsettings.Development.json`).

```bash
dotnet build InfraHub.slnx
dotnet run --project src/InfraHub.Web    # applique les migrations au démarrage
```

### Tests

Les tests d'intégration créent une base jetable par test sur le serveur indiqué par `INFRAHUB_TEST_PG` ; sans cette variable, ils sont ignorés.

```bash
export INFRAHUB_TEST_PG="Host=localhost;Database=postgres;Username=infrahub;Password=infrahub"
dotnet test --solution InfraHub.slnx
```

### Migrations

```bash
dotnet tool restore
dotnet ef migrations add <Nom> --project src/InfraHub.Core --startup-project src/InfraHub.Core --output-dir Donnees/Migrations
```

## Déployer sur le VPS

Déploiement automatique depuis GitHub Actions (images sur ghcr.io, puis `docker compose` sur le VPS en SSH).
Mise en place pas à pas : [docs/hebergement.md](docs/hebergement.md).

## Feuille de route

1. Socle : inventaire, relations, notes, interface
2. MCP en lecture seule + assistant
3. Agent : sondes HTTP / TLS, Docker du VPS, GitHub, Vercel
4. Tailscale + Synology DSM, puis Hostinger
5. Outils d'écriture, actions avec validation, alertes téléphone
