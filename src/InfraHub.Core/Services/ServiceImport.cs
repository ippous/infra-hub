using System.Text.Json;
using System.Text.Json.Serialization;
using InfraHub.Core.Domaine;

namespace InfraHub.Core.Services;

/// <summary>Fichier d'import : éléments puis relations, désignés par leur slug.</summary>
public sealed class DocumentImport
{
    public List<ElementImport> Elements { get; init; } = [];
    public List<RelationImport> Relations { get; init; } = [];

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

public sealed class ElementImport
{
    public required string Type { get; init; }
    public required string Nom { get; init; }
    public required string Slug { get; init; }
    public string? Description { get; init; }
    public StatutElement Statut { get; init; } = StatutElement.Actif;
    public List<string> Etiquettes { get; init; } = [];
    public JsonElement? Attributs { get; init; }
    public List<LienImport> Liens { get; init; } = [];
    public List<string> Notes { get; init; } = [];
    public List<SecretImport> Secrets { get; init; } = [];
}

public sealed record LienImport(string Libelle, string Url);

public sealed record SecretImport(string Libelle, string Reference);

public sealed record RelationImport(string Source, string Type, string Cible);

/// <summary>Résultat d'un import, ligne par ligne.</summary>
public sealed class RapportImport
{
    public List<string> Crees { get; } = [];
    public List<string> DejaPresents { get; } = [];
    public List<string> Relations { get; } = [];
    public List<string> Erreurs { get; } = [];
}

/// <summary>
/// Import d'un inventaire décrit en JSON, en passant par les mêmes règles que l'interface.
/// Idempotent : un élément dont le slug existe déjà est laissé tel quel ; une relation existante n'est pas dupliquée.
/// Une erreur sur une ligne n'empêche pas les suivantes.
/// </summary>
public sealed class ServiceImport(ServiceInventaire inventaire)
{
    public async Task<RapportImport> ImporterAsync(DocumentImport document, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var rapport = new RapportImport();
        var existants = (await inventaire.RechercherAsync(new FiltreElements(), ct).ConfigureAwait(false))
            .ToDictionary(e => e.Slug, e => e.Id, StringComparer.Ordinal);

        foreach (var e in document.Elements)
        {
            var slug = Slug.Depuis(e.Slug);
            if (existants.ContainsKey(slug))
            {
                rapport.DejaPresents.Add(slug);
                continue;
            }

            try
            {
                var attributs = e.Attributs is { } a ? JsonDocument.Parse(a.GetRawText()) : null;
                var cree = await inventaire.CreerElementAsync(
                    new NouvelElement(e.Type, e.Nom, slug, e.Description, attributs, e.Etiquettes, e.Statut), ct).ConfigureAwait(false);
                existants[cree.Slug] = cree.Id;

                foreach (var lien in e.Liens)
                {
                    await inventaire.AjouterLienAsync(cree.Id, lien.Libelle, lien.Url, ct).ConfigureAwait(false);
                }

                foreach (var secret in e.Secrets)
                {
                    await inventaire.AjouterSecretAsync(cree.Id, secret.Libelle, secret.Reference, ct).ConfigureAwait(false);
                }

                foreach (var note in e.Notes)
                {
                    await inventaire.AjouterNoteAsync(cree.Id, note, Auteur.Assistant, ct).ConfigureAwait(false);
                }

                rapport.Crees.Add(cree.Slug);
            }
            catch (ErreurMetier ex)
            {
                rapport.Erreurs.Add($"{slug} : {ex.Message}{(ex.Details.Count > 0 ? " — " + string.Join(" ; ", ex.Details) : "")}");
            }
        }

        foreach (var r in document.Relations)
        {
            var libelle = $"{r.Source} {r.Type} {r.Cible}";
            if (!existants.TryGetValue(Slug.Depuis(r.Source), out var source) || !existants.TryGetValue(Slug.Depuis(r.Cible), out var cible))
            {
                rapport.Erreurs.Add($"{libelle} : élément inconnu.");
                continue;
            }

            try
            {
                await inventaire.RelierAsync(source, r.Type, cible, OrigineRelation.Manuelle, ct).ConfigureAwait(false);
                rapport.Relations.Add(libelle);
            }
            catch (ErreurMetier ex)
            {
                rapport.Erreurs.Add($"{libelle} : {ex.Message}");
            }
        }

        return rapport;
    }
}
