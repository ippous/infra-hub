using System.Text.Json;

namespace InfraHub.Core.Domaine;

/// <summary>Lien orienté entre deux éléments. La hiérarchie passe par le type <c>fait_partie_de</c>.</summary>
public sealed class Relation : IHorodate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SourceId { get; set; }
    public Element? Source { get; set; }

    public Guid CibleId { get; set; }
    public Element? Cible { get; set; }

    public required string TypeCode { get; set; }
    public TypeRelation? Type { get; set; }

    public JsonDocument Attributs { get; set; } = JsonDocument.Parse("{}");
    public OrigineRelation Origine { get; set; } = OrigineRelation.Manuelle;

    public DateTimeOffset CreeLe { get; set; }
    public DateTimeOffset ModifieLe { get; set; }
}
