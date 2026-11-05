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
/// Chaque opération ouvre son propre contexte : le service peut vivre longtemps (circuit Blazor).
/// </summary>
public sealed class ServiceInventaire(IDbContextFactory<InfraHubDbContext> fabrique)
{
    // ───────────── Lectures ─────────────

    public async Task<IReadOnlyList<ResumeElement>> RechercherAsync(FiltreElements filtre, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtre);
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);

        var requete = db.Elements.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filtre.Texte))
        {
            var motif = $"%{EchapperLike(filtre.Texte.Trim())}%";
            requete = requete.Where(e =>
                EF.Functions.ILike(e.Nom, motif) || EF.Functions.ILike(e.Slug, motif)
                || (e.Description != null && EF.Functions.ILike(e.Description, motif)));
        }

        if (filtre.Categorie is { } categorie)
        {
            requete = requete.Where(e => e.Type!.Categorie == categorie);
        }

        if (!string.IsNullOrWhiteSpace(filtre.TypeCode))
        {
            requete = requete.Where(e => e.TypeCode == filtre.TypeCode);
        }

        if (!string.IsNullOrWhiteSpace(filtre.Etiquette))
        {
            requete = requete.Where(e => e.Etiquettes.Contains(filtre.Etiquette));
        }

        if (filtre.Statut is { } statut)
        {
            requete = requete.Where(e => e.Statut == statut);
        }

        if (filtre.Etat is { } etat)
        {
            requete = requete.Where(e => e.EtatCourant == etat);
        }

        var resultats = await ProjeterResume(requete).ToListAsync(ct).ConfigureAwait(false);
        return [.. resultats.OrderBy(e => e.Nom, Tri.Noms)];
    }

    public async Task<FicheElement?> FicheAsync(string slug, CancellationToken ct = default)
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);

        var element = await db.Elements.AsNoTracking()
            .Include(e => e.Type)
            .Include(e => e.Notes)
            .Include(e => e.Liens)
            .Include(e => e.Secrets)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Slug == slug, ct).ConfigureAwait(false);
        if (element is null)
        {
            return null;
        }

        var sortantes = await db.Relations.AsNoTracking()
            .Where(r => r.SourceId == element.Id)
            .Select(r => new RelationAffichee(r.Id, SensRelation.Sortante, r.TypeCode, r.Type!.Libelle,
                r.CibleId, r.Cible!.Slug, r.Cible.Nom, r.Cible.Type!.Libelle, r.Origine))
            .ToListAsync(ct).ConfigureAwait(false);
        var entrantes = await db.Relations.AsNoTracking()
            .Where(r => r.CibleId == element.Id)
            .Select(r => new RelationAffichee(r.Id, SensRelation.Entrante, r.TypeCode, r.Type!.LibelleInverse,
                r.SourceId, r.Source!.Slug, r.Source.Nom, r.Source.Type!.Libelle, r.Origine))
            .ToListAsync(ct).ConfigureAwait(false);

        return new FicheElement(
            Resumer(element),
            element.Description,
            element.Attributs,
            element.Type!.SchemaAttributs,
            element.Source,
            element.IdExterne,
            element.EtatObserveLe,
            element.CreeLe,
            [.. sortantes.Concat(entrantes).OrderBy(r => r.Verbe, Tri.Noms).ThenBy(r => r.AutreNom, Tri.Noms)],
            [.. element.Notes.OrderByDescending(n => n.CreeLe).Select(n => new NoteAffichee(n.Id, n.Contenu, n.Auteur, n.CreeLe))],
            [.. element.Liens.OrderBy(l => l.Libelle, Tri.Noms).Select(l => new LienAffiche(l.Id, l.Libelle, l.Url))],
            [.. element.Secrets.OrderBy(s => s.Libelle, Tri.Noms).Select(s => new SecretAffiche(s.Id, s.Libelle, s.Reference))]);
    }

    /// <summary>
    /// Arborescence des relations « fait partie de ». Les racines sont les éléments qui ne font partie de rien.
    /// Un élément qui fait partie de plusieurs parents apparaît sous chacun d'eux.
    /// </summary>
    public async Task<IReadOnlyList<NoeudArbre>> ArborescenceAsync(bool inclureArchives = false, CancellationToken ct = default)
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);

        var requete = db.Elements.AsNoTracking();
        if (!inclureArchives)
        {
            requete = requete.Where(e => e.Statut != StatutElement.Archive);
        }

        var elements = await ProjeterResume(requete).ToDictionaryAsync(e => e.Id, ct).ConfigureAwait(false);
        var liens = await db.Relations.AsNoTracking()
            .Where(r => r.TypeCode == TypesRelationInitiaux.FaitPartieDe)
            .Select(r => new { Enfant = r.SourceId, Parent = r.CibleId })
            .ToListAsync(ct).ConfigureAwait(false);
        liens.RemoveAll(l => !elements.ContainsKey(l.Enfant) || !elements.ContainsKey(l.Parent));

        var enfantsParParent = liens.ToLookup(l => l.Parent, l => l.Enfant);
        var avecParent = liens.Select(l => l.Enfant).ToHashSet();

        NoeudArbre Construire(Guid id, HashSet<Guid> chemin)
        {
            chemin.Add(id);
            var enfants = enfantsParParent[id]
                .Where(e => !chemin.Contains(e)) // garde-fou : les boucles sont refusées à l'écriture
                .Select(e => Construire(e, [.. chemin]))
                .OrderBy(n => n.Element.Nom, Tri.Noms)
                .ToList();
            return new NoeudArbre(elements[id], enfants);
        }

        return [.. elements.Keys
            .Where(id => !avecParent.Contains(id))
            .Select(id => Construire(id, []))
            .OrderBy(n => n.Element.Categorie)
            .ThenBy(n => n.Element.Nom, Tri.Noms)];
    }

    public async Task<IReadOnlyList<string>> EtiquettesAsync(CancellationToken ct = default)
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Elements.SelectMany(e => e.Etiquettes).Distinct().OrderBy(e => e).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TypeRelation>> TypesRelationAsync(CancellationToken ct = default)
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.TypesRelation.AsNoTracking().OrderBy(t => t.Libelle).ToListAsync(ct).ConfigureAwait(false);
    }

    // ───────────── Éléments ─────────────

    public async Task<Element> CreerElementAsync(NouvelElement nouveau, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nouveau);
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);

        var nom = Obligatoire(nouveau.Nom, "Le nom est obligatoire.");
        var type = await db.TypesElement.FindAsync([nouveau.TypeCode], ct).ConfigureAwait(false)
            ?? throw new ErreurMetier($"Type d'élément inconnu : « {nouveau.TypeCode} ».");

        var attributs = nouveau.Attributs ?? JsonDocument.Parse("{}");
        VerifierAttributs(type, attributs);

        var slug = string.IsNullOrWhiteSpace(nouveau.Slug)
            ? await SlugLibreAsync(db, Slug.Depuis(nom), ct).ConfigureAwait(false)
            : Slug.Depuis(nouveau.Slug);
        await VerifierSlugAsync(db, slug, null, ct).ConfigureAwait(false);

        var element = new Element
        {
            TypeCode = type.Code,
            Nom = nom,
            Slug = slug,
            Description = Nettoyer(nouveau.Description),
            Attributs = attributs,
            Etiquettes = NormaliserEtiquettes(nouveau.Etiquettes),
            Statut = nouveau.Statut,
            Source = nouveau.Source,
            IdExterne = nouveau.IdExterne,
        };
        db.Elements.Add(element);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return element;
    }

    public async Task ModifierElementAsync(Guid elementId, ModificationElement modification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modification);
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);

        var element = await db.Elements.Include(e => e.Type).FirstOrDefaultAsync(e => e.Id == elementId, ct).ConfigureAwait(false)
            ?? throw new ErreurMetier("Élément introuvable.");
        if (modification.Attributs is not null)
        {
            VerifierAttributs(element.Type!, modification.Attributs);
            if (!JsonElement.DeepEquals(element.Attributs.RootElement, modification.Attributs.RootElement))
            {
                element.Attributs = modification.Attributs;
            }
        }

        var slug = Slug.Depuis(modification.Slug ?? "");
        await VerifierSlugAsync(db, slug, elementId, ct).ConfigureAwait(false);

        element.Nom = Obligatoire(modification.Nom, "Le nom est obligatoire.");
        element.Slug = slug;
        element.Description = Nettoyer(modification.Description);
        element.Statut = modification.Statut;
        var etiquettes = NormaliserEtiquettes(modification.Etiquettes);
        if (!etiquettes.SequenceEqual(element.Etiquettes))
        {
            element.Etiquettes = etiquettes;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ModifierAttributsAsync(Guid elementId, JsonDocument attributs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attributs);
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);

        var element = await db.Elements.Include(e => e.Type).FirstOrDefaultAsync(e => e.Id == elementId, ct).ConfigureAwait(false)
            ?? throw new ErreurMetier("Élément introuvable.");
        VerifierAttributs(element.Type!, attributs);
        element.Attributs = attributs;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SupprimerElementAsync(Guid elementId, CancellationToken ct = default)
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.Elements.Remove(await TrouverAsync(db, elementId, ct).ConfigureAwait(false));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ───────────── Relations ─────────────

    public async Task<Relation> RelierAsync(
        Guid sourceId, string typeRelation, Guid cibleId,
        OrigineRelation origine = OrigineRelation.Manuelle, CancellationToken ct = default)
    {
        if (sourceId == cibleId)
        {
            throw new ErreurMetier("Un élément ne peut pas être relié à lui-même.");
        }

        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);

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
            && await CreeraitUnCycleAsync(db, sourceId, cibleId, ct).ConfigureAwait(false))
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

    public async Task SupprimerRelationAsync(Guid relationId, CancellationToken ct = default)
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        var relation = await db.Relations.FindAsync([relationId], ct).ConfigureAwait(false)
            ?? throw new ErreurMetier("Relation introuvable.");
        db.Relations.Remove(relation);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ───────────── Notes, liens, secrets ─────────────

    public async Task<Note> AjouterNoteAsync(Guid elementId, string contenu, Auteur auteur = Auteur.Moi, CancellationToken ct = default)
    {
        var texte = Obligatoire(contenu, "La note est vide.");
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        await VerifierExisteAsync(db, elementId, ct).ConfigureAwait(false);

        var note = new Note { ElementId = elementId, Contenu = texte, Auteur = auteur };
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return note;
    }

    public Task SupprimerNoteAsync(Guid noteId, CancellationToken ct = default) =>
        SupprimerAsync<Note>(noteId, "Note introuvable.", ct);

    public async Task<Lien> AjouterLienAsync(Guid elementId, string libelle, string url, CancellationToken ct = default)
    {
        var texte = Obligatoire(libelle, "Le libellé du lien est obligatoire.");
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ErreurMetier("L'adresse doit être une URL complète en http ou https.");
        }

        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        await VerifierExisteAsync(db, elementId, ct).ConfigureAwait(false);

        var lien = new Lien { ElementId = elementId, Libelle = texte, Url = uri };
        db.Liens.Add(lien);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return lien;
    }

    public Task SupprimerLienAsync(Guid lienId, CancellationToken ct = default) =>
        SupprimerAsync<Lien>(lienId, "Lien introuvable.", ct);

    public async Task<ReferenceSecret> AjouterSecretAsync(Guid elementId, string libelle, string reference, CancellationToken ct = default)
    {
        var texte = Obligatoire(libelle, "Le libellé est obligatoire.");
        var emplacement = Obligatoire(reference, "La référence est obligatoire.");
        if (ResembleAUnSecret(emplacement))
        {
            throw new ErreurMetier(
                "Cette valeur ressemble à un secret. Indique seulement où le trouver (ex. op://Coffre/Élément/champ), jamais le secret lui-même.");
        }

        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        await VerifierExisteAsync(db, elementId, ct).ConfigureAwait(false);

        var secret = new ReferenceSecret { ElementId = elementId, Libelle = texte, Reference = emplacement };
        db.Secrets.Add(secret);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return secret;
    }

    public Task SupprimerSecretAsync(Guid secretId, CancellationToken ct = default) =>
        SupprimerAsync<ReferenceSecret>(secretId, "Référence introuvable.", ct);

    // ───────────── Outils ─────────────

    /// <summary>
    /// Heuristique prudente : une référence a une forme d'emplacement (schéma op://, bw://…, ou texte avec espaces / slash).
    /// Une longue chaîne sans espace ni séparateur, ou une clé privée, est refusée.
    /// </summary>
    internal static bool ResembleAUnSecret(string valeur)
    {
        if (valeur.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (valeur.Contains("://", StringComparison.Ordinal) || valeur.Contains(' ', StringComparison.Ordinal)
            || valeur.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        return valeur.Length >= 20;
    }

    private async Task SupprimerAsync<T>(Guid id, string introuvable, CancellationToken ct) where T : class
    {
        await using var db = await fabrique.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entite = await db.Set<T>().FindAsync([id], ct).ConfigureAwait(false) ?? throw new ErreurMetier(introuvable);
        db.Set<T>().Remove(entite);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static IQueryable<ResumeElement> ProjeterResume(IQueryable<Element> requete) =>
        requete.Select(e => new ResumeElement(
            e.Id, e.Slug, e.Nom, e.TypeCode, e.Type!.Libelle, e.Type.Icone, e.Type.Categorie,
            e.Statut, e.EtatCourant, e.Etiquettes, e.ModifieLe));

    private static ResumeElement Resumer(Element e) => new(
        e.Id, e.Slug, e.Nom, e.TypeCode, e.Type!.Libelle, e.Type.Icone, e.Type.Categorie,
        e.Statut, e.EtatCourant, e.Etiquettes, e.ModifieLe);

    private static async Task<Element> TrouverAsync(InfraHubDbContext db, Guid id, CancellationToken ct) =>
        await db.Elements.FindAsync([id], ct).ConfigureAwait(false) ?? throw new ErreurMetier("Élément introuvable.");

    private static async Task VerifierExisteAsync(InfraHubDbContext db, Guid id, CancellationToken ct)
    {
        if (!await db.Elements.AnyAsync(e => e.Id == id, ct).ConfigureAwait(false))
        {
            throw new ErreurMetier("Élément introuvable.");
        }
    }

    private static async Task VerifierSlugAsync(InfraHubDbContext db, string slug, Guid? sauf, CancellationToken ct)
    {
        if (slug.Length == 0)
        {
            throw new ErreurMetier("Impossible de fabriquer un identifiant à partir de ce texte.");
        }

        if (await db.Elements.AnyAsync(e => e.Slug == slug && e.Id != sauf, ct).ConfigureAwait(false))
        {
            throw new ErreurMetier($"L'identifiant « {slug} » est déjà utilisé.");
        }
    }

    private static void VerifierAttributs(TypeElement type, JsonDocument attributs)
    {
        var erreurs = ValidateurAttributs.Valider(type, attributs);
        if (erreurs.Count > 0)
        {
            throw new ErreurMetier($"Attributs non conformes au type « {type.Code} ».", erreurs);
        }
    }

    private static async Task<string> SlugLibreAsync(InfraHubDbContext db, string base_, CancellationToken ct)
    {
        var candidat = base_;
        for (var i = 2; await db.Elements.AnyAsync(e => e.Slug == candidat, ct).ConfigureAwait(false); i++)
        {
            candidat = $"{base_}-{i}";
        }

        return candidat;
    }

    /// <summary>Vrai si la cible fait déjà partie (directement ou non) de la source.</summary>
    private static async Task<bool> CreeraitUnCycleAsync(InfraHubDbContext db, Guid sourceId, Guid cibleId, CancellationToken ct)
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

    private static string Obligatoire(string? valeur, string message) =>
        string.IsNullOrWhiteSpace(valeur) ? throw new ErreurMetier(message) : valeur.Trim();

    private static string? Nettoyer(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();

    private static List<string> NormaliserEtiquettes(IEnumerable<string>? etiquettes) =>
        [.. (etiquettes ?? [])
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static string EchapperLike(string texte) =>
        texte.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
