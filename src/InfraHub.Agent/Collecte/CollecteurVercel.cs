using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using InfraHub.Core.Services.Collecte;
using Microsoft.Extensions.Options;

namespace InfraHub.Agent.Collecte;

public sealed class OptionsVercel
{
    public const string Section = "Vercel";

    /// <summary>Jeton d'API Vercel (vercel.com/account/settings/tokens). Vide = collecteur désactivé.</summary>
    public string? Jeton { get; set; }

    /// <summary>Identifiant d'équipe (team_…). Vide = compte personnel.</summary>
    public string? Equipe { get; set; }

    public int IntervalleMinutes { get; set; } = 15;

    /// <summary>Adresse de l'API (modifiable pour les essais).</summary>
    public Uri Adresse { get; set; } = new("https://api.vercel.com");
}

/// <summary>Lit les projets, domaines et déploiements de production de Vercel, puis les reporte dans l'inventaire.</summary>
internal sealed class CollecteurVercel(IHttpClientFactory http, IOptions<OptionsVercel> options) : ICollecteur
{
    public const string ClientHttp = "vercel";

    private readonly OptionsVercel config = options.Value;

    public string Nom => "vercel";

    public TimeSpan Intervalle => TimeSpan.FromMinutes(Math.Max(5, config.IntervalleMinutes));

    public bool Actif => !string.IsNullOrWhiteSpace(config.Jeton);

    public async Task<string> ExecuterAsync(IServiceProvider services, CancellationToken ct)
    {
        var client = http.CreateClient(ClientHttp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Jeton!.Trim());

        var projets = await LireProjetsAsync(client, ct).ConfigureAwait(false);
        var rapport = await services.GetRequiredService<SynchroVercel>().SynchroniserAsync(projets, config.Equipe, ct).ConfigureAwait(false);

        var resume = $"{projets.Count} projet(s)";
        if (rapport.Crees.Count > 0) resume += $", créés : {string.Join(", ", rapport.Crees)}";
        if (rapport.Rattaches.Count > 0) resume += $", rattachés : {string.Join(", ", rapport.Rattaches)}";
        if (rapport.Modifies.Count > 0) resume += $", modifiés : {string.Join(", ", rapport.Modifies)}";
        if (rapport.Erreurs.Count > 0) resume += $", erreurs : {string.Join(" | ", rapport.Erreurs)}";
        return resume;
    }

    private async Task<List<ProjetVercel>> LireProjetsAsync(HttpClient client, CancellationToken ct)
    {
        var projets = new List<ProjetVercel>();
        string? suivant = null;
        do
        {
            using var page = await GetAsync(client, $"/v9/projects?limit=100{(suivant is null ? "" : $"&until={suivant}")}", ct).ConfigureAwait(false);
            foreach (var p in page.RootElement.GetProperty("projects").EnumerateArray())
            {
                projets.Add(await LireProjetAsync(client, p, ct).ConfigureAwait(false));
            }

            suivant = page.RootElement.TryGetProperty("pagination", out var pg) && pg.TryGetProperty("next", out var n)
                && n.ValueKind == JsonValueKind.Number ? n.GetRawText() : null;
        }
        while (suivant is not null);

        return projets;
    }

    private async Task<ProjetVercel> LireProjetAsync(HttpClient client, JsonElement p, CancellationToken ct)
    {
        var id = p.GetProperty("id").GetString()!;

        using var domaines = await GetAsync(client, $"/v9/projects/{id}/domains?limit=100", ct).ConfigureAwait(false);
        var listeDomaines = domaines.RootElement.GetProperty("domains").EnumerateArray()
            .Where(d => Texte(d, "redirect") is null)
            .Select(d => new DomaineVercel(Texte(d, "name")!, Texte(d, "apexName"), Texte(d, "gitBranch")))
            .ToList();

        using var deps = await GetAsync(client, $"/v6/deployments?projectId={id}&target=production&limit=1", ct).ConfigureAwait(false);
        DeploiementVercel? production = null;
        string? depotMeta = null;
        if (deps.RootElement.GetProperty("deployments").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } d0)
        {
            var meta = d0.TryGetProperty("meta", out var m) ? m : default;
            production = new DeploiementVercel(
                Texte(d0, "uid") ?? Texte(d0, "id") ?? "",
                Texte(d0, "state") ?? Texte(d0, "readyState") ?? "",
                DateTimeOffset.FromUnixTimeMilliseconds(d0.TryGetProperty("created", out var c) && c.TryGetInt64(out var ms) ? ms : 0),
                Texte(d0, "url"),
                Texte(meta, "githubCommitSha"),
                Texte(meta, "githubCommitMessage"),
                Texte(d0, "inspectorUrl"));
            if (Texte(meta, "githubOrg") is { } org && Texte(meta, "githubRepo") is { } repo)
            {
                depotMeta = $"{org}/{repo}";
            }
        }

        var lien = p.TryGetProperty("link", out var l) ? l : default;
        var depot = Texte(lien, "type") == "github" && Texte(lien, "org") is { } o && Texte(lien, "repo") is { } r ? $"{o}/{r}" : depotMeta;

        return new ProjetVercel(id, Texte(p, "name")!, Texte(p, "framework"), Texte(p, "nodeVersion"), depot, listeDomaines, production);
    }

    private async Task<JsonDocument> GetAsync(HttpClient client, string chemin, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(config.Equipe))
        {
            chemin += (chemin.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "teamId=" + Uri.EscapeDataString(config.Equipe.Trim());
        }

        using var reponse = await client.GetAsync(new Uri(chemin, UriKind.Relative), ct).ConfigureAwait(false);
        if (reponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException($"Vercel refuse le jeton ({(int)reponse.StatusCode}) : jeton expiré, révoqué ou sans accès à l'équipe.");
        }

        reponse.EnsureSuccessStatusCode();
        await using var flux = await reponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(flux, cancellationToken: ct).ConfigureAwait(false);
    }

    private static string? Texte(JsonElement e, string cle) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(cle, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
