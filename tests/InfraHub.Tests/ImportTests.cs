using System.Text.Json;
using InfraHub.Core.Domaine;
using InfraHub.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Tests;

public class ImportTests : BaseDeTest
{
    private const string Inventaire = """
        {
          // commentaires et virgules finales acceptés
          "elements": [
            { "type": "vps", "nom": "VPS", "slug": "vps", "etiquettes": ["prod"],
              "attributs": { "ip": "2.24.14.10" },
              "liens": [ { "libelle": "Console", "url": "https://hpanel.hostinger.com" } ],
              "notes": [ "Importé" ] },
            { "type": "site", "nom": "Site", "slug": "site", "statut": "EnProjet" },
            { "type": "vps", "nom": "Mauvais", "slug": "mauvais", "attributs": { "ip": "pas une ip" } },
          ],
          "relations": [
            { "source": "site", "type": "heberge_sur", "cible": "vps" },
            { "source": "site", "type": "heberge_sur", "cible": "inconnu" },
          ]
        }
        """;

    [Fact]
    public async Task Import_cree_relie_et_reste_idempotent()
    {
        var document = JsonSerializer.Deserialize<DocumentImport>(Inventaire, DocumentImport.Options)!;
        Contexte.Canal = Canal.Import;
        var service = new ServiceImport(new ServiceInventaire(Fabrique));

        var premier = await service.ImporterAsync(document, Ct);
        var second = await service.ImporterAsync(document, Ct);

        Assert.Equal(["vps", "site"], premier.Crees);
        Assert.Single(premier.Relations);
        Assert.Equal(2, premier.Erreurs.Count); // attributs invalides + élément inconnu
        Assert.Empty(second.Crees);
        Assert.Equal(["vps", "site"], second.DejaPresents);

        await using var db = NouveauContexte();
        Assert.Equal(2, await db.Elements.CountAsync(Ct));
        Assert.Equal(1, await db.Relations.CountAsync(Ct));
        Assert.Equal(StatutElement.EnProjet, (await db.Elements.SingleAsync(e => e.Slug == "site", Ct)).Statut);
        Assert.Equal(Auteur.Assistant, (await db.Notes.SingleAsync(Ct)).Auteur);
        Assert.True(await db.Audit.Where(a => a.Entite == nameof(Element)).AllAsync(a => a.Canal == Canal.Import, Ct));
    }
}
