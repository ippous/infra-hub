using InfraHub.Core.Donnees;
using InfraHub.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace InfraHub.Tests;

/// <summary>Horloge réglable pour les tests.</summary>
internal sealed class HorlogeDeTest(DateTimeOffset depart) : TimeProvider
{
    public DateTimeOffset Maintenant { get; set; } = depart;

    public override DateTimeOffset GetUtcNow() => Maintenant;

    public void Avancer(TimeSpan duree) => Maintenant += duree;
}

/// <summary>
/// Base PostgreSQL jetable, créée et migrée pour chaque test.
/// Les tests sont ignorés si la variable INFRAHUB_TEST_PG (chaîne de connexion d'un serveur de test) est absente.
/// </summary>
public abstract class BaseDeTest : IAsyncLifetime
{
    private readonly string _serveur;
    private readonly string _nomBase = $"infrahub_test_{Guid.NewGuid():N}";
    private DbContextOptions<InfraHubDbContext>? _options;

    internal HorlogeDeTest Horloge { get; } = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    internal ContexteOperation Contexte { get; } = new();

    /// <summary>Fabrique passée aux services : chaque opération obtient un contexte neuf.</summary>
    internal IDbContextFactory<InfraHubDbContext> Fabrique => new FabriqueDeTest(this);

    private sealed class FabriqueDeTest(BaseDeTest test) : IDbContextFactory<InfraHubDbContext>
    {
        public InfraHubDbContext CreateDbContext() => test.NouveauContexte();
    }

    protected BaseDeTest()
    {
        _serveur = Environment.GetEnvironmentVariable("INFRAHUB_TEST_PG") ?? "";
    }

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(string.IsNullOrWhiteSpace(_serveur), "INFRAHUB_TEST_PG non renseignée : tests PostgreSQL ignorés.");

        await ExecuterSurServeurAsync($"CREATE DATABASE \"{_nomBase}\"");
        var chaine = new NpgsqlConnectionStringBuilder(_serveur) { Database = _nomBase }.ConnectionString;
        _options = new DbContextOptionsBuilder<InfraHubDbContext>().UseNpgsql(chaine).Options;

        await using var db = NouveauContexte();
        await new InitialiseurBase(db).InitialiserAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_options is null)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await ExecuterSurServeurAsync($"DROP DATABASE IF EXISTS \"{_nomBase}\" WITH (FORCE)");
    }

    /// <summary>Nouveau contexte, sans suivi partagé avec les précédents (comme une nouvelle requête).</summary>
    internal InfraHubDbContext NouveauContexte() => new(_options!, Contexte, Horloge);

    internal static IOptions<OptionsObservations> Retention(int jours = 90) =>
        Options.Create(new OptionsObservations { RetentionJours = jours });

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task ExecuterSurServeurAsync(string sql)
    {
        await using var connexion = new NpgsqlConnection(_serveur);
        await connexion.OpenAsync();
        await using var commande = new NpgsqlCommand(sql, connexion);
        await commande.ExecuteNonQueryAsync();
    }
}
