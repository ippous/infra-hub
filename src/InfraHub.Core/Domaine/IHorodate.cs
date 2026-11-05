namespace InfraHub.Core.Domaine;

/// <summary>Entité dont les dates de création et de modification sont tenues par le contexte.</summary>
public interface IHorodate
{
    DateTimeOffset CreeLe { get; set; }
    DateTimeOffset ModifieLe { get; set; }
}
