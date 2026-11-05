using InfraHub.Core.Donnees;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Core.Services;

/// <summary>Applique les migrations puis ajoute les types initiaux manquants (sans toucher à ceux modifiés).</summary>
public sealed class InitialiseurBase(InfraHubDbContext db)
{
    public async Task InitialiserAsync(CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct).ConfigureAwait(false);

        var typesExistants = await db.TypesElement.Select(t => t.Code).ToListAsync(ct).ConfigureAwait(false);
        db.TypesElement.AddRange(TypesElementInitiaux.Creer().Where(t => !typesExistants.Contains(t.Code)));

        var relationsExistantes = await db.TypesRelation.Select(t => t.Code).ToListAsync(ct).ConfigureAwait(false);
        db.TypesRelation.AddRange(TypesRelationInitiaux.Creer().Where(t => !relationsExistantes.Contains(t.Code)));

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
