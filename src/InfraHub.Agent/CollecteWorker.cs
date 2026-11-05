namespace InfraHub.Agent;

/// <summary>
/// Boucle de collecte planifiée. Pour l'instant, elle se contente de signaler qu'elle tourne.
/// </summary>
internal sealed partial class CollecteWorker(ILogger<CollecteWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalle = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogDemarrage(logger, Intervalle);

        using var timer = new PeriodicTimer(Intervalle);
        do
        {
            LogCycle(logger, DateTimeOffset.UtcNow);
            // TODO : exécuter les collecteurs enregistrés.
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent de collecte démarré (intervalle : {Intervalle}).")]
    private static partial void LogDemarrage(ILogger logger, TimeSpan intervalle);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cycle de collecte à {Horodatage:o}.")]
    private static partial void LogCycle(ILogger logger, DateTimeOffset horodatage);
}
