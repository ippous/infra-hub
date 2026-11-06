using InfraHub.Agent.Collecte;
using InfraHub.Core.Domaine;
using InfraHub.Core.Donnees;

namespace InfraHub.Agent;

/// <summary>
/// Planificateur de l'agent : toutes les minutes, lance les collecteurs dont l'intervalle est écoulé.
/// Chaque exécution a sa propre portée (canal d'audit « Agent ») ; l'échec d'un collecteur n'arrête pas les autres.
/// </summary>
internal sealed partial class CollecteWorker(
    IEnumerable<ICollecteur> collecteurs,
    IServiceScopeFactory scopes,
    TimeProvider horloge,
    ILogger<CollecteWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Battement = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var actifs = collecteurs.Where(c => c.Actif).ToList();
        foreach (var c in collecteurs.Where(c => !c.Actif))
        {
            LogInactif(logger, c.Nom);
        }

        LogDemarrage(logger, string.Join(", ", actifs.Select(c => $"{c.Nom} ({c.Intervalle.TotalMinutes} min)")));
        var prochains = actifs.ToDictionary(c => c, _ => DateTimeOffset.MinValue);

        using var timer = new PeriodicTimer(Battement, horloge);
        do
        {
            foreach (var c in actifs.Where(c => horloge.GetUtcNow() >= prochains[c]))
            {
                prochains[c] = horloge.GetUtcNow() + c.Intervalle;
                await ExecuterAsync(c, stoppingToken).ConfigureAwait(false);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task ExecuterAsync(ICollecteur collecteur, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ContexteOperation>().Canal = Canal.Agent;
            var resume = await collecteur.ExecuterAsync(scope.ServiceProvider, ct).ConfigureAwait(false);
            LogSucces(logger, collecteur.Nom, resume);
        }
#pragma warning disable CA1031 // Un collecteur en échec (réseau, jeton, base pas encore migrée…) ne doit pas arrêter l'agent.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // Erreurs attendues (réseau, jeton refusé) : le message suffit ; les autres gardent la pile d'appels.
            LogEchec(logger, collecteur.Nom, ex.Message, ex is HttpRequestException or InvalidOperationException or TaskCanceledException ? null : ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent démarré. Collecteurs actifs : {Collecteurs}.")]
    private static partial void LogDemarrage(ILogger logger, string collecteurs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Collecteur {Nom} désactivé (configuration absente).")]
    private static partial void LogInactif(ILogger logger, string nom);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nom} : {Resume}.")]
    private static partial void LogSucces(ILogger logger, string nom, string resume);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Nom} en échec : {Message}")]
    private static partial void LogEchec(ILogger logger, string nom, string message, Exception? ex);
}
