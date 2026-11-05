using System.Text.Json;
using InfraHub.Core.Domaine;

namespace InfraHub.Core.Services;

/// <summary>Types d'élément proposés au premier démarrage. Ils restent ensuite modifiables.</summary>
public static class TypesElementInitiaux
{
    public static IReadOnlyList<TypeElement> Creer() =>
    [
        Type("vps", "VPS", Categorie.Environnement, "server",
            """{"fournisseur":{"type":"string"},"ip":{"type":"string","format":"ipv4"},"os":{"type":"string"},"region":{"type":"string"},"offre":{"type":"string"}}"""),
        Type("nas", "NAS", Categorie.Environnement, "hard-drive",
            """{"modele":{"type":"string"},"ip":{"type":"string"},"version_dsm":{"type":"string"},"adresse_tailscale":{"type":"string"}}"""),
        Type("paas", "PaaS", Categorie.Environnement, "cloud",
            """{"fournisseur":{"type":"string"},"equipe":{"type":"string"}}"""),
        Type("base", "Base de données managée", Categorie.Environnement, "database",
            """{"moteur":{"type":"string"},"version":{"type":"string"},"fournisseur":{"type":"string"}}"""),
        Type("domaine", "Nom de domaine", Categorie.Environnement, "globe",
            """{"registrar":{"type":"string"},"expiration":{"type":"string","format":"date"},"renouvellement_auto":{"type":"boolean"}}"""),
        Type("compte", "Compte de service", Categorie.Environnement, "user",
            """{"service":{"type":"string"},"identifiant":{"type":"string"}}"""),
        Type("conteneur", "Conteneur", Categorie.Environnement, "box",
            """{"image":{"type":"string"},"ports":{"type":"array","items":{"type":"string"}}}"""),
        Type("site", "Site web", Categorie.Developpement, "layout",
            """{"url":{"type":"string","format":"uri"},"framework":{"type":"string"}}"""),
        Type("appli", "Application", Categorie.Developpement, "app-window",
            """{"url":{"type":"string","format":"uri"},"pile":{"type":"string"}}"""),
        Type("depot", "Dépôt de code", Categorie.Developpement, "git-branch",
            """{"url":{"type":"string","format":"uri"},"branche_defaut":{"type":"string"},"visibilite":{"enum":["public","prive"]}}"""),
        Type("outil", "Outil", Categorie.Developpement, "wrench",
            """{"usage":{"type":"string"}}"""),
    ];

    private static TypeElement Type(string code, string libelle, Categorie categorie, string icone, string proprietes) => new()
    {
        Code = code,
        Libelle = libelle,
        Categorie = categorie,
        Icone = icone,
        SchemaAttributs = JsonDocument.Parse(
            $$"""{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{{proprietes}}}"""),
    };
}

/// <summary>Verbes de relation proposés au premier démarrage.</summary>
public static class TypesRelationInitiaux
{
    public const string HebergeSur = "heberge_sur";
    public const string DeployeSur = "deploye_sur";
    public const string DependDe = "depend_de";
    public const string CodeDans = "code_dans";
    public const string PointeVers = "pointe_vers";
    public const string SauvegardeVers = "sauvegarde_vers";
    public const string FaitPartieDe = "fait_partie_de";

    public static IReadOnlyList<TypeRelation> Creer() =>
    [
        new() { Code = HebergeSur, Libelle = "est hébergé sur", LibelleInverse = "héberge" },
        new() { Code = DeployeSur, Libelle = "est déployé sur", LibelleInverse = "déploie" },
        new() { Code = DependDe, Libelle = "dépend de", LibelleInverse = "est requis par" },
        new() { Code = CodeDans, Libelle = "a son code dans", LibelleInverse = "contient le code de" },
        new() { Code = PointeVers, Libelle = "pointe vers", LibelleInverse = "reçoit le domaine" },
        new() { Code = SauvegardeVers, Libelle = "est sauvegardé vers", LibelleInverse = "reçoit la sauvegarde de" },
        new() { Code = FaitPartieDe, Libelle = "fait partie de", LibelleInverse = "contient" },
    ];
}
