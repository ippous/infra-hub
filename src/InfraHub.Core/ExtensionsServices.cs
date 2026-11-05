using InfraHub.Core.Donnees;
using InfraHub.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InfraHub.Core;

public static class ExtensionsServices
{
    /// <summary>Enregistre la base PostgreSQL et les services métier.</summary>
    public static IServiceCollection AjouterInfraHubCore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var chaine = configuration.GetConnectionString("InfraHub");
        if (string.IsNullOrWhiteSpace(chaine))
        {
            throw new InvalidOperationException("La chaîne de connexion « ConnectionStrings:InfraHub » n'est pas renseignée.");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ContexteOperation>();
        services.AddDbContext<InfraHubDbContext>(o => o.UseNpgsql(chaine));
        services.Configure<OptionsObservations>(configuration.GetSection(OptionsObservations.Section));

        services.AddScoped<ServiceInventaire>();
        services.AddScoped<ServiceObservations>();
        services.AddScoped<InitialiseurBase>();
        return services;
    }
}
