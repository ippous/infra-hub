using InfraHub.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Interface Blazor (rendu serveur interactif)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHealthChecks();

// À venir :
// - services de InfraHub.Core et EF Core (PostgreSQL)
// - API REST sous /api
// - serveur MCP sous /mcp (même processus)

var app = builder.Build();

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

app.Run();
