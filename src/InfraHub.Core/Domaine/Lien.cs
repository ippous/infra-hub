namespace InfraHub.Core.Domaine;

/// <summary>URL utile d'un élément : console, dépôt, documentation…</summary>
public sealed class Lien
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ElementId { get; set; }
    public Element? Element { get; set; }

    public required string Libelle { get; set; }
    public required Uri Url { get; set; }
}
