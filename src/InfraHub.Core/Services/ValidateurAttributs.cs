using System.Text.Json;
using InfraHub.Core.Domaine;
using Json.Schema;

namespace InfraHub.Core.Services;

/// <summary>Vérifie les attributs d'un élément contre le JSON Schema de son type.</summary>
public static class ValidateurAttributs
{
    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    /// <summary>Retourne la liste des erreurs, vide si les attributs sont valides.</summary>
    public static IReadOnlyList<string> Valider(TypeElement type, JsonDocument attributs)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(attributs);

        if (attributs.RootElement.ValueKind != JsonValueKind.Object)
        {
            return ["Les attributs doivent être un objet JSON."];
        }

        if (type.SchemaAttributs is null)
        {
            return [];
        }

        var schema = JsonSchema.Build(type.SchemaAttributs.RootElement, new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        var resultat = schema.Evaluate(attributs.RootElement, Options);
        if (resultat.IsValid)
        {
            return [];
        }

        return (resultat.Details ?? [])
            .Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Values.Select(message => $"{Emplacement(d)}{message}"))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string Emplacement(EvaluationResults detail)
    {
        var chemin = detail.InstanceLocation.ToString();
        return string.IsNullOrEmpty(chemin) || chemin == "/" ? "" : $"{chemin} : ";
    }
}
