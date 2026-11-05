using System.Text.Json;

namespace InfraHub.Core.Domaine;

/// <summary>Tout ce qu'on inventorie : environnement ou développement.</summary>
public sealed class Element : IHorodate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string TypeCode { get; set; }
    public TypeElement? Type { get; set; }

    public required string Nom { get; set; }

    /// <summary>Identifiant lisible et unique, utilisé dans les URL et par le MCP.</summary>
    public required string Slug { get; set; }

    public StatutElement Statut { get; set; } = StatutElement.Actif;
    public string? Description { get; set; }
    public List<string> Etiquettes { get; set; } = [];

    /// <summary>Attributs propres au type, validés par <see cref="TypeElement.SchemaAttributs"/>.</summary>
    public JsonDocument Attributs { get; set; } = JsonDocument.Parse("{}");

    /// <summary>Source de collecte (github, vercel…) ; null pour un élément saisi à la main.</summary>
    public string? Source { get; set; }

    /// <summary>Identifiant chez la source ; unique avec <see cref="Source"/>, pour que l'agent mette à jour sans dupliquer.</summary>
    public string? IdExterne { get; set; }

    /// <summary>Copie de l'état de la dernière observation.</summary>
    public EtatSante EtatCourant { get; set; } = EtatSante.Inconnu;
    public DateTimeOffset? EtatObserveLe { get; set; }

    public DateTimeOffset CreeLe { get; set; }
    public DateTimeOffset ModifieLe { get; set; }

    public List<Relation> RelationsSortantes { get; } = [];
    public List<Relation> RelationsEntrantes { get; } = [];
    public List<Note> Notes { get; } = [];
    public List<Lien> Liens { get; } = [];
    public List<ReferenceSecret> Secrets { get; } = [];
}
