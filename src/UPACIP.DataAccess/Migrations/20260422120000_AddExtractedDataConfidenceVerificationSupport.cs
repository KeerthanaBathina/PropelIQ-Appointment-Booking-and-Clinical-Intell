using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractedDataConfidenceVerificationSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ExtractedData: verification workflow columns (US_041 AC-4, EC-1, EC-2) ──────────

            // VerificationStatus — pending / verified / corrected.
            // Default "Pending" for all existing rows so they surface in the review queue.
            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "extracted_data",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            // ReviewReason — None / LowConfidence / ConfidenceUnavailable.
            // Default "None" for existing rows (confidence threshold was not applied retroactively).
            migrationBuilder.AddColumn<string>(
                name: "ReviewReason",
                table: "extracted_data",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            // VerifiedAtUtc — UTC timestamp set when a staff member verifies the row.
            // Nullable: remains null until explicit verification occurs.
            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAtUtc",
                table: "extracted_data",
                type: "timestamp with time zone",
                nullable: true);

            // ── Review work-queue indexes (US_041 EC-2) ──────────────────────────────────────────

            // Flagged-row retrieval: surfaces all rows requiring mandatory review.
            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_flagged_for_review",
                table: "extracted_data",
                column: "FlaggedForReview");

            // Verification-status retrieval: surfaces pending/verified split for SCR-013 counts.
            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_verification_status",
                table: "extracted_data",
                column: "VerificationStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_extracted_data_verification_status",
                table: "extracted_data");

            migrationBuilder.DropIndex(
                name: "ix_extracted_data_flagged_for_review",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "VerifiedAtUtc",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "ReviewReason",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "extracted_data");
        }
    }
}
