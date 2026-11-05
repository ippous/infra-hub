using System.Text.Json;
using InfraHub.Core.Domaine;
using InfraHub.Core.Donnees;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraHub.Core.Services;

public sealed class OptionsObservations
{
    public const string Section = "Observations";

    /// <summary>Durée de conservation de l'historique des observations.</summary>
    public int RetentionJours { get; set; } = 90;
}

/// <summary>Enregistre les résultats des collectes et purge l'historique ancien.</summary>
public sealed class ServiceObservations(InfraHubDbContext db, TimeProvider horloge, IOptions<OptionsObservations> options)
{
    public async Task<Observation> EnregistrerAsync(
        Guid elementId, string collecteur, EtatSante statut, JsonDocument? mesures = null, CancellationToken ct = default)
    {
        var element = await db.Elements.FindAsync([elementId], ct).ConfigureAwait(false)
            ?? throw new ErreurMetier("Élément introuvable.");

        var maintenant = horloge.GetUtcNow();
        var observation = new Observation
        {
            ElementId = elementId,
            Collecteur = collecteur,
            Statut = statut,
            Horodatage = maintenant,
            Mesures = mesures ?? JsonDocument.Parse("{}"),
        };
        db.Observations.Add(observation);

        element.EtatCourant = statut;
        element.EtatObserveLe = maintenant;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return observation;
    }

    /// <summary>Supprime les observations plus anciennes que la rétention. Retourne le nombre supprimé.</summary>
    public Task<int> PurgerAsync(CancellationToken ct = default)
    {
        var limite = horloge.GetUtcNow().AddDays(-options.Value.RetentionJours);
        return db.Observations.Where(o => o.Horodatage < limite).ExecuteDeleteAsync(ct);
    }
}
