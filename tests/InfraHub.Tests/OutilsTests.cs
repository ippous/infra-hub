using System.Text.Json;
using InfraHub.Core.Donnees;
using InfraHub.Core.Services;

namespace InfraHub.Tests;

public class OutilsTests
{
    [Theory]
    [InlineData("Moulin Frat – Site", "moulin-frat-site")]
    [InlineData("  VPS Hostinger (KVM 2) ", "vps-hostinger-kvm-2")]
    [InlineData("jaume-eric.com", "jaume-eric.com")]
    [InlineData("Écurie & Pâtures", "ecurie-patures")]
    public void Slug_depuis_un_nom(string nom, string attendu) => Assert.Equal(attendu, Slug.Depuis(nom));

    [Theory]
    [InlineData("TypeCode", "type_code")]
    [InlineData("IdExterne", "id_externe")]
    [InlineData("PK_Element", "pk_element")]
    [InlineData("IX_Element_Source_IdExterne", "ix_element_source_id_externe")]
    [InlineData("Url", "url")]
    public void Nommage_snake_case(string nom, string attendu) => Assert.Equal(attendu, NommageSnakeCase.Convertir(nom));

    [Fact]
    public void Attributs_conformes_au_type()
    {
        var vps = TypesElementInitiaux.Creer().Single(t => t.Code == "vps");
        using var attributs = JsonDocument.Parse("""{"fournisseur":"Hostinger","ip":"203.0.113.10","libre":"accepté"}""");

        Assert.Empty(ValidateurAttributs.Valider(vps, attributs));
    }

    [Fact]
    public void Attributs_non_conformes_signales()
    {
        var vps = TypesElementInitiaux.Creer().Single(t => t.Code == "vps");
        using var attributs = JsonDocument.Parse("""{"ip":"pas-une-ip","os":42}""");

        var erreurs = ValidateurAttributs.Valider(vps, attributs);

        Assert.Contains(erreurs, e => e.StartsWith("/ip", StringComparison.Ordinal));
        Assert.Contains(erreurs, e => e.StartsWith("/os", StringComparison.Ordinal));
    }

    [Fact]
    public void Attributs_doivent_etre_un_objet()
    {
        var outil = TypesElementInitiaux.Creer().Single(t => t.Code == "outil");
        using var attributs = JsonDocument.Parse("[1,2]");

        Assert.NotEmpty(ValidateurAttributs.Valider(outil, attributs));
    }

    [Fact]
    public void Les_schemas_initiaux_sont_valides()
    {
        using var vide = JsonDocument.Parse("{}");
        foreach (var type in TypesElementInitiaux.Creer())
        {
            Assert.Empty(ValidateurAttributs.Valider(type, vide));
        }
    }
}
