using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddCptCodeLibraryAndBundleRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BundleGroupId",
                table: "medical_codes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBundled",
                table: "medical_codes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "cpt_bundle_rules",
                columns: table => new
                {
                    BundleRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BundleCptCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ComponentCptCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BundleDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cpt_bundle_rules", x => x.BundleRuleId);
                });

            migrationBuilder.CreateTable(
                name: "cpt_code_library",
                columns: table => new
                {
                    CptCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CptCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cpt_code_library", x => x.CptCodeId);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cpt_bundle_rules_bundle_cpt_code",
                table: "cpt_bundle_rules",
                column: "BundleCptCode");

            migrationBuilder.CreateIndex(
                name: "ix_cpt_bundle_rules_is_active",
                table: "cpt_bundle_rules",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "uq_cpt_bundle_rules_bundle_component",
                table: "cpt_bundle_rules",
                columns: new[] { "BundleCptCode", "ComponentCptCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cpt_code_library_category_is_active",
                table: "cpt_code_library",
                columns: new[] { "Category", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "ix_cpt_code_library_is_active",
                table: "cpt_code_library",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "uq_cpt_code_library_cpt_code",
                table: "cpt_code_library",
                column: "CptCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cpt_bundle_rules");

            migrationBuilder.DropTable(
                name: "cpt_code_library");

            migrationBuilder.DropColumn(
                name: "BundleGroupId",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "IsBundled",
                table: "medical_codes");
        }
    }
}
