using System.Text.Json;
using System.Text.Json.Nodes;
using InfraHub.Core.Domaine;
using InfraHub.Core.Donnees;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Core.Services.Collecte;

/// <summary>Domaine rattaché à un projet Vercel ; <see cref="Branche"/> non nulle = domaine d'une branche (préproduction).</summary>
public sealed record DomaineVercel(string Nom, string? Apex, string? Branche);

/// <summary>Dernier déploiement de production d'un projet.</summary>
public sealed record DeploiementVercel(
    string Id, string Etat, DateTimeOffset Date, string? Url, string? Commit, string? Message, string? InspecteurUrl);

/// <summary>Projet Vercel tel que lu par le collecteur (l'appel HTTP vit dans l'agent).</summary>
public sealed record ProjetVercel(
    string Id,
    string Nom,
    string? Framework,
    string? Node,
    string? DepotGitHub,
    IReadOnlyList<DomaineVercel> Domaines,
    DeploiementVercel? Production);

public sealed class RapportSynchro
{
    public List<string> Crees { get; } = [];
    public List<string> Rattaches { get; } = [];
    public List<string> Modifies { get; } = [];
    public List<string> Erreurs { get; } = [];
}

/// <summary>
/// Reporte l'état d'une équipe Vercel dans l'inventaire.
/// <list type="bullet">
/// <item>Projet → élément (source « vercel », id du projet). Un élément saisi à la main avec l'attribut
/// <c>projet_vercel</c> est rattaché plutôt que dupliqué (de préférence celui dont l'URL est un domaine de production).</item>
/// <item>Dépôt GitHub → élément « depot » (source « github », org/dépôt) ; un dépôt saisi à la main avec la même URL est rattaché.</item>
/// <item>Relations : déployé sur la plateforme Vercel, code dans le dépôt, domaine personnalisé qui pointe vers le projet.</item>
/// <item>Dernier déploiement de production → observation (READY = OK, ERROR = en panne, BLOCKED / CANCELED = dégradé).</item>
/// </list>
/// N'écrit que ce qui change : les passages suivants ne créent que des observations.
/// </summary>
public sealed class SynchroVercel(
    IDbContextFactory<InfraHubDbContext> fabrique,
    ServiceInventaire inventaire,
    ServiceObservations observations)
{
    public const string Source = "vercel";
    public const string SourceGitHub = "github";
    public const string IdPlateforme = "plateforme";

    public async Task<RapportSynchro> SynchroniserAsync(IReadOnlyList<ProjetVercel> projets, string? equipe, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projets);
        var rapport = new RapportSynchro();
        var plateforme = await PlateformeAsync(equipe, rapport, ct).ConfigureAwait(false);

        foreach (var projet in projets)
        {
            try
            {
                await SynchroniserProjetAsync(projet, plateforme, rapport, ct).ConfigureAwait(false);
            }
            catch (ErreurMetier ex)
            {
                rapport.Erreurs.Add($"{projet.Nom} : {ex.Message}{(ex.Details.Count > 0 ? " — " + string.Join(" ; ", ex.Details) : "")}");
            }
        }

        return rapport;
    }

    private async Task SynchroniserProjetAsync(ProjetVercel projet, Guid plateforme, RapportSynchro rapport, CancellationToken ct)
    {
        var elementId = await ElementDuProjetAsync(projet, rapport, ct).ConfigureAwait(false);

        // Attributs tenus par le collecteur ; l'URL n'est proposée que si l'élément n'en a pas.
        var imposes = new Dictionary<string, JsonNode?> { ["projet_vercel"] = projet.Nom };
        if (projet.Framework is not null)
        {
            imposes["framework"] = projet.Framework;
        }

        if (projet.Node is not null)
        {
            imposes["node"] = projet.Node;
        }

        var siAbsents = new Dictionary<string, JsonNode?>();
        if (UrlPrincipale(projet) is { } url)
        {
            siAbsents["url"] = url;
        }

        if (await inventaire.FusionnerAttributsAsync(elementId, imposes, siAbsents, ct).ConfigureAwait(false))
        {
            rapport.Modifies.Add(projet.Nom);
        }

        await inventaire.RelierAsync(elementId, TypesRelationInitiaux.DeployeSur, plateforme, OrigineRelation.Collecte, ct).ConfigureAwait(false);

        if (projet.DepotGitHub is { Length: > 0 } depot)
        {
            var depotId = await DepotAsync(depot, rapport, ct).ConfigureAwait(false);
            await inventaire.RelierAsync(elementId, TypesRelationInitiaux.CodeDans, depotId, OrigineRelation.Collecte, ct).ConfigureAwait(false);
        }

        foreach (var apex in projet.Domaines.Where(d => d.Branche is null && d.Apex is not null && !d.Apex.EndsWith("vercel.app", StringComparison.Ordinal))
                     .Select(d => d.Apex!).Distinct(StringComparer.Ordinal))
        {
            if (await DomaineInventorieAsync(apex, ct).ConfigureAwait(false) is { } domaineId)
            {
                await inventaire.RelierAsync(domaineId, TypesRelationInitiaux.PointeVers, elementId, OrigineRelation.Collecte, ct).ConfigureAwait(false);
            }
        }

        if (projet.Production is { } dep && Etat(dep.Etat) is { } etat)
        {
            var mesures = new JsonObject
            {
                ["etat"] = dep.Etat,
                ["deploiement"] = dep.Id,
                ["date"] = dep.Date.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["url"] = dep.Url,
                ["commit"] = dep.Commit,
                ["message"] = dep.Message?.Split('\n')[0],
                ["inspecteur"] = dep.InspecteurUrl,
            };
            await observations.EnregistrerAsync(elementId, Source, etat, JsonDocument.Parse(mesures.ToJsonString()), ct).ConfigureAwait(false);
        }
    }

    private static EtatSante? Etat(string etatVercel) => etatVercel.ToUpperInvariant() switch
    {
        "READY" => EtatSante.Ok,
        "ERROR" => EtatSante.Ko,
        "BLOCKED" or "CANCELED" => EtatSante.Degrade,
        _ => null, // en cours de construction : on garde l'état précédent
    };

    private static string? UrlPrincipale(ProjetVercel projet)
    {
        var production = projet.Domaines.Where(d => d.Branche is null).Select(d => d.Nom).ToList();
        var nom = production.FirstOrDefault(n => !n.EndsWith(".vercel.app", StringComparison.Ordinal)) ?? production.FirstOrDefault();
        return nom is null ? null : $"https://{nom}";
    }

    /// <summary>Élément du projet : déjà rattaché, sinon élément manuel à rattacher, sinon nouvel élément.</summary>
    private async Task<Guid> ElementDuProjetAsync(ProjetVercel projet, RapportSynchro rapport, CancellationToken ct)
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rattache = await db.Elements.AsNoTracking()
            .Where(e => e.Source == Source && e.IdExterne == projet.Id).Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (rattache is { } id)
        {
            return id;
        }

        var manuels = (await db.Elements.AsNoTracking().Where(e => e.Source == null).ToListAsync(ct).ConfigureAwait(false))
            .Where(e => Texte(e.Attributs, "projet_vercel") == projet.Nom)
            .ToList();
        var domainesProduction = projet.Domaines.Where(d => d.Branche is null).Select(d => d.Nom).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidat = manuels.FirstOrDefault(e => Uri.TryCreate(Texte(e.Attributs, "url"), UriKind.Absolute, out var u) && domainesProduction.Contains(u.Host))
            ?? (manuels.Count == 1 ? manuels[0] : null);
        if (candidat is not null)
        {
            await inventaire.RattacherSourceAsync(candidat.Id, Source, projet.Id, ct).ConfigureAwait(false);
            rapport.Rattaches.Add($"{projet.Nom} → {candidat.Slug}");
            return candidat.Id;
        }

        var cree = await inventaire.CreerElementAsync(new NouvelElement(
            "appli", projet.Nom, Description: "Projet Vercel, ajouté par le collecteur.",
            Source: Source, IdExterne: projet.Id), ct).ConfigureAwait(false);
        rapport.Crees.Add(cree.Slug);
        return cree.Id;
    }

    private async Task<Guid> DepotAsync(string depot, RapportSynchro rapport, CancellationToken ct)
    {
        var url = $"https://github.com/{depot}";
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existant = await db.Elements.AsNoTracking()
            .Where(e => e.Source == SourceGitHub && e.IdExterne == depot).Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (existant is { } id)
        {
            return id;
        }

        var manuel = (await db.Elements.AsNoTracking().Where(e => e.Source == null && e.TypeCode == "depot").ToListAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(e => string.Equals(Texte(e.Attributs, "url")?.TrimEnd('/'), url, StringComparison.OrdinalIgnoreCase));
        if (manuel is not null)
        {
            await inventaire.RattacherSourceAsync(manuel.Id, SourceGitHub, depot, ct).ConfigureAwait(false);
            rapport.Rattaches.Add($"{depot} → {manuel.Slug}");
            return manuel.Id;
        }

        var cree = await inventaire.CreerElementAsync(new NouvelElement(
            "depot", depot, $"depot-{depot.Split('/')[^1]}",
            Attributs: JsonDocument.Parse(new JsonObject { ["url"] = url }.ToJsonString()),
            Source: SourceGitHub, IdExterne: depot), ct).ConfigureAwait(false);
        rapport.Crees.Add(cree.Slug);
        return cree.Id;
    }

    /// <summary>Élément de la plateforme Vercel : rattaché, sinon l'élément manuel « vercel », sinon créé.</summary>
    private async Task<Guid> PlateformeAsync(string? equipe, RapportSynchro rapport, CancellationToken ct)
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existant = await db.Elements.AsNoTracking()
            .Where(e => e.Source == Source && e.IdExterne == IdPlateforme).Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (existant is { } id)
        {
            return id;
        }

        var manuel = await db.Elements.AsNoTracking()
            .Where(e => e.Source == null && e.TypeCode == "paas" && e.Slug == "vercel")
            .Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (manuel is { } m)
        {
            await inventaire.RattacherSourceAsync(m, Source, IdPlateforme, ct).ConfigureAwait(false);
            rapport.Rattaches.Add("plateforme → vercel");
            return m;
        }

        var attributs = new JsonObject { ["fournisseur"] = "Vercel" };
        if (equipe is not null)
        {
            attributs["equipe"] = equipe;
        }

        var cree = await inventaire.CreerElementAsync(new NouvelElement(
            "paas", "Vercel", "vercel", Attributs: JsonDocument.Parse(attributs.ToJsonString()),
            Source: Source, IdExterne: IdPlateforme), ct).ConfigureAwait(false);
        rapport.Crees.Add(cree.Slug);
        return cree.Id;
    }

    private async Task<Guid?> DomaineInventorieAsync(string apex, CancellationToken ct)
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Elements.AsNoTracking()
            .Where(e => e.TypeCode == "domaine" && (e.Slug == apex || e.Nom == apex))
            .Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    private static string? Texte(JsonDocument document, string cle) =>
        document.RootElement.ValueKind == JsonValueKind.Object
        && document.RootElement.TryGetProperty(cle, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
