using System.Text.Json;
using InfraHub.Core.Domaine;
using InfraHub.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Tests;

public class LecturesTests : BaseDeTest
{
    private async Task<(Element Vps, Element Caddy, Element Site, Element Depot)> PeuplerAsync()
    {
        var s = new ServiceInventaire(Fabrique);
        var vps = await s.CreerElementAsync(new("vps", "VPS Hostinger", Etiquettes: ["Prod", "docker", "prod"]), Ct);
        var caddy = await s.CreerElementAsync(new("conteneur", "Caddy", Description: "Reverse proxy TLS"), Ct);
        var site = await s.CreerElementAsync(new("site", "Moulin Frat", Etiquettes: ["prod"]), Ct);
        var depot = await s.CreerElementAsync(new("depot", "moulin-frat (GitHub)", Statut: StatutElement.Archive), Ct);
        await s.RelierAsync(caddy.Id, TypesRelationInitiaux.FaitPartieDe, vps.Id, ct: Ct);
        await s.RelierAsync(site.Id, TypesRelationInitiaux.HebergeSur, vps.Id, ct: Ct);
        await s.RelierAsync(site.Id, TypesRelationInitiaux.CodeDans, depot.Id, ct: Ct);
        return (vps, caddy, site, depot);
    }

    [Fact]
    public async Task Recherche_par_texte_categorie_type_etiquette_et_statut()
    {
        var (vps, caddy, site, depot) = await PeuplerAsync();
        var s = new ServiceInventaire(Fabrique);

        Assert.Equal(4, (await s.RechercherAsync(new(), Ct)).Count);
        Assert.Equal([caddy.Id], (await s.RechercherAsync(new(Texte: "proxy"), Ct)).Select(e => e.Id));
        Assert.Equal(2, (await s.RechercherAsync(new(Texte: "MOULIN"), Ct)).Count);
        Assert.Equal([vps.Id, caddy.Id], (await s.RechercherAsync(new(Categorie: Categorie.Environnement), Ct)).Select(e => e.Id).Order());
        Assert.Equal([site.Id], (await s.RechercherAsync(new(TypeCode: "site"), Ct)).Select(e => e.Id));
        Assert.Equal(2, (await s.RechercherAsync(new(Etiquette: "prod"), Ct)).Count);
        Assert.Equal([depot.Id], (await s.RechercherAsync(new(Statut: StatutElement.Archive), Ct)).Select(e => e.Id));
        Assert.Empty(await s.RechercherAsync(new(Texte: "%"), Ct));
        Assert.Equal(["docker", "prod"], await s.EtiquettesAsync(Ct));
    }

    [Fact]
    public async Task Fiche_montre_les_relations_dans_les_deux_sens()
    {
        var (vps, _, site, _) = await PeuplerAsync();
        var s = new ServiceInventaire(Fabrique);
        await s.AjouterNoteAsync(vps.Id, "Première note", ct: Ct);
        Horloge.Avancer(TimeSpan.FromMinutes(1));
        await s.AjouterNoteAsync(vps.Id, "Seconde note", ct: Ct);

        var fiche = await s.FicheAsync("vps-hostinger", Ct);

        Assert.NotNull(fiche);
        Assert.Equal(["docker", "prod"], fiche.Resume.Etiquettes);
        Assert.Contains(fiche.Relations, r => r.Sens == SensRelation.Entrante && r.Verbe == "contient" && r.AutreSlug == "caddy");
        Assert.Contains(fiche.Relations, r => r.Sens == SensRelation.Entrante && r.Verbe == "héberge" && r.AutreId == site.Id);
        Assert.Equal(["Seconde note", "Première note"], fiche.Notes.Select(n => n.Contenu));

        var ficheSite = await s.FicheAsync("moulin-frat", Ct);
        Assert.Contains(ficheSite!.Relations, r => r.Sens == SensRelation.Sortante && r.Verbe == "est hébergé sur" && r.AutreId == vps.Id);
        Assert.Null(await s.FicheAsync("inexistant", Ct));
    }

    [Fact]
    public async Task Arborescence_par_fait_partie_de()
    {
        var (vps, caddy, site, _) = await PeuplerAsync();
        var s = new ServiceInventaire(Fabrique);

        var racines = await s.ArborescenceAsync(ct: Ct);

        // Le dépôt archivé est masqué, Caddy est sous le VPS, le site (simple « hébergé sur ») reste à la racine.
        Assert.Equal([vps.Id, site.Id], racines.Select(r => r.Element.Id));
        Assert.Equal([caddy.Id], racines[0].Enfants.Select(e => e.Element.Id));
        Assert.Equal(3, (await s.ArborescenceAsync(inclureArchives: true, Ct)).Count);
    }

