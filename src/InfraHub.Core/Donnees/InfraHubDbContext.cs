using InfraHub.Core.Domaine;
using Microsoft.EntityFrameworkCore;

namespace InfraHub.Core.Donnees;

public sealed partial class InfraHubDbContext(
    DbContextOptions<InfraHubDbContext> options,
    ContexteOperation contexte,
    TimeProvider horloge) : DbContext(options)
{
    public DbSet<TypeElement> TypesElement => Set<TypeElement>();
    public DbSet<Element> Elements => Set<Element>();
    public DbSet<TypeRelation> TypesRelation => Set<TypeRelation>();
    public DbSet<Relation> Relations => Set<Relation>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Lien> Liens => Set<Lien>();
    public DbSet<ReferenceSecret> Secrets => Set<ReferenceSecret>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<EntreeAudit> Audit => Set<EntreeAudit>();

    public ContexteOperation Contexte => contexte;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Énumérations en texte : la base reste lisible et l'ordre des valeurs peut évoluer.
        configurationBuilder.Properties<Categorie>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<StatutElement>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<EtatSante>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<OrigineRelation>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Auteur>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Canal>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ActionAudit>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InfraHubDbContext).Assembly);
        NommageSnakeCase.Appliquer(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PreparerEnregistrement();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PreparerEnregistrement();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PreparerEnregistrement()
    {
        ChangeTracker.DetectChanges();
        var maintenant = horloge.GetUtcNow();
        Horodater(maintenant);
        Journaliser(maintenant);
    }

    private void Horodater(DateTimeOffset maintenant)
    {
        foreach (var entree in ChangeTracker.Entries<IHorodate>())
        {
            if (entree.State == EntityState.Added)
            {
                entree.Entity.CreeLe = maintenant;
                entree.Entity.ModifieLe = maintenant;
            }
            else if (entree.State == EntityState.Modified && JournalAudit.AChangementSignificatif(entree))
            {
                entree.Entity.ModifieLe = maintenant;
            }
        }
    }

    private void Journaliser(DateTimeOffset maintenant)
    {
        var entrees = ChangeTracker.Entries()
            .Select(e => JournalAudit.Construire(e, contexte.Canal, maintenant))
            .OfType<EntreeAudit>()
            .ToList();
        Audit.AddRange(entrees);
    }
}
