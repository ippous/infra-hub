using System.Text.Json;
using InfraHub.Core.Domaine;
using InfraHub.Core.Donnees;
using Json.Schema;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Core.Services;

/// <summary>Type d'élément avec le nombre d'éléments qui l'utilisent.</summary>
public sealed record TypeAffiche(TypeElement Type, int NombreElements);

/// <summary>Gestion du catalogue des types d'élément.</summary>
public sealed class ServiceTypes(IDbContextFactory<InfraHubDbContext> fabrique)
{
    public async Task<IReadOnlyList<TypeAffiche>> ListerAsync(CancellationToken ct = default)
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.TypesElement.AsNoTracking()
            .OrderBy(t => t.Categorie).ThenBy(t => t.Libelle)
            .Select(t => new TypeAffiche(t, t.Elements.Count))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Crée ou met à jour un type. Retourne le nombre d'éléments existants que le nouveau schéma rend invalides.</summary>
    public async Task<int> EnregistrerAsync(
        string code, string libelle, Categorie categorie, string? icone, string? schemaJson, CancellationToken ct = default)
    {
        var codeNormalise = Slug.Depuis(code ?? "").Replace('-', '_');
        if (codeNormalise.Length == 0)
        {
            throw new ErreurMetier("Le code est obligatoire (lettres, chiffres, tiret bas).");
        }

        if (string.IsNullOrWhiteSpace(libelle))
        {
            throw new ErreurMetier("Le libellé est obligatoire.");
        }

        var schema = LireSchema(schemaJson);

        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        var type = await db.TypesElement.FindAsync([codeNormalise], ct).ConfigureAwait(false);
        if (type is null)
        {
            type = new TypeElement { Code = codeNormalise, Libelle = libelle.Trim() };
            db.TypesElement.Add(type);
        }

        type.Libelle = libelle.Trim();
        type.Categorie = categorie;
        type.Icone = string.IsNullOrWhiteSpace(icone) ? null : icone.Trim();
        if (!MemeJson(type.SchemaAttributs, schema))
        {
            type.SchemaAttributs = schema;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var attributs = await db.Elements.AsNoTracking().Where(e => e.TypeCode == codeNormalise)
            .Select(e => e.Attributs).ToListAsync(ct).ConfigureAwait(false);
        return attributs.Count(a => ValidateurAttributs.Valider(type, a).Count > 0);
    }

    private static JsonDocument? LireSchema(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(schemaJson);
        }
        catch (JsonException ex)
        {
            throw new ErreurMetier($"Le schéma n'est pas du JSON valide : {ex.Message}");
        }

        try
        {
            JsonSchema.Build(document.RootElement, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        }
#pragma warning disable CA1031 // la bibliothèque lève des exceptions variées pour un schéma invalide
        catch (Exception ex)
#pragma warning restore CA1031
        {
            document.Dispose();
            throw new ErreurMetier($"Le schéma n'est pas un JSON Schema valide : {ex.Message}");
        }

        return document;
    }

    private static bool MemeJson(JsonDocument? a, JsonDocument? b) =>
        a is null ? b is null : b is not null && JsonElement.DeepEquals(a.RootElement, b.RootElement);
}
