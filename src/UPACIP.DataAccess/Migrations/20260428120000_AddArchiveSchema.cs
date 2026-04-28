using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Creates the <c>archive</c> PostgreSQL schema, the <c>archive.appointments</c> table,
    /// and the <c>archived_appointment_references</c> stub table (US_086 AC-3, AC-5, DR-018, DR-020).
    ///
    /// Schema overview:
    /// <list type="bullet">
    ///   <item>
    ///     <c>archive.appointments</c> — mirrors the main <c>appointments</c> table columns plus
    ///     an <c>archived_at_utc</c> timestamp.  Managed via raw SQL because EF Core does not
    ///     natively support multi-schema mapping for the same entity type.  This table is queried
    ///     read-only via <c>IAppointmentArchivalService.GetArchivedAppointmentAsync</c>.
    ///   </item>
    ///   <item>
    ///     <c>archived_appointment_references</c> — EF Core-managed stub table.  One row per
    ///     archived appointment; the <c>Id</c> column holds the original appointment ID so that
    ///     patient-history queries can find archived appointments with a simple PK lookup and
    ///     navigate to the archive for full details.
    ///   </item>
    /// </list>
    ///
    /// Rollback strategy (Down):
    ///   Drops both tables and the archive schema.  Safe because the data has already been
    ///   moved from the main table; rolling back this migration without first restoring the
    ///   archived rows from <c>archive.appointments</c> would result in permanent data loss.
    ///   Administrators must restore from a database backup or reverse the archival operation
    ///   before running Down() in a production environment.
    /// </summary>
    public partial class AddArchiveSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Create the archive schema ──────────────────────────────────────
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS archive;");

            // ── 2. Create archive.appointments (raw SQL — mirrors main appointments) ──
            //
            // Column layout matches the main appointments table in creation order.
            // PascalCase columns mirror the EF Core-generated column names in the main table;
            // snake_case columns (no_show_risk_*) mirror the names from migration
            // 20260421000004_AddAppointmentNoShowRiskMetadata.
            // archived_at_utc is the only additional column.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS archive.appointments (
                    "Id"                      UUID            NOT NULL,
                    "PatientId"               UUID            NOT NULL,
                    "BookingReference"        VARCHAR(20),
                    "AppointmentTime"         TIMESTAMPTZ     NOT NULL,
                    "Status"                  VARCHAR(20)     NOT NULL,
                    "IsWalkIn"                BOOLEAN         NOT NULL,
                    "ProviderId"              UUID,
                    "ProviderName"            VARCHAR(100),
                    "AppointmentType"         VARCHAR(50),
                    "PreferredSlotCriteria"   JSONB,
                    "Version"                 INTEGER         NOT NULL,
                    no_show_risk_score        INTEGER,
                    no_show_risk_band         VARCHAR(10),
                    is_risk_estimated         BOOLEAN,
                    requires_outreach         BOOLEAN,
                    risk_calculated_at_utc    TIMESTAMPTZ,
                    "SlotTemplateId"          UUID,
                    "CreatedAt"               TIMESTAMPTZ     NOT NULL,
                    "UpdatedAt"               TIMESTAMPTZ     NOT NULL,
                    archived_at_utc           TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
                    CONSTRAINT pk_archive_appointments PRIMARY KEY ("Id")
                );
                """);

            // Index: patient_id + appointment_time — supports patient-history archive queries.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_archive_appointments_patient_id_appointment_time
                    ON archive.appointments ("PatientId", "AppointmentTime");
                """);

            // ── 3. Create archived_appointment_references (EF Core entity table) ──
            //
            // Retained in the default schema so standard EF Core patient-history queries
            // can join on it without a cross-schema round-trip.
            migrationBuilder.CreateTable(
                name: "archived_appointment_references",
                columns: table => new
                {
                    Id              = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId       = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentTime = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    Status          = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ArchivedAtUtc   = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ArchiveTable    = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false,
                                          defaultValue: "archive.appointments"),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archived_appointment_references", x => x.Id);
                    table.ForeignKey(
                        name: "FK_archived_appointment_references_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Index: PatientId — primary access pattern for patient-history timeline.
            migrationBuilder.CreateIndex(
                name: "ix_archived_appointment_references_patient_id",
                table: "archived_appointment_references",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop archived_appointment_references (EF Core-managed).
            migrationBuilder.DropTable(name: "archived_appointment_references");

            // Drop archive.appointments and then the schema.
            // WARNING: This permanently removes all archived appointment data.
            // Restore from backup before executing Down() in a production environment.
            migrationBuilder.Sql("DROP TABLE IF EXISTS archive.appointments;");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS archive;");
        }
    }
}
