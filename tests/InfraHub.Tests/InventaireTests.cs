using System.Text.Json;
using InfraHub.Core.Domaine;
using InfraHub.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Tests;

public class InventaireTests : BaseDeTest
{
    [Fact]
    public async Task Types_initiaux_presents_et_initialisation_idempotente()
    {
        await using (var db = NouveauContexte())
        {
            var vps = await db.TypesElement.SingleAsync(t => t.Code == "vps", Ct);
            vps.Libelle = "Serveur virtuel";
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = NouveauContexte())
        {
            await new InitialiseurBase(db).InitialiserAsync(Ct);
        }

        await using var verif = NouveauContexte();
        Assert.Equal(11, await verif.TypesElement.CountAsync(Ct));
        Assert.Equal(7, await verif.TypesRelation.CountAsync(Ct));
        Assert.Equal("Serveur virtuel", (await verif.TypesElement.SingleAsync(t => t.Code == "vps", Ct)).Libelle);
    }

    [Fact]
    public async Task Creer_element_fabrique_un_slug_unique()
    {
        await using var db = NouveauContexte();
        var service = new ServiceInventaire(Fabrique);

        var premier = await service.CreerElementAsync(new("site", "Moulin Frat"), Ct);
        var second = await service.CreerElementAsync(new("site", "Moulin Frat"), Ct);

        Assert.Equal("moulin-frat", premier.Slug);
        Assert.Equal("moulin-frat-2", second.Slug);
        Assert.Equal(Horloge.Maintenant, premier.CreeLe);
    }

    [Fact]
    public async Task Creer_element_refuse_un_type_inconnu_ou_des_attributs_invalides()
    {
        await using var db = NouveauContexte();
        var service = new ServiceInventaire(Fabrique);

        await Assert.ThrowsAsync<ErreurMetier>(() => service.CreerElementAsync(new("frigo", "Frigo"), Ct));

        var erreur = await Assert.ThrowsAsync<ErreurMetier>(() =>
            service.CreerElementAsync(new("vps", "VPS", Attributs: JsonDocument.Parse("""{"ip":"999.1.1.1"}""")), Ct));
        Assert.NotEmpty(erreur.Details);
        Assert.Equal(0, await db.Elements.CountAsync(Ct));
    }

    [Fact]
    public async Task Element_et_attributs_jsonb_relus_depuis_la_base()
    {
        Guid id;
        await using (var db = NouveauContexte())
        {
            var element = await new ServiceInventaire(Fabrique).CreerElementAsync(new(
                "vps", "VPS Hostinger",
                Attributs: JsonDocument.Parse("""{"fournisseur":"Hostinger","ip":"203.0.113.10"}"""),
                Etiquettes: ["prod", "docker"]), Ct);
            id = element.Id;
        }

        await using var verif = NouveauContexte();
        var relu = await verif.Elements.SingleAsync(e => e.Id == id, Ct);
        Assert.Equal("Hostinger", relu.Attributs.RootElement.GetProperty("fournisseur").GetString());
        Assert.Equal(["docker", "prod"], relu.Etiquettes);
        Assert.Equal(StatutElement.Actif, relu.Statut);

        var parEtiquette = await verif.Elements.Where(e => e.Etiquettes.Contains("docker")).CountAsync(Ct);
        Assert.Equal(1, parEtiquette);
    }

    [Fact]
    public async Task Relations_refusent_boucles_et_doublons()
    {
        await using var db = NouveauContexte();
        var service = new ServiceInventaire(Fabrique);
        var vps = await service.CreerElementAsync(new("vps", "VPS"), Ct);
        var conteneur = await service.CreerElementAsync(new("conteneur", "Caddy"), Ct);
        var site = await service.CreerElementAsync(new("site", "Site"), Ct);

        var r1 = await service.RelierAsync(conteneur.Id, TypesRelationInitiaux.FaitPartieDe, vps.Id, ct: Ct);
        var r1Bis = await service.RelierAsync(conteneur.Id, TypesRelationInitiaux.FaitPartieDe, vps.Id, ct: Ct);
        await service.RelierAsync(site.Id, TypesRelationInitiaux.FaitPartieDe, conteneur.Id, ct: Ct);

        Assert.Equal(r1.Id, r1Bis.Id);
        await Assert.ThrowsAsync<ErreurMetier>(() => service.RelierAsync(vps.Id, TypesRelationInitiaux.FaitPartieDe, site.Id, ct: Ct));
        await Assert.ThrowsAsync<ErreurMetier>(() => service.RelierAsync(vps.Id, TypesRelationInitiaux.HebergeSur, vps.Id, ct: Ct));
        await Assert.ThrowsAsync<ErreurMetier>(() => service.RelierAsync(vps.Id, "aime", site.Id, ct: Ct));

        // Une autre relation dans l'autre sens reste possible si le verbe n'est pas « fait partie de ».
        await service.RelierAsync(vps.Id, TypesRelationInitiaux.DependDe, site.Id, ct: Ct);
        Assert.Equal(3, await db.Relations.CountAsync(Ct));
    }

    [Fact]
    public async Task Supprimer_un_element_supprime_ses_dependances()
    {
        await using var db = NouveauContexte();
        var service = new ServiceInventaire(Fabrique);
        var vps = await service.CreerElementAsync(new("vps", "VPS"), Ct);
        var site = await service.CreerElementAsync(new("site", "Site"), Ct);
        await service.RelierAsync(site.Id, TypesRelationInitiaux.HebergeSur, vps.Id, ct: Ct);
        await service.AjouterNoteAsync(vps.Id, "Migré en Ubuntu 26.04", ct: Ct);
        await service.AjouterSecretAsync(vps.Id, "SSH", "op://Infra/VPS/ssh", Ct);
        await service.AjouterLienAsync(vps.Id, "Console", "https://hpanel.hostinger.com", Ct);

        await service.SupprimerElementAsync(vps.Id, Ct);

        await using var verif = NouveauContexte();
        Assert.Equal(0, await verif.Relations.CountAsync(Ct));
        Assert.Equal(0, await verif.Notes.CountAsync(Ct));
        Assert.Equal(0, await verif.Secrets.CountAsync(Ct));
        Assert.Equal(0, await verif.Liens.CountAsync(Ct));
        Assert.Equal(1, await verif.Elements.CountAsync(Ct));
    }
}
