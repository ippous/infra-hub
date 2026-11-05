using System.Text.Json;
using InfraHub.Core.Domaine;
using InfraHub.Core.Donnees;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Core.Services;

/// <summary>Données d'un nouvel élément.</summary>
public sealed record NouvelElement(
    string TypeCode,
    string Nom,
    string? Slug = null,
    string? Description = null,
    JsonDocument? Attributs = null,
    IReadOnlyList<string>? Etiquettes = null,
    StatutElement Statut = StatutElement.Actif,
    string? Source = null,
    string? IdExterne = null);

/// <summary>
/// Opérations d'inventaire partagées par l'interface, l'API, le MCP et l'agent.
/// Toutes les règles (type connu, slug unique, attributs conformes, relations valides) sont ici.
/// </summary>
public sealed class ServiceInventaire(InfraHubDbContext db)
{
    public async Task<Element> CreerElementAsync(NouvelElement nouveau, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nouveau);

        if (string.IsNullOrWhiteSpace(nouveau.Nom))
        {
            throw new ErreurMetier("Le nom est obligatoire.");
        }

        var type = await db.TypesElement.FindAsync([nouveau.TypeCode], ct).ConfigureAwait(false)
            ?? throw new ErreurMetier($"Type d'élément inconnu : « {nouveau.TypeCode} ».");

        var attributs = nouveau.Attributs ?? JsonDocument.Parse("{}");
        VerifierAttributs(type, attributs);

        var slug = string.IsNullOrWhiteSpace(nouveau.Slug)
            ? await SlugLibreAsync(Slug.Depuis(nouveau.Nom), ct).ConfigureAwait(false)
            : Slug.Depuis(nouveau.Slug);
        if (slug.Length == 0)
        {
            throw new ErreurMetier("Impossible de fabriquer un identifiant à partir de ce nom.");
        }

        if (await db.Elements.AnyAsync(e => e.Slug == slug, ct).ConfigureAwait(false))
        {
            throw new ErreurMetier($"L'identifiant « {slug} » est déjà utilisé.");
        }

        var element = new Element
        {
            TypeCode = type.Code,
            Nom = nouveau.Nom.Trim(),
            Slug = slug,
            Description = nouveau.Description,
            Attributs = attributs,
            Etiquettes = [.. nouveau.Etiquettes ?? []],
            Statut = nouveau.Statut,
            Source = nouveau.Source,
            IdExterne = nouveau.IdExterne,
        };
        db.Elements.Add(element);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return element;
    }

    public async Task ModifierAttributsAsync(Guid elementId, JsonDocument attributs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attributs);

        var element = await db.Elements.Include(e => e.Type).FirstOrDefaultAsync(e => e.Id == elementId, ct).ConfigureAwait(false)
            ?? throw new ErreurMetier("Élément introuvable.");
        VerifierAttributs(element.Type!, attributs);
        element.Attributs = attributs;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<Relation> RelierAsync(
        Guid sourceId, string typeRelation, Guid cibleId,
        OrigineRelation origine = OrigineRelation.Manuelle, CancellationToken ct = default)
    {
        if (sourceId == cibleId)
        {
            throw new ErreurMetier("Un élément ne peut pas être relié à lui-même.");
        }

        if (await db.TypesRelation.FindAsync([typeRelation], ct).ConfigureAwait(false) is null)
        {
            throw new ErreurMetier($"Type de relation inconnu : « {typeRelation} ».");
        }

        var nbElements = await db.Elements.CountAsync(e => e.Id == sourceId || e.Id == cibleId, ct).ConfigureAwait(false);
        if (nbElements != 2)
        {
            throw new ErreurMetier("Élément source ou cible introuvable.");
        }

        if (typeRelation == TypesRelationInitiaux.FaitPartieDe
            && await CreeraitUnCycleAsync(sourceId, cibleId, ct).ConfigureAwait(false))
        {
            throw new ErreurMetier("Cette relation « fait partie de » créerait une boucle dans l'arborescence.");
        }

        var existante = await db.Relations
            .FirstOrDefaultAsync(r => r.SourceId == sourceId && r.CibleId == cibleId && r.TypeCode == typeRelation, ct)
            .ConfigureAwait(false);
        if (existante is not null)
        {
            return existante;
        }

        var relation = new Relation { SourceId = sourceId, CibleId = cibleId, TypeCode = typeRelation, Origine = origine };
        db.Relations.Add(relation);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return relation;
    }

    public async Task<Note> AjouterNoteAsync(Guid elementId, string contenu, Auteur auteur = Auteur.Moi, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contenu))
        {
            throw new ErreurMetier("La note est vide.");
        }

        if (!await db.Elements.AnyAsync(e => e.Id == elementId, ct).ConfigureAwait(false))
        {
            throw new ErreurMetier("Élément introuvable.");
        }

        var note = new Note { ElementId = elementId, Contenu = contenu, Auteur = auteur };
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return note;
    }

    private static void VerifierAttributs(TypeElement type, JsonDocument attributs)
    {
        var erreurs = ValidateurAttributs.Valider(type, attributs);
        if (erreurs.Count > 0)
        {
            throw new ErreurMetier($"Attributs non conformes au type « {type.Code} ».", erreurs);
        }
    }

    private async Task<string> SlugLibreAsync(string base_, CancellationToken ct)
    {
        var candidat = base_;
        for (var i = 2; await db.Elements.AnyAsync(e => e.Slug == candidat, ct).ConfigureAwait(false); i++)
        {
            candidat = $"{base_}-{i}";
        }

        return candidat;
    }

    /// <summary>Vrai si la cible fait déjà partie (directement ou non) de la source.</summary>
    private async Task<bool> CreeraitUnCycleAsync(Guid sourceId, Guid cibleId, CancellationToken ct)
    {
        var aVisiter = new Queue<Guid>([cibleId]);
        var vus = new HashSet<Guid>();
        while (aVisiter.TryDequeue(out var courant))
        {
            if (courant == sourceId)
            {
                return true;
            }

            if (!vus.Add(courant))
            {
                continue;
            }

            var parents = await db.Relations
                .Where(r => r.SourceId == courant && r.TypeCode == TypesRelationInitiaux.FaitPartieDe)
                .Select(r => r.CibleId)
                .ToListAsync(ct).ConfigureAwait(false);
            parents.ForEach(aVisiter.Enqueue);
        }

        return false;
    }
}
