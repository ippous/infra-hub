using InfraHub.Core;
using InfraHub.Core.Services;
using InfraHub.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Interface Blazor (rendu serveur interactif)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHealthChecks();

// Base PostgreSQL et services métier partagés
builder.Services.AjouterInfraHubCore(builder.Configuration);

// À venir :
// - API REST sous /api
// - serveur MCP sous /mcp (même processus)

var app = builder.Build();

// Seul le web applique les migrations : l'agent attend que le schéma soit en place.
await using (var scope = app.Services.CreateAsyncScope())
{
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
