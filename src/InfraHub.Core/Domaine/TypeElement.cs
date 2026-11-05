using System.Text.Json;

namespace InfraHub.Core.Domaine;

/// <summary>
/// Type d'élément (vps, nas, site…). Stocké en base et modifiable depuis l'interface.
/// Le JSON Schema décrit les attributs propres au type, rangés dans <see cref="Element.Attributs"/>.
/// </summary>
public sealed class TypeElement
{
    public required string Code { get; set; }
    public required string Libelle { get; set; }
    public Categorie Categorie { get; set; }

    /// <summary>JSON Schema (draft 2020-12) des attributs ; null = attributs libres.</summary>
    public JsonDocument? SchemaAttributs { get; set; }

    public string? Icone { get; set; }

    public List<Element> Elements { get; } = [];
}
