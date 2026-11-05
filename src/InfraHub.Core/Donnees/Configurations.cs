using InfraHub.Core.Domaine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InfraHub.Core.Donnees;

internal sealed class TypeElementConfiguration : IEntityTypeConfiguration<TypeElement>
{
    public void Configure(EntityTypeBuilder<TypeElement> b)
    {
        b.ToTable("type_element");
        b.HasKey(t => t.Code);
        b.Property(t => t.Code).HasMaxLength(40);
        b.Property(t => t.Libelle).HasMaxLength(100);
        b.Property(t => t.SchemaAttributs).HasColumnType("jsonb");
        b.Property(t => t.Icone).HasMaxLength(40);
    }
}

internal sealed class ElementConfiguration : IEntityTypeConfiguration<Element>
{
    public void Configure(EntityTypeBuilder<Element> b)
    {
        b.ToTable("element");
        b.HasKey(e => e.Id);
        b.Property(e => e.TypeCode).HasMaxLength(40);
        b.Property(e => e.Nom).HasMaxLength(200);
        b.Property(e => e.Slug).HasMaxLength(200);
        b.Property(e => e.Attributs).HasColumnType("jsonb");
        b.Property(e => e.Source).HasMaxLength(40);
        b.Property(e => e.IdExterne).HasMaxLength(200);

        b.HasOne(e => e.Type).WithMany(t => t.Elements).HasForeignKey(e => e.TypeCode).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(e => e.Slug).IsUnique();
        b.HasIndex(e => new { e.Source, e.IdExterne }).IsUnique();
        b.HasIndex(e => e.TypeCode);
        b.HasIndex(e => e.Etiquettes).HasMethod("gin");
        b.HasIndex(e => e.Attributs).HasMethod("gin").HasOperators("jsonb_path_ops");
    }
}

internal sealed class TypeRelationConfiguration : IEntityTypeConfiguration<TypeRelation>
{
    public void Configure(EntityTypeBuilder<TypeRelation> b)
    {
        b.ToTable("type_relation");
        b.HasKey(t => t.Code);
        b.Property(t => t.Code).HasMaxLength(40);
        b.Property(t => t.Libelle).HasMaxLength(100);
        b.Property(t => t.LibelleInverse).HasMaxLength(100);
    }
}

internal sealed class RelationConfiguration : IEntityTypeConfiguration<Relation>
{
    public void Configure(EntityTypeBuilder<Relation> b)
    {
        b.ToTable("relation", t => t.HasCheckConstraint("ck_relation_pas_de_boucle", "source_id <> cible_id"));
        b.HasKey(r => r.Id);
        b.Property(r => r.TypeCode).HasMaxLength(40);
        b.Property(r => r.Attributs).HasColumnType("jsonb");

        b.HasOne(r => r.Source).WithMany(e => e.RelationsSortantes).HasForeignKey(r => r.SourceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(r => r.Cible).WithMany(e => e.RelationsEntrantes).HasForeignKey(r => r.CibleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(r => r.Type).WithMany().HasForeignKey(r => r.TypeCode).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(r => new { r.SourceId, r.CibleId, r.TypeCode }).IsUnique();
        b.HasIndex(r => r.CibleId);
    }
}

internal sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> b)
    {
        b.ToTable("note");
        b.HasKey(n => n.Id);
        b.HasOne(n => n.Element).WithMany(e => e.Notes).HasForeignKey(n => n.ElementId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(n => new { n.ElementId, n.CreeLe });
    }
}

internal sealed class LienConfiguration : IEntityTypeConfiguration<Lien>
{
    public void Configure(EntityTypeBuilder<Lien> b)
    {
        b.ToTable("lien");
        b.HasKey(l => l.Id);
        b.Property(l => l.Libelle).HasMaxLength(100);
        b.Property(l => l.Url).HasMaxLength(2000);
        b.HasOne(l => l.Element).WithMany(e => e.Liens).HasForeignKey(l => l.ElementId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ReferenceSecretConfiguration : IEntityTypeConfiguration<ReferenceSecret>
{
    public void Configure(EntityTypeBuilder<ReferenceSecret> b)
    {
        b.ToTable("reference_secret");
        b.HasKey(s => s.Id);
        b.Property(s => s.Libelle).HasMaxLength(100);
        b.Property(s => s.Reference).HasMaxLength(500);
        b.HasOne(s => s.Element).WithMany(e => e.Secrets).HasForeignKey(s => s.ElementId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ObservationConfiguration : IEntityTypeConfiguration<Observation>
{
    public void Configure(EntityTypeBuilder<Observation> b)
    {
        b.ToTable("observation");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).UseIdentityAlwaysColumn();
        b.Property(o => o.Collecteur).HasMaxLength(40);
        b.Property(o => o.Mesures).HasColumnType("jsonb");
        b.HasOne(o => o.Element).WithMany().HasForeignKey(o => o.ElementId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(o => new { o.ElementId, o.Horodatage }).IsDescending(false, true);
        b.HasIndex(o => o.Horodatage);
    }
}

internal sealed class EntreeAuditConfiguration : IEntityTypeConfiguration<EntreeAudit>
{
    public void Configure(EntityTypeBuilder<EntreeAudit> b)
    {
        b.ToTable("audit");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).UseIdentityAlwaysColumn();
        b.Property(a => a.Entite).HasMaxLength(40);
        b.Property(a => a.EntiteId).HasMaxLength(200);
        b.Property(a => a.Avant).HasColumnType("jsonb");
        b.Property(a => a.Apres).HasColumnType("jsonb");
        b.HasIndex(a => new { a.Entite, a.EntiteId });
        b.HasIndex(a => a.Horodatage);
    }
}
