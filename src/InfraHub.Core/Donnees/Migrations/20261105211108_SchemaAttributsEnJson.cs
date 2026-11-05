using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraHub.Core.Donnees.Migrations;

/// <inheritdoc />
public partial class _20261105211108_SchemaAttributsEnJson : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<JsonDocument>(
            name: "schema_attributs",
            table: "type_element",
            type: "json",
            nullable: true,
            oldClrType: typeof(JsonDocument),
            oldType: "jsonb",
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<JsonDocument>(
            name: "schema_attributs",
            table: "type_element",
            type: "jsonb",
            nullable: true,
            oldClrType: typeof(JsonDocument),
            oldType: "json",
            oldNullable: true);
    }
}
