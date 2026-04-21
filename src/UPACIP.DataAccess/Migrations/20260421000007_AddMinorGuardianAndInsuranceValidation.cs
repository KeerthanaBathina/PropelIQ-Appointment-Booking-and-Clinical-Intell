using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Extends the <c>intake_data</c> table with minor guardian consent and insurance validation
    /// outcome columns, creates the <c>insurance_validation_records</c> dummy-record reference
    /// table, and seeds representative insurance records for deterministic pre-checks (US_031,
    /// AC-1, AC-2, AC-3, EC-1, EC-2, FR-032, FR-033, FR-034).
    ///
    /// Schema additions to <c>intake_data</c>:
    ///   - <c>guardian_consent</c>                    JSONB        NULL
    ///       Stores guardian name, DOB, relationship, consent acknowledgment, and recorded timestamp.
    ///       NULL for adult patients and records predating this feature.
    ///   - <c>insurance_validation_status</c>         VARCHAR(20)  NULL
    ///       "valid" | "needs-review" | "skipped"; NULL until pre-check runs.
    ///   - <c>insurance_review_reason</c>             TEXT         NULL
    ///       Explanatory text for "needs-review" and "skipped" outcomes (staff and patient-facing).
    ///   - <c>insurance_requires_staff_followup</c>   BOOLEAN      NOT NULL DEFAULT false
    ///       Explicit flag indexed for efficient staff-dashboard querying (AC-3).
    ///   - <c>insurance_validated_at</c>              TIMESTAMPTZ  NULL
    ///       UTC timestamp of the most recent pre-check execution.
    ///
    /// New table: <c>insurance_validation_records</c>
    ///   Reference data used by InsurancePrecheckService; seeded with 16 dummy records.
    ///
    /// New partial index: <c>ix_intake_data_insurance_staff_followup</c>
    ///   WHERE insurance_requires_staff_followup = true AND completed_at IS NOT NULL
    ///   — supports staff-dashboard queries without scanning the full intake_data table.
    ///
    /// Safety / backward-compatibility:
    ///   - All new intake_data columns are nullable or have safe defaults.
    ///   - Existing rows are unaffected; NULL values are treated as "not applicable" by the service.
    ///   - Down() cleanly reverts all schema additions and drops the seeded reference table.
    /// </summary>
    public partial class AddMinorGuardianAndInsuranceValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. guardian_consent JSONB column on intake_data (US_031 AC-1, EC-1) ─────
            // NULL for adult patients and legacy rows.
            // Contains: guardian_name, guardian_date_of_birth, guardian_relationship,
            //           consent_acknowledged, consent_recorded_at.
            migrationBuilder.AddColumn<string>(
                name:     "guardian_consent",
                table:    "intake_data",
                type:     "jsonb",
                nullable: true);

            // ── 2. Insurance pre-check outcome scalar columns (US_031 AC-2, AC-3, EC-2) ─
            // Scalar (not JSONB) so a simple boolean partial index covers staff queries.

            migrationBuilder.AddColumn<string>(
                name:      "insurance_validation_status",
                table:     "intake_data",
                type:      "character varying(20)",
                maxLength: 20,
                nullable:  true);

            migrationBuilder.AddColumn<string>(
                name:     "insurance_review_reason",
                table:    "intake_data",
                type:     "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name:         "insurance_requires_staff_followup",
                table:        "intake_data",
                type:         "boolean",
                nullable:     false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name:     "insurance_validated_at",
                table:    "intake_data",
                type:     "timestamp with time zone",
                nullable: true);

            // Partial index: completed intake rows that need insurance staff follow-up.
            // Excludes in-progress drafts and completed-valid rows — keeps the index tiny.
            migrationBuilder.Sql(
                "CREATE INDEX ix_intake_data_insurance_staff_followup " +
                "ON intake_data (patient_id, insurance_requires_staff_followup) " +
                "WHERE insurance_requires_staff_followup = true AND completed_at IS NOT NULL;");

            // ── 3. insurance_validation_records reference table ───────────────────────────
            migrationBuilder.CreateTable(
                name: "insurance_validation_records",
                columns: t => new
                {
                    id               = t.Column<int>(type: "integer", nullable: false)
                                        .Annotation("Npgsql:ValueGenerationStrategy",
                                                    NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    provider_name    = t.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provider_keyword = t.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    policy_prefix    = t.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active        = t.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at       = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: t => t.PrimaryKey("pk_insurance_validation_records", x => x.id));

            migrationBuilder.CreateIndex(
                name:    "ix_insurance_validation_records_provider_keyword",
                table:   "insurance_validation_records",
                column:  "provider_keyword");

            // ── 4. Seed dummy insurance records (US_031 FR-033) ──────────────────────────
            // 16 records covering the same provider/policy pairs used by InsurancePrecheckService
            // in-memory sets, making the reference data durable and override-able without code change.
            var seedDate = new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table:   "insurance_validation_records",
                columns: ["provider_name", "provider_keyword", "policy_prefix", "is_active", "created_at"],
                values: new object[,]
                {
                    { "Blue Cross Blue Shield", "blue cross",    "BCB-", true, seedDate },
                    { "Blue Cross Blue Shield", "bcbs",          "BCB-", true, seedDate },
                    { "Aetna",                  "aetna",         "AET-", true, seedDate },
                    { "Cigna",                  "cigna",         "CIG-", true, seedDate },
                    { "Humana",                 "humana",        "HUM-", true, seedDate },
                    { "UnitedHealth",           "united health", "UHC-", true, seedDate },
                    { "UnitedHealth",           "uhc",           "UHC-", true, seedDate },
                    { "Anthem",                 "anthem",        "ANT-", true, seedDate },
                    { "Kaiser Permanente",      "kaiser",        "KAI-", true, seedDate },
                    { "Molina Healthcare",      "molina",        "MOL-", true, seedDate },
                    { "Wellcare",               "wellcare",      "WEL-", true, seedDate },
                    { "Centene",                "centene",       "CEN-", true, seedDate },
                    { "Tricare",                "tricare",       "TRI-", true, seedDate },
                    { "Medicare",               "medicare",      "MCR-", true, seedDate },
                    { "Medicaid",               "medicaid",      "MCD-", true, seedDate },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse order: drop reference table first, then index, then columns.

            migrationBuilder.DropTable(name: "insurance_validation_records");

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_intake_data_insurance_staff_followup;");

            migrationBuilder.DropColumn(name: "guardian_consent",                  table: "intake_data");
            migrationBuilder.DropColumn(name: "insurance_validation_status",       table: "intake_data");
            migrationBuilder.DropColumn(name: "insurance_review_reason",           table: "intake_data");
            migrationBuilder.DropColumn(name: "insurance_requires_staff_followup", table: "intake_data");
            migrationBuilder.DropColumn(name: "insurance_validated_at",            table: "intake_data");
        }
    }
}
