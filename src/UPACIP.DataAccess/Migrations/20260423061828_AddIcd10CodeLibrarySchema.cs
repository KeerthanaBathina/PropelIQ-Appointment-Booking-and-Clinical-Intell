using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIcd10CodeLibrarySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LibraryVersion",
                table: "medical_codes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelevanceRank",
                table: "medical_codes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevalidationStatus",
                table: "medical_codes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "icd10_code_library",
                columns: table => new
                {
                    LibraryEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeValue = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeprecatedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReplacementCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    LibraryVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_icd10_code_library", x => x.LibraryEntryId);
                });

            migrationBuilder.CreateIndex(
                name: "ix_icd10_code_library_category",
                table: "icd10_code_library",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "ix_icd10_code_library_code_value_is_current",
                table: "icd10_code_library",
                columns: new[] { "CodeValue", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "uq_icd10_code_library_code_version",
                table: "icd10_code_library",
                columns: new[] { "CodeValue", "LibraryVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "icd10_code_library");

            migrationBuilder.DropColumn(
                name: "LibraryVersion",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "RelevanceRank",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "RevalidationStatus",
                table: "medical_codes");
        }
    }
}
