namespace InfraHub.Core.Domaine;

/// <summary>Grande famille d'un type d'élément.</summary>
public enum Categorie
{
    Environnement,
    Developpement,
}

/// <summary>Cycle de vie d'un élément, décidé par l'humain.</summary>
public enum StatutElement
{
    Actif,
    EnProjet,
    Archive,
}

/// <summary>Santé observée d'un élément, calculée par les collectes.</summary>
public enum EtatSante
{
    Inconnu,
    Ok,
    Degrade,
    Ko,
}

/// <summary>Qui a créé une relation.</summary>
public enum OrigineRelation
{
    Manuelle,
    Collecte,
}

/// <summary>Auteur d'une note.</summary>
public enum Auteur
{
    Moi,
    Assistant,
    Agent,
}

/// <summary>Surface par laquelle une modification est arrivée.</summary>
public enum Canal
{
    Ui,
    Api,
    Mcp,
    Agent,
    Systeme,
}

/// <summary>Nature d'une entrée du journal d'audit.</summary>
public enum ActionAudit
{
    Creation,
    Modification,
    Suppression,
}
