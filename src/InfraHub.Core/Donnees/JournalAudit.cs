using System.Text.Json;
using InfraHub.Core.Domaine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace InfraHub.Core.Donnees;

/// <summary>Transforme les changements suivis par EF Core en entrées d'audit.</summary>
internal static class JournalAudit
{
    private static readonly HashSet<Type> TypesAudites =
    [
        typeof(TypeElement), typeof(Element), typeof(TypeRelation), typeof(Relation),
        typeof(Note), typeof(Lien), typeof(ReferenceSecret),
    ];

    /// <summary>Propriétés tenues par le système : leur seul changement ne mérite pas d'entrée d'audit.</summary>
    private static readonly HashSet<string> ProprietesTechniques =
    [
        nameof(IHorodate.CreeLe), nameof(IHorodate.ModifieLe),
        nameof(Element.EtatCourant), nameof(Element.EtatObserveLe),
    ];

    public static bool AChangementSignificatif(EntityEntry entree) =>
        entree.Properties.Any(p => p.IsModified && !ProprietesTechniques.Contains(p.Metadata.Name));

    public static EntreeAudit? Construire(EntityEntry entree, Canal canal, DateTimeOffset horodatage)
    {
        if (!TypesAudites.Contains(entree.Metadata.ClrType))
        {
            return null;
        }

        (ActionAudit action, JsonDocument? avant, JsonDocument? apres) contenu;
        switch (entree.State)
        {
            case EntityState.Added:
                contenu = (ActionAudit.Creation, null, Instantane(entree, p => true, courant: true));
                break;
            case EntityState.Deleted:
                contenu = (ActionAudit.Suppression, Instantane(entree, p => true, courant: false), null);
                break;
            case EntityState.Modified when AChangementSignificatif(entree):
                bool Modifiee(PropertyEntry p) => p.IsModified && !ProprietesTechniques.Contains(p.Metadata.Name);
                contenu = (ActionAudit.Modification,
                    Instantane(entree, Modifiee, courant: false),
                    Instantane(entree, Modifiee, courant: true));
                break;
            default:
                return null;
        }

        var cle = entree.Metadata.FindPrimaryKey()!.Properties
            .Select(p => Convert.ToString(entree.Property(p.Name).CurrentValue, System.Globalization.CultureInfo.InvariantCulture));

        return new EntreeAudit
        {
            Entite = entree.Metadata.ClrType.Name,
            EntiteId = string.Join('|', cle),
            Action = contenu.action,
            Avant = contenu.avant,
            Apres = contenu.apres,
            Canal = canal,
            Horodatage = horodatage,
        };
    }

    private static JsonDocument Instantane(EntityEntry entree, Func<PropertyEntry, bool> filtre, bool courant)
    {
        var valeurs = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var propriete in entree.Properties.Where(filtre))
        {
            var valeur = courant ? propriete.CurrentValue : propriete.OriginalValue;
            valeurs[propriete.Metadata.Name] = valeur switch
            {
                JsonDocument document => document.RootElement,
                Enum e => e.ToString(),
                _ => valeur,
            };
        }

        return JsonSerializer.SerializeToDocument(valeurs);
    }
}
