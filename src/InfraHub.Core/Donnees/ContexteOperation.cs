using InfraHub.Core.Domaine;

namespace InfraHub.Core.Donnees;

/// <summary>
/// Informations sur l'opération en cours, à portée de requête : l'interface, l'API, le MCP et l'agent
/// y indiquent leur canal pour que le journal d'audit sache d'où vient chaque modification.
/// </summary>
public sealed class ContexteOperation
{
    public Canal Canal { get; set; } = Canal.Systeme;
}
