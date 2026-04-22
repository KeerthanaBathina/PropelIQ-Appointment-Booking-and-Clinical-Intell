using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Creates the <c>patient_profile_versions</c> table for patient profile consolidation
    /// version tracking (US_043, AC-2, FR-056).
    ///
    /// Schema:
    ///   - <c>id</c>                      UUID        NOT NULL PK (application-generated)
    ///   - <c>patient_id</c>              UUID        NOT NULL FK → patients.id ON DELETE CASCADE
    ///   - <c>version_number</c>          INTEGER     NOT NULL — per-patient monotonic counter
    ///   - <c>consolidated_by_user_id</c> UUID        NULL     FK → asp_net_users.id ON DELETE RESTRICT
    ///   - <c>consolidation_type</c>      VARCHAR(30) NOT NULL — "Initial" | "Incremental"
    ///   - <c>source_document_ids</c>     JSONB       NOT NULL — JSON array of document UUIDs
    ///   - <c>data_snapshot</c>           JSONB       NULL     — delta of changed fields
    ///   - <c>created_at</c>              TIMESTAMPTZ NOT NULL
    ///   - <c>updated_at</c>              TIMESTAMPTZ NOT NULL
    ///
    /// Indexes:
    ///   - UNIQUE (patient_id, version_number)                  — version deduplication
    ///   - (patient_id, created_at)                             — latest-version lookup
    ///   - (consolidated_by_user_id) WHERE NOT NULL             — attribution audit queries
    ///
    /// Safety / rollout:
    ///   - New table; zero impact on existing rows.
    ///   - Down() drops the table and its indexes cleanly.
    /// </summary>
    public partial class AddPatientProfileVersionTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "patient_profile_versions",
                columns: t => new
                {
                    id = t.Column<Guid>(type: "uuid", nullable: false),

                    patient_id = t.Column<Guid>(type: "uuid", nullable: false),

                    version_number = t.Column<int>(type: "integer", nullable: false),

                    // NULL when triggered by automated pipeline (no staff user involved).
                    consolidated_by_user_id = t.Column<Guid>(type: "uuid", nullable: true),

                    // Persisted as VARCHAR for forward-compatible enum extension.
                    consolidation_type = t.Column<string>(
                        type: "character varying(30)",
                        maxLength: 30,
                        nullable: false),

                    // JSONB array of contributing document UUIDs (AC-2 source attribution).
                    source_document_ids = t.Column<string>(
                        type: "jsonb",
                        nullable: false),

                    // JSONB delta of fields changed by this consolidation event. NULL for initial.
                    data_snapshot = t.Column<string>(
                        type: "jsonb",
                        nullable: true),

                    created_at = t.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false),

                    updated_at = t.Column<DateTime>(
                        type: "timestamp with time zone",
                        nullable: false),
                },
                constraints: t =>
                {
                    t.PrimaryKey("pk_patient_profile_versions", x => x.id);

                    // Cascade: deleting a patient removes all profile version history.
                    t.ForeignKey(
                        name:       "fk_patient_profile_versions_patient_id",
                        column:     x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete:   ReferentialAction.Cascade);

                    // Restrict: do not cascade-delete audit trail when a staff user is removed.
                    t.ForeignKey(
                        name:       "fk_patient_profile_versions_consolidated_by_user_id",
                        column:     x => x.consolidated_by_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "id",
                        onDelete:   ReferentialAction.Restrict);
                });

            // Unique constraint: a patient cannot have two rows with the same version number.
            migrationBuilder.CreateIndex(
                name:    "uq_patient_profile_versions_patient_version",
                table:   "patient_profile_versions",
                columns: new[] { "patient_id", "version_number" },
                unique:  true);

            // Latest-version lookup: ORDER BY patient_id, created_at DESC LIMIT 1.
            migrationBuilder.CreateIndex(
                name:    "ix_patient_profile_versions_patient_created_at",
                table:   "patient_profile_versions",
                columns: new[] { "patient_id", "created_at" });

            // Attribution audit: find all versions consolidated by a specific staff user.
            migrationBuilder.Sql(
                "CREATE INDEX ix_patient_profile_versions_consolidated_by_user_id " +
                "ON patient_profile_versions (consolidated_by_user_id) " +
                "WHERE consolidated_by_user_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "patient_profile_versions");
        }
    }
}
