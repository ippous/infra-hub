using System.Text.Json;
using InfraHub.Core.Domaine;

namespace InfraHub.Core.Services;

/// <summary>Critères de la liste d'inventaire. Tous facultatifs.</summary>
public sealed record FiltreElements(
    string? Texte = null,
    Categorie? Categorie = null,
    string? TypeCode = null,
    string? Etiquette = null,
    StatutElement? Statut = null,
    EtatSante? Etat = null);

/// <summary>Ligne de la liste d'inventaire.</summary>
public sealed record ResumeElement(
    Guid Id,
    string Slug,
    string Nom,
    string TypeCode,
    string TypeLibelle,
    string? TypeIcone,
    Categorie Categorie,
    StatutElement Statut,
    EtatSante EtatCourant,
    IReadOnlyList<string> Etiquettes,
    DateTimeOffset ModifieLe);

/// <summary>Sens d'une relation vu depuis l'élément affiché.</summary>
public enum SensRelation
{
    Sortante,
    Entrante,
}

/// <summary>Relation telle qu'affichée sur une fiche : « {Verbe} {Autre} ».</summary>
public sealed record RelationAffichee(
    Guid Id,
    SensRelation Sens,
    string TypeCode,
    string Verbe,
    Guid AutreId,
    string AutreSlug,
    string AutreNom,
    string AutreTypeLibelle,
    OrigineRelation Origine);

public sealed record NoteAffichee(Guid Id, string Contenu, Auteur Auteur, DateTimeOffset CreeLe);

public sealed record LienAffiche(Guid Id, string Libelle, Uri Url);

public sealed record SecretAffiche(Guid Id, string Libelle, string Reference);

/// <summary>Tout ce qu'affiche la fiche d'un élément.</summary>
public sealed record FicheElement(
    ResumeElement Resume,
    string? Description,
    JsonDocument Attributs,
    JsonDocument? SchemaAttributs,
    string? Source,
    string? IdExterne,
    DateTimeOffset? EtatObserveLe,
    DateTimeOffset CreeLe,
    IReadOnlyList<RelationAffichee> Relations,
    IReadOnlyList<NoteAffichee> Notes,
    IReadOnlyList<LienAffiche> Liens,
    IReadOnlyList<SecretAffiche> Secrets);

/// <summary>Nœud de l'arborescence construite par les relations « fait partie de ».</summary>
public sealed record NoeudArbre(ResumeElement Element, IReadOnlyList<NoeudArbre> Enfants);

/// <summary>Champs modifiables d'un élément. Attributs null = inchangés.</summary>
public sealed record ModificationElement(
    string Nom,
    string Slug,
    string? Description,
    StatutElement Statut,
    IReadOnlyList<string> Etiquettes,
    JsonDocument? Attributs = null);
