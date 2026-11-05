using System.Globalization;

namespace InfraHub.Core.Services;

/// <summary>Tri des noms à la française, sans tenir compte de la casse ni de la collation de la base.</summary>
public static class Tri
{
    public static StringComparer Noms { get; } =
        StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), CompareOptions.IgnoreCase);
}
