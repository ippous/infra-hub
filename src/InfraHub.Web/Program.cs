using InfraHub.Core;
using InfraHub.Core.Domaine;
using InfraHub.Core.Donnees;
using InfraHub.Core.Services;
using InfraHub.Web.Components;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Interface Blazor (rendu serveur interactif)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHealthChecks();

// Derrière Caddy : schéma et IP d'origine transmis par les en-têtes X-Forwarded-*.
// Le réseau Docker interne n'est pas connu à l'avance, on fait confiance au proxy du réseau privé.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Clés Data Protection persistées (volume Docker) : antiforgery et circuits survivent aux redéploiements.
if (builder.Configuration["DataProtection:Dossier"] is { Length: > 0 } dossierCles)
{
    builder.Services.AddDataProtection()
        .SetApplicationName("InfraHub")
        .PersistKeysToFileSystem(new DirectoryInfo(dossierCles));
}

// Base PostgreSQL et services métier partagés
builder.Services.AjouterInfraHubCore(builder.Configuration);

// Les modifications faites depuis l'interface sont journalisées avec le canal « Ui ».
// (Les futures routes /api et /mcp fixeront leur propre canal dans leur portée.)
builder.Services.AddScoped(_ => new ContexteOperation { Canal = Canal.Ui });

// À venir :
// - API REST sous /api
// - serveur MCP sous /mcp (même processus)

var app = builder.Build();

app.UseForwardedHeaders();

// Seul le web applique les migrations : l'agent attend que le schéma soit en place.
await using (var scope = app.Services.CreateAsyncScope())
{
    scope.ServiceProvider.GetRequiredService<ContexteOperation>().Canal = Canal.Systeme;
    await scope.ServiceProvider.GetRequiredService<InitialiseurBase>().InitialiserAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Caddy termine le TLS en production ; la redirection HTTPS ne sert qu'en local.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
