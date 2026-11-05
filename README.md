# Infra Hub

Inventaire vivant de mes **environnements** (VPS, NAS, PaaS, bases managées, domaines…) et de mes **développements** (sites, applis, code source, outils), avec leurs relations, leur état et un assistant IA.

> État : squelette initial. Les fonctionnalités arrivent après validation du schéma de données.

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

Prérequis : SDK .NET 11 (RC3 ou version finale).

```bash
dotnet build InfraHub.slnx
dotnet run --project src/InfraHub.Web
```

## Déployer sur le VPS

```bash
cp .env.example .env    # puis renseigner le domaine et le mot de passe PostgreSQL
docker compose up -d --build
```

Le DNS du domaine choisi doit pointer vers l'IP du VPS ; Caddy obtient le certificat tout seul.
Avant la sortie officielle de .NET 11 (10 novembre 2026), l'étiquette d'image `11.0` peut ne pas exister : passer `--build-arg DOTNET_TAG=<tag RC>` si besoin.

## Feuille de route

1. Socle : inventaire, relations, notes, interface
2. MCP en lecture seule + assistant
3. Agent : sondes HTTP / TLS, Docker du VPS, GitHub, Vercel
4. Tailscale + Synology DSM, puis Hostinger
5. Outils d'écriture, actions avec validation, alertes téléphone
