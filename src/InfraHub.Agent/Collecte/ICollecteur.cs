namespace InfraHub.Agent.Collecte;

/// <summary>Tâche périodique de l'agent. Chaque exécution a sa propre portée de services (canal d'audit « Agent »).</summary>
internal interface ICollecteur
{
    string Nom { get; }

    TimeSpan Intervalle { get; }

    /// <summary>Faux si la configuration manque (jeton…) : le collecteur est alors ignoré.</summary>
    bool Actif { get; }

    /// <summary>Retourne un résumé d'une ligne pour le journal.</summary>
    Task<string> ExecuterAsync(IServiceProvider services, CancellationToken ct);
}
