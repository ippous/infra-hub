namespace InfraHub.Core.Domaine;

/// <summary>
/// Emplacement d'un secret dans le gestionnaire de mots de passe (ex. <c>op://Infra/VPS/ssh</c>).
/// La valeur du secret n'est jamais stockée.
/// </summary>
public sealed class ReferenceSecret
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ElementId { get; set; }
    public Element? Element { get; set; }

    public required string Libelle { get; set; }
    public required string Reference { get; set; }
}
