using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InfraHub.Core.Donnees;

/// <summary>Utilisée uniquement par <c>dotnet ef</c> pour générer les migrations.</summary>
internal sealed class FabriqueConception : IDesignTimeDbContextFactory<InfraHubDbContext>
{
    public InfraHubDbContext CreateDbContext(string[] args)
    {
        var chaine = Environment.GetEnvironmentVariable("ConnectionStrings__InfraHub")
            ?? "Host=localhost;Database=infrahub;Username=infrahub;Password=infrahub";
        var options = new DbContextOptionsBuilder<InfraHubDbContext>().UseNpgsql(chaine).Options;
        return new InfraHubDbContext(options, new ContexteOperation(), TimeProvider.System);
    }
}