    [Fact]
    public async Task Modifier_element_controle_le_slug_et_normalise_les_etiquettes()
    {
        var (vps, caddy, _, _) = await PeuplerAsync();
        var s = new ServiceInventaire(Fabrique);

        await Assert.ThrowsAsync<ErreurMetier>(() =>
            s.ModifierElementAsync(caddy.Id, new("Caddy", "vps-hostinger", null, StatutElement.Actif, []), Ct));

        await s.ModifierElementAsync(caddy.Id, new(" Caddy 2 ", "Caddy 2", "  ", StatutElement.EnProjet, [" TLS ", "tls", "web"]), Ct);

        var fiche = await s.FicheAsync("caddy-2", Ct);
        Assert.Equal("Caddy 2", fiche!.Resume.Nom);
        Assert.Null(fiche.Description);
        Assert.Equal(StatutElement.EnProjet, fiche.Resume.Statut);
        Assert.Equal(["tls", "web"], fiche.Resume.Etiquettes);
        Assert.NotEqual(vps.Id, fiche.Resume.Id);
    }

    [Fact]
    public async Task Liens_et_secrets_controles()
    {
        var (vps, _, _, _) = await PeuplerAsync();
        var s = new ServiceInventaire(Fabrique);

        await Assert.ThrowsAsync<ErreurMetier>(() => s.AjouterLienAsync(vps.Id, "Console", "hpanel.hostinger.com", Ct));
        await Assert.ThrowsAsync<ErreurMetier>(() => s.AjouterLienAsync(vps.Id, "Script", "javascript:alert(1)", Ct));
        await Assert.ThrowsAsync<ErreurMetier>(() => s.AjouterSecretAsync(vps.Id, "Mot de passe", "Xk29!fjQ0zz81mPq_Lw4Rt", Ct));
        await Assert.ThrowsAsync<ErreurMetier>(() =>
            s.AjouterSecretAsync(vps.Id, "Clé", "-----BEGIN OPENSSH PRIVATE KEY-----", Ct));

        var lien = await s.AjouterLienAsync(vps.Id, "Console", "https://hpanel.hostinger.com", Ct);
        await s.AjouterSecretAsync(vps.Id, "SSH root", "op://Infra/VPS Hostinger/clé privée", Ct);
        await s.AjouterSecretAsync(vps.Id, "Admin", "Bitwarden > Infra > VPS", Ct);
        await s.SupprimerLienAsync(lien.Id, Ct);

        var fiche = await s.FicheAsync("vps-hostinger", Ct);
        Assert.Empty(fiche!.Liens);
        Assert.Equal(2, fiche.Secrets.Count);
    }

    [Fact]
    public async Task Supprimer_une_relation()
    {
        var (vps, caddy, _, _) = await PeuplerAsync();
        var s = new ServiceInventaire(Fabrique);
        var relation = (await s.FicheAsync("caddy", Ct))!.Relations.Single();

        await s.SupprimerRelationAsync(relation.Id, Ct);

        Assert.Empty((await s.FicheAsync("caddy", Ct))!.Relations);
        Assert.Equal(3, (await s.ArborescenceAsync(ct: Ct)).Count);
        Assert.NotEqual(vps.Id, caddy.Id);
    }

    [Fact]
    public async Task Types_modifiables_avec_schema_verifie()
    {
        var s = new ServiceInventaire(Fabrique);
        await s.CreerElementAsync(new("outil", "Bruno", Attributs: JsonDocument.Parse("""{"usage":"tests d'API"}""")), Ct);
        var types = new ServiceTypes(Fabrique);

        await Assert.ThrowsAsync<ErreurMetier>(() => types.EnregistrerAsync("outil", "Outil", Categorie.Developpement, null, "{pas du json", Ct));
        await Assert.ThrowsAsync<ErreurMetier>(() => types.EnregistrerAsync("outil", "Outil", Categorie.Developpement, null, """{"type":12}""", Ct));

        var invalides = await types.EnregistrerAsync("outil", "Outil", Categorie.Developpement, "wrench",
            """{"type":"object","properties":{"usage":{"type":"integer"}}}""", Ct);
        Assert.Equal(1, invalides);

        Assert.Equal(0, await types.EnregistrerAsync("Imprimante 3D", "Imprimante 3D", Categorie.Environnement, null, null, Ct));
        var liste = await types.ListerAsync(Ct);
        Assert.Contains(liste, t => t.Type.Code == "imprimante_3d" && t.NombreElements == 0);
        Assert.Contains(liste, t => t.Type.Code == "outil" && t.NombreElements == 1);

        await using var db = NouveauContexte();
        Assert.Equal(12, await db.TypesElement.CountAsync(Ct));
    }
}
