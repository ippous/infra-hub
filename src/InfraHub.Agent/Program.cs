using System.Text.Json;
using InfraHub.Agent;
using InfraHub.Agent.Collecte;
using InfraHub.Core;
using InfraHub.Core.Domaine;
using InfraHub.Core.Donnees;
using InfraHub.Core.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AjouterInfraHubCore(builder.Configuration);

// Commande ponctuelle : importer un inventaire JSON lu sur l'entrée standard, puis s'arrêter.
//   docker compose exec -T agent dotnet InfraHub.Agent.dll importer < inventaire.json
if (args.Length > 0 && args[0] == "importer")
{
    builder.Logging.ClearProviders(); // seul le rapport d'import s'affiche
    using var hoteImport = builder.Build();
    return await Importer(hoteImport.Services);
}

// Collecteurs : chacun a son intervalle ; ceux sans configuration (jeton…) sont ignorés.
builder.Services.Configure<OptionsVercel>(builder.Configuration.GetSection(OptionsVercel.Section));
builder.Services.AddHttpClient(CollecteurVercel.ClientHttp, (sp, c) =>
{
    c.BaseAddress = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OptionsVercel>>().Value.Adresse;
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("InfraHub-Agent/1.0");
});
builder.Services.AddSingleton<ICollecteur, CollecteurVercel>();
builder.Services.AddSingleton<ICollecteur, PurgeObservations>();
builder.Services.AddHostedService<CollecteWorker>();

// À venir : GitHub, Hostinger, Synology DSM (via Tailscale), sondes HTTP / TLS, conteneurs Docker du VPS.

var host = builder.Build();
await host.RunAsync();
return 0;

static async Task<int> Importer(IServiceProvider services)
{
    DocumentImport? document;
    try
    {
        document = await JsonSerializer.DeserializeAsync<DocumentImport>(Console.OpenStandardInput(), DocumentImport.Options);
    }
    catch (JsonException ex)
    {
        await Console.Error.WriteLineAsync($"Fichier d'import illisible : {ex.Message}");
        return 2;
    }

    if (document is null)
    {
        await Console.Error.WriteLineAsync("Fichier d'import vide.");
        return 2;
    }

    await using var scope = services.CreateAsyncScope();
    scope.ServiceProvider.GetRequiredService<ContexteOperation>().Canal = Canal.Import;
    var rapport = await scope.ServiceProvider.GetRequiredService<ServiceImport>().ImporterAsync(document);

    Console.WriteLine($"Créés ({rapport.Crees.Count}) : {string.Join(", ", rapport.Crees)}");
    Console.WriteLine($"Déjà présents ({rapport.DejaPresents.Count}) : {string.Join(", ", rapport.DejaPresents)}");
    Console.WriteLine($"Relations ({rapport.Relations.Count}) :");
    rapport.Relations.ForEach(r => Console.WriteLine($"  {r}"));
    if (rapport.Erreurs.Count > 0)
    {
        Console.WriteLine($"Erreurs ({rapport.Erreurs.Count}) :");
        rapport.Erreurs.ForEach(e => Console.WriteLine($"  {e}"));
        return 1;
    }

    return 0;
}
