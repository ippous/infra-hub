using InfraHub.Core.Services;

namespace InfraHub.Agent.Collecte;

/// <summary>Supprime les observations plus anciennes que la rétention (90 jours par défaut).</summary>
internal sealed class PurgeObservations : ICollecteur
{
    public string Nom => "purge";

    public TimeSpan Intervalle => TimeSpan.FromHours(1);

    public bool Actif => true;

    public async Task<string> ExecuterAsync(IServiceProvider services, CancellationToken ct)
    {
        var n = await services.GetRequiredService<ServiceObservations>().PurgerAsync(ct).ConfigureAwait(false);
        return $"{n} observation(s) purgée(s)";
    }
}
