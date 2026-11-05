using System.Text.Json;

namespace InfraHub.Core.Domaine;

/// <summary>Résultat d'une collecte sur un élément. Historique purgé après la durée de rétention.</summary>
public sealed class Observation
{
    public long Id { get; set; }
    public Guid ElementId { get; set; }
    public Element? Element { get; set; }

    /// <summary>Nom du collecteur (http, tls, docker, github…).</summary>
    public required string Collecteur { get; set; }
    public DateTimeOffset Horodatage { get; set; }
    public EtatSante Statut { get; set; }

    /// <summary>Mesures brutes : latence, expiration TLS, version…</summary>
    public JsonDocument Mesures { get; set; } = JsonDocument.Parse("{}");
}
