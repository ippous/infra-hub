using InfraHub.Agent;
using InfraHub.Core;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AjouterInfraHubCore(builder.Configuration);
builder.Services.AddHostedService<CollecteWorker>();

// À venir : collecteurs GitHub, Vercel, Hostinger, Synology DSM (via Tailscale),
// sondes HTTP / TLS, état des conteneurs Docker du VPS.

var host = builder.Build();
await host.RunAsync();
