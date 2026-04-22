using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentParsingRetrySupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ClinicalDocument parsing lifecycle columns (US_039 task_004, AC-2 – AC-5, EC-1) ──

            // ParseAttemptCount — nullable for backward compat; populated by the dispatcher on each attempt.
            migrationBuilder.AddColumn<int>(
                name: "ParseAttemptCount",
                table: "clinical_documents",
                type: "integer",
                nullable: true);

            // ParseStartedAt — UTC time the last parsing run started (status → Processing).
            migrationBuilder.AddColumn<DateTime>(
                name: "ParseStartedAt",
                table: "clinical_documents",
                type: "timestamp with time zone",
                nullable: true);

            // ParseCompletedAt — UTC time of terminal transition (Completed or Failed).
            migrationBuilder.AddColumn<DateTime>(
                name: "ParseCompletedAt",
                table: "clinical_documents",
                type: "timestamp with time zone",
                nullable: true);

            // ParseNextAttemptAt — UTC time after which a retry should be dispatched (EC-1 resume).
            migrationBuilder.AddColumn<DateTime>(
                name: "ParseNextAttemptAt",
                table: "clinical_documents",
                type: "timestamp with time zone",
                nullable: true);

            // RequiresManualReview — set on terminal failure so dashboards can surface staff tasks (AC-5).
            migrationBuilder.AddColumn<bool>(
                name: "RequiresManualReview",
                table: "clinical_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // ManualReviewReason — plain-English reason for terminal failure (AC-5); varchar(500).
            migrationBuilder.AddColumn<string>(
                name: "ManualReviewReason",
                table: "clinical_documents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // ── New indexes on clinical_documents ─────────────────────────────────────────────────

            // Retry-resume: find documents with a pending next-attempt time after worker restart (EC-1).
            migrationBuilder.CreateIndex(
                name: "ix_clinical_documents_parse_next_attempt_at",
                table: "clinical_documents",
                column: "ParseNextAttemptAt");

            // Manual-review dashboard: quickly list documents requiring staff follow-up (AC-5).
            migrationBuilder.CreateIndex(
                name: "ix_clinical_documents_requires_manual_review_patient_id",
                table: "clinical_documents",
                columns: new[] { "RequiresManualReview", "PatientId" });

            // ── DocumentParsingAttempts table (US_039 task_004, AC-4, EC-1, EC-2) ─────────────────

            migrationBuilder.CreateTable(
                name: "document_parsing_attempts",
                columns: table => new
                {
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AiProvider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ModelConfidence = table.Column<double>(type: "double precision", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_parsing_attempts", x => x.AttemptId);
                    table.ForeignKey(
                        name: "FK_document_parsing_attempts_clinical_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "clinical_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Primary lookup: all attempts for a given document ordered by attempt number (EC-2).
            migrationBuilder.CreateIndex(
                name: "ix_document_parsing_attempts_document_id_attempt_number",
                table: "document_parsing_attempts",
                columns: new[] { "DocumentId", "AttemptNumber" },
                unique: true);

            // Worker-restart resume: find rows where NextAttemptAt ≤ now (EC-1).
            migrationBuilder.CreateIndex(
                name: "ix_document_parsing_attempts_next_attempt_at",
                table: "document_parsing_attempts",
                column: "NextAttemptAt");

            // Recent-activity queries for staff dashboard.
            migrationBuilder.CreateIndex(
                name: "ix_document_parsing_attempts_started_at",
                table: "document_parsing_attempts",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_parsing_attempts");

            migrationBuilder.DropIndex(
                name: "ix_clinical_documents_requires_manual_review_patient_id",
                table: "clinical_documents");

            migrationBuilder.DropIndex(
                name: "ix_clinical_documents_parse_next_attempt_at",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "ManualReviewReason",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "RequiresManualReview",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "ParseNextAttemptAt",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "ParseCompletedAt",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "ParseStartedAt",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "ParseAttemptCount",
                table: "clinical_documents");
        }
    }
}
