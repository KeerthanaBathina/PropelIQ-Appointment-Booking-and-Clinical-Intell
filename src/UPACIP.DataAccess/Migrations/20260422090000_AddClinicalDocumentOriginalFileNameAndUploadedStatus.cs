using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalDocumentOriginalFileNameAndUploadedStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add OriginalFileName column — not null with default empty string so existing rows
            // (if any) do not violate the constraint. The default is a migration guard only;
            // application code always supplies a real value on insert (US_038 AC-4).
            migrationBuilder.AddColumn<string>(
                name: "OriginalFileName",
                table: "clinical_documents",
                type: "character varying(260)",
                maxLength: 260,
                nullable: false,
                defaultValue: string.Empty);

            // ProcessingStatus enum gains the new 'Uploaded' value at the head of the sequence.
            // The column is stored as a string (HasConversion<string>()) so no ALTER TYPE is needed —
            // the new string value is accepted by the existing varchar(20) column.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalFileName",
                table: "clinical_documents");
        }
    }
}
