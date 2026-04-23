using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddPayerRulesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BundlingCheckResult",
                table: "medical_codes",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "NotChecked");

            migrationBuilder.AddColumn<string>(
                name: "PayerValidationStatus",
                table: "medical_codes",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "NotValidated");

            migrationBuilder.AddColumn<int>(
                name: "SequenceOrder",
                table: "medical_codes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "bundling_edits",
                columns: table => new
                {
                    EditId = table.Column<Guid>(type: "uuid", nullable: false),
                    Column1Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Column2Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    EditType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ModifierAllowed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AllowedModifiers = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, defaultValue: "[]"),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "NCCI"),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bundling_edits", x => x.EditId);
                });

            migrationBuilder.CreateTable(
                name: "code_modifiers",
                columns: table => new
                {
                    ModifierId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifierCode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    ModifierDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ApplicableCodeTypes = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "[\"cpt\"]"),
                    DocumentationRequired = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_code_modifiers", x => x.ModifierId);
                });

            migrationBuilder.CreateTable(
                name: "payer_rules",
                columns: table => new
                {
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayerId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PayerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RuleType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CodeType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PrimaryCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SecondaryCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RuleDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DenialReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CorrectiveAction = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Severity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsCmsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payer_rules", x => x.RuleId);
                });

            migrationBuilder.CreateTable(
                name: "payer_rule_violations",
                columns: table => new
                {
                    ViolationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncounterDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViolatingCodes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: "[]"),
                    Severity = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ResolutionStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionJustification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payer_rule_violations", x => x.ViolationId);
                    table.ForeignKey(
                        name: "FK_payer_rule_violations_asp_net_users_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_payer_rule_violations_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payer_rule_violations_payer_rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "payer_rules",
                        principalColumn: "RuleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_bundling_edits_column1_column2",
                table: "bundling_edits",
                columns: new[] { "Column1Code", "Column2Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_code_modifiers_modifier_code",
                table: "code_modifiers",
                column: "ModifierCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payer_rule_violations_patient_encounter",
                table: "payer_rule_violations",
                columns: new[] { "PatientId", "EncounterDate" });

            migrationBuilder.CreateIndex(
                name: "IX_payer_rule_violations_ResolvedByUserId",
                table: "payer_rule_violations",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_payer_rule_violations_RuleId",
                table: "payer_rule_violations",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "ix_payer_rules_is_cms_default",
                table: "payer_rules",
                column: "IsCmsDefault");

            migrationBuilder.CreateIndex(
                name: "ix_payer_rules_payer_primary_secondary",
                table: "payer_rules",
                columns: new[] { "PayerId", "PrimaryCode", "SecondaryCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bundling_edits");

            migrationBuilder.DropTable(
                name: "code_modifiers");

            migrationBuilder.DropTable(
                name: "payer_rule_violations");

            migrationBuilder.DropTable(
                name: "payer_rules");

            migrationBuilder.DropColumn(
                name: "BundlingCheckResult",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "PayerValidationStatus",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "SequenceOrder",
                table: "medical_codes");
        }
    }
}
