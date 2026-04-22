using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddManualVerificationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DateConflictExplanation",
                table: "extracted_data",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIncompleteDate",
                table: "extracted_data",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_is_incomplete_date",
                table: "extracted_data",
                column: "IsIncompleteDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_extracted_data_is_incomplete_date",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "DateConflictExplanation",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "IsIncompleteDate",
                table: "extracted_data");
        }
    }
}
