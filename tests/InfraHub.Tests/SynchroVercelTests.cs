using System.Text.Json;
using InfraHub.Core.Domaine;
using InfraHub.Core.Services;
using InfraHub.Core.Services.Collecte;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Tests;

public class SynchroVercelTests : BaseDeTest
{
    private static readonly DateTimeOffset Hier = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<ProjetVercel> Projets(string etatIsps = "ERROR") =>
    [
        new("prj_todo", "todo-objets", "nextjs", "24.x", "ippous/todo-objets",
            [new("todo.jaume-eric.com", "jaume-eric.com", null), new("dev.todo.jaume-eric.com", "jaume-eric.com", "develop"),
             new("todo-objets.vercel.app", "vercel.app", null)],
            new("dpl_1", "READY", Hier, "todo-objets-x.vercel.app", "939d419", "Fusion de develop\n\ndétails", null)),
        new("prj_scea", "scea-web-site", null, "24.x", "ippous/Scea-WebSite",
            [new("scea-web-site-erics.vercel.app", "vercel.app", null)],
            new("dpl_2", "BLOCKED", Hier, null, null, null, null)),
        new("prj_isps", "isps2025api", null, "22.x", null, [], new("dpl_3", etatIsps, Hier, null, null, null, null)),
    ];

    private async Task PeuplerAsync()
    {
        var s = new ServiceInventaire(Fabrique);
        await s.CreerElementAsync(new("paas", "Vercel", "vercel"), Ct);
        await s.CreerElementAsync(new("domaine", "jaume-eric.com", "jaume-eric.com"), Ct);
        await s.CreerElementAsync(new("appli", "Carnet", "carnet",
            Attributs: JsonDocument.Parse("""{"url":"https://todo.jaume-eric.com","projet_vercel":"todo-objets"}""")), Ct);
        await s.CreerElementAsync(new("appli", "Carnet (dev)", "carnet-dev",
            Attributs: JsonDocument.Parse("""{"url":"https://dev.todo.jaume-eric.com","projet_vercel":"todo-objets"}""")), Ct);
        await s.CreerElementAsync(new("depot", "ippous/todo-objets", "depot-todo-objets",
            Attributs: JsonDocument.Parse("""{"url":"https://github.com/ippous/todo-objets"}""")), Ct);
    }

    private SynchroVercel Synchro()
    {
        var db = NouveauContexte();
        return new SynchroVercel(Fabrique, new ServiceInventaire(Fabrique), new ServiceObservations(db, Horloge, Retention()));
    }

    [Fact]
    public async Task Rattache_les_elements_manuels_cree_le_reste_et_releve_les_etats()
    {
        await PeuplerAsync();
        Contexte.Canal = Canal.Agent;

        var rapport = await Synchro().SynchroniserAsync(Projets(), "team_x", Ct);

        Assert.Empty(rapport.Erreurs);
        Assert.Contains("todo-objets → carnet", rapport.Rattaches);
        Assert.Contains("ippous/todo-objets → depot-todo-objets", rapport.Rattaches);
        Assert.Contains("plateforme → vercel", rapport.Rattaches);
        Assert.Equal(["scea-web-site", "depot-scea-website", "isps2025api"], rapport.Crees);

        await using var db = NouveauContexte();
        var parSlug = await db.Elements.AsNoTracking().ToDictionaryAsync(e => e.Slug, Ct);
        Assert.Equal(("vercel", "prj_todo"), (parSlug["carnet"].Source, parSlug["carnet"].IdExterne));
        Assert.Null(parSlug["carnet-dev"].Source); // domaine de branche : laissé à la main
        Assert.Equal("nextjs", parSlug["carnet"].Attributs.RootElement.GetProperty("framework").GetString());
        Assert.Equal("https://todo.jaume-eric.com", parSlug["carnet"].Attributs.RootElement.GetProperty("url").GetString());
        Assert.Equal("https://scea-web-site-erics.vercel.app", parSlug["scea-web-site"].Attributs.RootElement.GetProperty("url").GetString());

        Assert.Equal(EtatSante.Ok, parSlug["carnet"].EtatCourant);
        Assert.Equal(EtatSante.Degrade, parSlug["scea-web-site"].EtatCourant);
        Assert.Equal(EtatSante.Ko, parSlug["isps2025api"].EtatCourant);

        var relations = await db.Relations.AsNoTracking()
            .Select(r => r.Source!.Slug + " " + r.TypeCode + " " + r.Cible!.Slug).ToListAsync(Ct);
        Assert.Contains("carnet deploye_sur vercel", relations);
        Assert.Contains("carnet code_dans depot-todo-objets", relations);
        Assert.Contains("jaume-eric.com pointe_vers carnet", relations);
        Assert.Contains("scea-web-site code_dans depot-scea-website", relations);
        Assert.DoesNotContain(relations, r => r.Contains("carnet-dev", StringComparison.Ordinal));

        var mesure = (await db.Observations.AsNoTracking().FirstAsync(o => o.ElementId == parSlug["carnet"].Id, Ct)).Mesures.RootElement;
        Assert.Equal("Fusion de develop", mesure.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Les_passages_suivants_n_ecrivent_que_les_observations()
    {
        await PeuplerAsync();
        await Synchro().SynchroniserAsync(Projets(), "team_x", Ct);
        int audit, relations;
        await using (var db = NouveauContexte())
        {
            (audit, relations) = (await db.Audit.CountAsync(Ct), await db.Relations.CountAsync(Ct));
        }

        Horloge.Avancer(TimeSpan.FromMinutes(15));
        var second = await Synchro().SynchroniserAsync(Projets(etatIsps: "READY"), "team_x", Ct);

        Assert.Empty(second.Crees);
        Assert.Empty(second.Rattaches);
        Assert.Empty(second.Modifies);
        await using var verif = NouveauContexte();
        Assert.Equal(audit, await verif.Audit.CountAsync(Ct));
        Assert.Equal(relations, await verif.Relations.CountAsync(Ct));
        Assert.Equal(6, await verif.Observations.CountAsync(Ct));
        Assert.Equal(EtatSante.Ok, (await verif.Elements.SingleAsync(e => e.Slug == "isps2025api", Ct)).EtatCourant);
    }

    [Fact]
    public async Task Sans_element_manuel_la_plateforme_est_creee()
    {
        var rapport = await Synchro().SynchroniserAsync([Projets()[1]], "team_x", Ct);

        Assert.Equal(["vercel", "scea-web-site", "depot-scea-website"], rapport.Crees);
        await using var db = NouveauContexte();
        var vercel = await db.Elements.SingleAsync(e => e.Slug == "vercel", Ct);
        Assert.Equal(("vercel", "plateforme"), (vercel.Source, vercel.IdExterne));
    }
}
