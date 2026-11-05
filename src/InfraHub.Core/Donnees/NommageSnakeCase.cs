using System.Text;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Core.Donnees;

/// <summary>Noms de colonnes, clés et index en snake_case, l'usage PostgreSQL.</summary>
internal static class NommageSnakeCase
{
    public static void Appliquer(ModelBuilder modelBuilder)
    {
        foreach (var entite in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var propriete in entite.GetProperties())
            {
                propriete.SetColumnName(Convertir(propriete.Name));
            }

            foreach (var cle in entite.GetKeys())
            {
                cle.SetName(Convertir(cle.GetName()!));
            }

            foreach (var etrangere in entite.GetForeignKeys())
            {
                etrangere.SetConstraintName(Convertir(etrangere.GetConstraintName()!));
            }

            foreach (var index in entite.GetIndexes())
            {
                index.SetDatabaseName(Convertir(index.GetDatabaseName()!));
            }
        }
    }

    public static string Convertir(string nom)
    {
        var resultat = new StringBuilder(nom.Length + 8);
        for (var i = 0; i < nom.Length; i++)
        {
            var c = nom[i];
            if (char.IsUpper(c))
            {
                var precedentMinuscule = i > 0 && (char.IsLower(nom[i - 1]) || char.IsDigit(nom[i - 1]));
                var suivantMinuscule = i > 0 && i + 1 < nom.Length && char.IsUpper(nom[i - 1]) && char.IsLower(nom[i + 1]);
                if (precedentMinuscule || suivantMinuscule)
                {
                    resultat.Append('_');
                }

                resultat.Append(char.ToLowerInvariant(c));
            }
            else
            {
                resultat.Append(c);
            }
        }

        return resultat.ToString();
    }
}
