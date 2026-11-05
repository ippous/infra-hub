using System.Text.Json;

namespace InfraHub.Core.Domaine;

/// <summary>Trace d'une modification : quoi, comment, par quel canal. Écrite automatiquement à l'enregistrement.</summary>
public sealed class EntreeAudit
{
    public long Id { get; set; }

    /// <summary>Nom de l'entité modifiée (Element, Relation…).</summary>
    public required string Entite { get; set; }

    /// <summary>Clé de l'entité, sous forme texte.</summary>
    public required string EntiteId { get; set; }

    public ActionAudit Action { get; set; }
    public JsonDocument? Avant { get; set; }
    public JsonDocument? Apres { get; set; }
    public Canal Canal { get; set; }
    public DateTimeOffset Horodatage { get; set; }
}
