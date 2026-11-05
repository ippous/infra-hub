using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InfraHub.Core.Donnees.Migrations;

/// <inheritdoc />
public partial class _20261105203856_Initiale : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                entite = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                entite_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                avant = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                apres = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                canal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                horodatage = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_audit", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "type_element",
            columns: table => new
            {
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                libelle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                categorie = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                schema_attributs = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                icone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_type_element", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "type_relation",
            columns: table => new
            {
                code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                libelle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                libelle_inverse = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_type_relation", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "element",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                nom = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                description = table.Column<string>(type: "text", nullable: true),
                etiquettes = table.Column<List<string>>(type: "text[]", nullable: false),
                attributs = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                id_externe = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                etat_courant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                etat_observe_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                modifie_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_element", x => x.id);
                table.ForeignKey(
                    name: "fk_element_type_element_type_code",
                    column: x => x.type_code,
                    principalTable: "type_element",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "lien",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                element_id = table.Column<Guid>(type: "uuid", nullable: false),
                libelle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_lien", x => x.id);
                table.ForeignKey(
                    name: "fk_lien_element_element_id",
                    column: x => x.element_id,
                    principalTable: "element",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "note",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                element_id = table.Column<Guid>(type: "uuid", nullable: false),
                contenu = table.Column<string>(type: "text", nullable: false),
                auteur = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                modifie_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_note", x => x.id);
                table.ForeignKey(
                    name: "fk_note_element_element_id",
                    column: x => x.element_id,
                    principalTable: "element",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "observation",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                element_id = table.Column<Guid>(type: "uuid", nullable: false),
                collecteur = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                horodatage = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                statut = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                mesures = table.Column<JsonDocument>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_observation", x => x.id);
                table.ForeignKey(
                    name: "fk_observation_element_element_id",
                    column: x => x.element_id,
                    principalTable: "element",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "reference_secret",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                element_id = table.Column<Guid>(type: "uuid", nullable: false),
                libelle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_reference_secret", x => x.id);
                table.ForeignKey(
                    name: "fk_reference_secret_element_element_id",
                    column: x => x.element_id,
                    principalTable: "element",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "relation",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_id = table.Column<Guid>(type: "uuid", nullable: false),
                cible_id = table.Column<Guid>(type: "uuid", nullable: false),
                type_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                attributs = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                origine = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                cree_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                modifie_le = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_relation", x => x.id);
                table.CheckConstraint("ck_relation_pas_de_boucle", "source_id <> cible_id");
                table.ForeignKey(
                    name: "fk_relation_element_cible_id",
                    column: x => x.cible_id,
                    principalTable: "element",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_relation_element_source_id",
                    column: x => x.source_id,
                    principalTable: "element",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_relation_type_relation_type_code",
                    column: x => x.type_code,
                    principalTable: "type_relation",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_audit_entite_entite_id",
            table: "audit",
            columns: new[] { "entite", "entite_id" });

        migrationBuilder.CreateIndex(
            name: "ix_audit_horodatage",
            table: "audit",
            column: "horodatage");

        migrationBuilder.CreateIndex(
            name: "ix_element_attributs",
            table: "element",
            column: "attributs")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });

        migrationBuilder.CreateIndex(
            name: "ix_element_etiquettes",
            table: "element",
            column: "etiquettes")
            .Annotation("Npgsql:IndexMethod", "gin");

        migrationBuilder.CreateIndex(
            name: "ix_element_slug",
            table: "element",
            column: "slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_element_source_id_externe",
            table: "element",
            columns: new[] { "source", "id_externe" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_element_type_code",
            table: "element",
            column: "type_code");

        migrationBuilder.CreateIndex(
            name: "ix_lien_element_id",
            table: "lien",
            column: "element_id");

        migrationBuilder.CreateIndex(
            name: "ix_note_element_id_cree_le",
            table: "note",
            columns: new[] { "element_id", "cree_le" });

        migrationBuilder.CreateIndex(
            name: "ix_observation_element_id_horodatage",
            table: "observation",
            columns: new[] { "element_id", "horodatage" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "ix_observation_horodatage",
            table: "observation",
            column: "horodatage");

        migrationBuilder.CreateIndex(
            name: "ix_reference_secret_element_id",
            table: "reference_secret",
            column: "element_id");

        migrationBuilder.CreateIndex(
            name: "ix_relation_cible_id",
            table: "relation",
            column: "cible_id");

        migrationBuilder.CreateIndex(
            name: "ix_relation_source_id_cible_id_type_code",
            table: "relation",
            columns: new[] { "source_id", "cible_id", "type_code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_relation_type_code",
            table: "relation",
            column: "type_code");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit");

        migrationBuilder.DropTable(
            name: "lien");

        migrationBuilder.DropTable(
            name: "note");

        migrationBuilder.DropTable(
            name: "observation");

        migrationBuilder.DropTable(
            name: "reference_secret");

        migrationBuilder.DropTable(
            name: "relation");

        migrationBuilder.DropTable(
            name: "element");

        migrationBuilder.DropTable(
            name: "type_relation");

        migrationBuilder.DropTable(
            name: "type_element");
    }
}
