namespace InfraHub.Core.Domaine;

/// <summary>Verbe d'une relation orientée : « source {Libelle} cible », « cible {LibelleInverse} source ».</summary>
public sealed class TypeRelation
{
    public required string Code { get; set; }
    public required string Libelle { get; set; }
    public required string LibelleInverse { get; set; }
}
