namespace InfraHub.Core.Domaine;

/// <summary>Entrée du journal libre d'un élément (markdown).</summary>
public sealed class Note : IHorodate
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ElementId { get; set; }
    public Element? Element { get; set; }

    public required string Contenu { get; set; }
    public Auteur Auteur { get; set; } = Auteur.Moi;

    public DateTimeOffset CreeLe { get; set; }
    public DateTimeOffset ModifieLe { get; set; }
}
