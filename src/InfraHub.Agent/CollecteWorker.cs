using InfraHub.Core.Domaine;
using InfraHub.Core.Donnees;
using InfraHub.Core.Services;

namespace InfraHub.Agent;

/// <summary>
/// Boucle de collecte planifiée. Pour l'instant, elle purge l'historique des observations ;
/// les collecteurs viendront s'y ajouter.
/// </summary>
internal sealed partial class CollecteWorker(IServiceScopeFactory scopes, ILogger<CollecteWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalle = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogDemarrage(logger, Intervalle);

        using var timer = new PeriodicTimer(Intervalle);
        do
        {
            await ExecuterCycleAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task ExecuterCycleAsync(CancellationToken ct)
    {
        LogCycle(logger, DateTimeOffset.UtcNow);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ContexteOperation>().Canal = Canal.Agent;

            // TODO : exécuter les collecteurs enregistrés.

            var purgees = await scope.ServiceProvider.GetRequiredService<ServiceObservations>().PurgerAsync(ct).ConfigureAwait(false);
            if (purgees > 0)
            {
                LogPurge(logger, purgees);
            }
        }
#pragma warning disable CA1031 // Un cycle raté (base pas encore migrée, réseau…) ne doit pas arrêter l'agent.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogEchec(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent de collecte démarré (intervalle : {Intervalle}).")]
    private static partial void LogDemarrage(ILogger logger, TimeSpan intervalle);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cycle de collecte à {Horodatage:o}.")]
    private static partial void LogCycle(ILogger logger, DateTimeOffset horodatage);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Nombre} observations anciennes purgées.")]
    private static partial void LogPurge(ILogger logger, int nombre);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cycle de collecte en échec.")]
    private static partial void LogEchec(ILogger logger, Exception ex);
}
