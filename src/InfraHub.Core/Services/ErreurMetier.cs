namespace InfraHub.Core.Services;

/// <summary>Règle métier non respectée. Le message est destiné à l'utilisateur (interface, API, MCP).</summary>
public sealed class ErreurMetier : Exception
{
    public ErreurMetier(string message) : base(message)
    {
        Details = [];
    }

    public ErreurMetier(string message, IReadOnlyList<string> details) : base(message)
    {
        Details = details;
    }

    public ErreurMetier() : this("Règle métier non respectée.")
    {
    }

    public ErreurMetier(string message, Exception innerException) : base(message, innerException)
    {
        Details = [];
    }

    public IReadOnlyList<string> Details { get; }
}
