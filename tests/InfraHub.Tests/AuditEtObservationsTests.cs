using System.Text.Json;
using InfraHub.Core.Domaine;
using InfraHub.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Tests;

public class AuditEtObservationsTests : BaseDeTest
{
    [Fact]
    public async Task Creation_et_modification_journalisees_avec_le_canal()
    {
        Contexte.Canal = Canal.Mcp;
        Guid id;
        await using (var db = NouveauContexte())
        {
            id = (await new ServiceInventaire(Fabrique).CreerElementAsync(
                new("vps", "VPS", Attributs: JsonDocument.Parse("""{"os":"Ubuntu 24.04"}""")), Ct)).Id;
        }

        Horloge.Avancer(TimeSpan.FromHours(1));
        Contexte.Canal = Canal.Ui;
        await using (var db = NouveauContexte())
        {
            await new ServiceInventaire(Fabrique).ModifierAttributsAsync(id, JsonDocument.Parse("""{"os":"Ubuntu 26.04"}"""), Ct);
        }

        await using var verif = NouveauContexte();
        var journal = await verif.Audit.Where(a => a.Entite == nameof(Element)).OrderBy(a => a.Id).ToListAsync(Ct);
        Assert.Collection(journal,
            creation =>
            {
                Assert.Equal(ActionAudit.Creation, creation.Action);
                Assert.Equal(Canal.Mcp, creation.Canal);
                Assert.Equal(id.ToString(), creation.EntiteId);
                Assert.Equal("VPS", creation.Apres!.RootElement.GetProperty("Nom").GetString());
            },
            modification =>
            {
                Assert.Equal(ActionAudit.Modification, modification.Action);
                Assert.Equal(Canal.Ui, modification.Canal);
                Assert.Equal("Ubuntu 24.04", modification.Avant!.RootElement.GetProperty("Attributs").GetProperty("os").GetString());
                Assert.Equal("Ubuntu 26.04", modification.Apres!.RootElement.GetProperty("Attributs").GetProperty("os").GetString());
                Assert.False(modification.Apres.RootElement.TryGetProperty("Nom", out _));
            });

        var element = await verif.Elements.SingleAsync(e => e.Id == id, Ct);
        Assert.Equal(Horloge.Maintenant, element.ModifieLe);
        Assert.True(element.ModifieLe > element.CreeLe);
    }

    [Fact]
    public async Task Observation_met_a_jour_l_etat_sans_polluer_l_audit()
    {
        Guid id;
        await using (var db = NouveauContexte())
        {
            id = (await new ServiceInventaire(Fabrique).CreerElementAsync(new("site", "Site"), Ct)).Id;
        }

        Horloge.Avancer(TimeSpan.FromMinutes(5));
        Contexte.Canal = Canal.Agent;
        await using (var db = NouveauContexte())
        {
            await new ServiceObservations(db, Horloge, Retention()).EnregistrerAsync(
                id, "http", EtatSante.Degrade, JsonDocument.Parse("""{"latence_ms":2300}"""), Ct);
        }

        await using var verif = NouveauContexte();
        var element = await verif.Elements.SingleAsync(e => e.Id == id, Ct);
        Assert.Equal(EtatSante.Degrade, element.EtatCourant);
        Assert.Equal(Horloge.Maintenant, element.EtatObserveLe);
        Assert.Equal(element.CreeLe, element.ModifieLe);
        Assert.Equal(1, await verif.Audit.CountAsync(a => a.Entite == nameof(Element), Ct));
        Assert.Equal(0, await verif.Audit.CountAsync(a => a.Entite == nameof(Observation), Ct));
    }

    [Fact]
    public async Task Purge_supprime_les_observations_au_dela_de_la_retention()
    {
        Guid id;
        await using (var db = NouveauContexte())
        {
            id = (await new ServiceInventaire(Fabrique).CreerElementAsync(new("site", "Site"), Ct)).Id;
            var service = new ServiceObservations(db, Horloge, Retention());
            await service.EnregistrerAsync(id, "http", EtatSante.Ok, ct: Ct);
            Horloge.Avancer(TimeSpan.FromDays(60));
            await service.EnregistrerAsync(id, "http", EtatSante.Ok, ct: Ct);
            Horloge.Avancer(TimeSpan.FromDays(31));
        }

        await using var purge = NouveauContexte();
        var supprimees = await new ServiceObservations(purge, Horloge, Retention()).PurgerAsync(Ct);

        Assert.Equal(1, supprimees);
        Assert.Equal(1, await purge.Observations.CountAsync(Ct));
    }
}
