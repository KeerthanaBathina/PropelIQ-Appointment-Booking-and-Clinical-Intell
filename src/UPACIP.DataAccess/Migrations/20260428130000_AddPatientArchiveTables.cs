using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Extends the <c>archive</c> schema (created by <c>20260428120000_AddArchiveSchema</c>)
    /// with archive tables for all patient-owned entities, and creates the
    /// <c>archived_patient_references</c> stub table (US_087 AC-4, DR-021).
    ///
    /// Tables created (all in the <c>archive</c> schema via raw SQL):
    /// <list type="bullet">
    ///   <item><c>archive.patients</c> — full patient row + <c>archived_at_utc</c>.</item>
    ///   <item><c>archive.intake_data</c> — intake records + <c>archived_at_utc</c>.</item>
    ///   <item><c>archive.clinical_documents</c> — document metadata + <c>archived_at_utc</c>.</item>
    ///   <item><c>archive.extracted_data</c> — AI extractions + <c>archived_at_utc</c>.</item>
    ///   <item><c>archive.medical_codes</c> — patient medical codes + <c>archived_at_utc</c>.</item>
    /// </list>
    ///
    /// <c>archived_patient_references</c> is created via EF Core <c>CreateTable</c> so it
    /// is tracked in the EF Core model and accessible via <c>ApplicationDbContext</c>.
    ///
    /// All archive tables use <c>CREATE TABLE ... (LIKE public.&lt;table&gt;)</c> which copies
    /// column definitions (names, types, nullability, defaults) without FK constraints —
    /// exactly what cold-storage archive tables need.
    ///
    /// Down() drops all tables and the archive schema.  WARNING: irreversible data loss
    /// in production — restore from backup before running Down().
    ///
    /// Note: <c>archive.appointments</c> was already created by <c>AddArchiveSchema</c> (US_086).
    /// </summary>
    public partial class AddPatientArchiveTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The archive schema was created by AddArchiveSchema (US_086) — ensure it exists
            // in case migrations are applied out of order.
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS archive;");

            // ── archive.patients ─────────────────────────────────────────────────
            // LIKE copies: column names, types, nullability, default expressions.
            // EXCLUDING constraints (no FKs, no unique constraints) — appropriate for cold storage.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS archive.patients (
                    LIKE patients,
                    archived_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    CONSTRAINT pk_archive_patients PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_archive_patients_deleted_at
                    ON archive.patients ("DeletedAt");
                """);

            // ── archive.intake_data ──────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS archive.intake_data (
                    LIKE intake_data,
                    archived_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    CONSTRAINT pk_archive_intake_data PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_archive_intake_data_patient_id
                    ON archive.intake_data ("PatientId");
                """);

            // ── archive.clinical_documents ───────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS archive.clinical_documents (
                    LIKE clinical_documents,
                    archived_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    CONSTRAINT pk_archive_clinical_documents PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_archive_clinical_documents_patient_id
                    ON archive.clinical_documents ("PatientId");
                """);

            // ── archive.extracted_data ───────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS archive.extracted_data (
                    LIKE extracted_data,
                    archived_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    CONSTRAINT pk_archive_extracted_data PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_archive_extracted_data_document_id
                    ON archive.extracted_data ("DocumentId");
                """);

            // ── archive.medical_codes ────────────────────────────────────────────
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS archive.medical_codes (
                    LIKE medical_codes,
                    archived_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    CONSTRAINT pk_archive_medical_codes PRIMARY KEY ("Id")
                );
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_archive_medical_codes_patient_id
                    ON archive.medical_codes ("PatientId");
                """);

            // ── archived_patient_references (EF Core-managed, default schema) ───
            //
            // Reference stubs retained in the main schema so that audit-log resolution
            // queries never need a cross-schema join.
            migrationBuilder.CreateTable(
                name: "archived_patient_references",
                columns: table => new
                {
                    Id            = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName      = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email         = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeletedAtUtc  = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ArchiveSchema = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false,
                                        defaultValue: "archive"),
                },
                constraints: table =>
                {
                    // No FK — the original patients row no longer exists after archival.
                    table.PrimaryKey("PK_archived_patient_references", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop EF Core-managed reference table.
            migrationBuilder.DropTable(name: "archived_patient_references");

            // Drop archive tables.  WARNING: irreversible data loss in production.
            migrationBuilder.Sql("DROP TABLE IF EXISTS archive.medical_codes;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS archive.extracted_data;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS archive.clinical_documents;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS archive.intake_data;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS archive.patients;");
            // Note: archive.appointments (from AddArchiveSchema) is intentionally preserved here.
        }
    }
}
