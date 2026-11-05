# Schéma de données

Toutes les tables vivent dans PostgreSQL, en snake_case. Le code est dans `src/InfraHub.Core` (`Domaine/` pour les entités, `Donnees/` pour EF Core et les migrations).

| Table | Rôle | Champs principaux |
|---|---|---|
| `type_element` | Catalogue des types, modifiable depuis l'interface | `code`, `libelle`, `categorie` (Environnement / Developpement), `schema_attributs` (JSON Schema), `icone` |
| `element` | Tout ce qu'on inventorie | `id`, `type_code`, `nom`, `slug` (unique), `statut`, `description`, `etiquettes` (text[]), `attributs` (jsonb), `source` + `id_externe` (uniques ensemble), `etat_courant`, `etat_observe_le`, `cree_le`, `modifie_le` |
| `type_relation` | Verbes des relations | `code`, `libelle`, `libelle_inverse` |
| `relation` | Lien orienté entre deux éléments | `source_id`, `cible_id`, `type_code`, `attributs` (jsonb), `origine` (Manuelle / Collecte) ; unique sur (source, cible, type) |
| `note` | Journal libre d'un élément | `element_id`, `contenu` (markdown), `auteur` (Moi / Assistant / Agent) |
| `lien` | URL utile d'un élément | `element_id`, `libelle`, `url` |
| `reference_secret` | Où trouver un secret, jamais sa valeur | `element_id`, `libelle`, `reference` (ex. `op://Infra/VPS/ssh`) |
| `observation` | Historique des collectes | `element_id`, `collecteur`, `horodatage`, `statut`, `mesures` (jsonb) |
| `audit` | Qui a changé quoi | `entite`, `entite_id`, `action`, `avant` / `apres` (jsonb), `canal` (Ui / Api / Mcp / Agent / Systeme), `horodatage` |

## Règles

- **Attributs** : chaque type porte un JSON Schema (draft 2020-12, formats vérifiés). Les attributs d'un élément sont validés contre lui à la création et à la modification. Les propriétés non décrites restent acceptées.
- **Hiérarchie** : pas de colonne parent ; la relation `fait_partie_de` construit l'arborescence. Les boucles sont refusées.
- **Suppression** : supprimer un élément supprime ses relations, notes, liens, références de secret et observations. Un type utilisé ne peut pas être supprimé.
- **Audit** : écrit automatiquement à chaque enregistrement pour les types, éléments, relations, notes, liens et références de secret. Une modification ne garde que les champs changés. L'état de santé (mis à jour par les collectes) n'est pas journalisé.
- **Observations** : chaque enregistrement met à jour `etat_courant` de l'élément. L'agent purge l'historique au-delà de `Observations:RetentionJours` (90 par défaut).
- **Types initiaux** : au démarrage, le web applique les migrations puis ajoute les types et verbes manquants, sans toucher à ceux qui ont été modifiés.

## Types initiaux

Environnements : `vps`, `nas`, `paas`, `base`, `domaine`, `compte`, `conteneur`.
Développements : `site`, `appli`, `depot`, `outil`.

Relations : `heberge_sur`, `deploye_sur`, `depend_de`, `code_dans`, `pointe_vers`, `sauvegarde_vers`, `fait_partie_de`.
