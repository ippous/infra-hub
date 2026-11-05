using System.Globalization;
using InfraHub.Core.Domaine;

namespace InfraHub.Web.Interface;

/// <summary>Libellés et formats d'affichage partagés par les écrans.</summary>
public static class Affichage
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly TimeZoneInfo Paris = TrouverFuseau();

    public static string Libelle(Categorie c) => c switch
    {
        Categorie.Environnement => "Environnement",
        Categorie.Developpement => "Développement",
        _ => c.ToString(),
    };

    public static string Libelle(StatutElement s) => s switch
    {
        StatutElement.Actif => "Actif",
        StatutElement.EnProjet => "En projet",
        StatutElement.Archive => "Archivé",
        _ => s.ToString(),
    };

    public static string Libelle(EtatSante e) => e switch
    {
        EtatSante.Inconnu => "Inconnu",
        EtatSante.Ok => "OK",
        EtatSante.Degrade => "Dégradé",
        EtatSante.Ko => "En panne",
        _ => e.ToString(),
    };

    public static string Libelle(Auteur a) => a switch
    {
        Auteur.Moi => "Moi",
        Auteur.Assistant => "Assistant",
        Auteur.Agent => "Agent",
        _ => a.ToString(),
    };

    public static string Classe(EtatSante e) => e switch
    {
        EtatSante.Ok => "etat-ok",
        EtatSante.Degrade => "etat-degrade",
        EtatSante.Ko => "etat-ko",
        _ => "etat-inconnu",
    };

    public static string Classe(StatutElement s) => s switch
    {
        StatutElement.EnProjet => "statut-projet",
        StatutElement.Archive => "statut-archive",
        _ => "statut-actif",
    };

    public static string Date(DateTimeOffset? date) =>
        date is { } d ? TimeZoneInfo.ConvertTime(d, Paris).ToString("d MMM yyyy à HH:mm", Fr) : "—";

    public static string DateCourte(DateTimeOffset date) =>
        TimeZoneInfo.ConvertTime(date, Paris).ToString("d MMM yyyy", Fr);

    /// <summary>« il y a 3 min », « il y a 2 j »…</summary>
    public static string Depuis(DateTimeOffset date, DateTimeOffset maintenant)
    {
        var ecart = maintenant - date;
        return ecart.TotalMinutes switch
        {
            < 1 => "à l'instant",
            < 60 => $"il y a {(int)ecart.TotalMinutes} min",
            < 60 * 24 => $"il y a {(int)ecart.TotalHours} h",
            < 60 * 24 * 30 => $"il y a {(int)ecart.TotalDays} j",
            _ => DateCourte(date),
        };
    }

    public static List<string> Etiquettes(string? saisie) =>
        [.. (saisie ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static TimeZoneInfo TrouverFuseau()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
