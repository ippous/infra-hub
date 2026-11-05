using System.Globalization;
using System.Text;

namespace InfraHub.Core.Services;

/// <summary>Fabrique un identifiant lisible : « Moulin Frat – Site » → « moulin-frat-site ».</summary>
public static class Slug
{
    public static string Depuis(string texte)
    {
        ArgumentNullException.ThrowIfNull(texte);

        var sansAccents = new StringBuilder(texte.Length);
        foreach (var c in texte.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sansAccents.Append(c);
            }
        }

        var resultat = new StringBuilder(sansAccents.Length);
        foreach (var c in sansAccents.ToString().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '.')
            {
                resultat.Append(c);
            }
            else if (resultat.Length > 0 && resultat[^1] != '-')
            {
                resultat.Append('-');
            }
        }

        return resultat.ToString().Trim('-', '.');
    }
}
