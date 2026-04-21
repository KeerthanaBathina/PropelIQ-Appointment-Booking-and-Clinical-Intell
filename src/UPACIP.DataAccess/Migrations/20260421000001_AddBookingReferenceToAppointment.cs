using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingReferenceToAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookingReference",
                table: "appointments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingReference",
                table: "appointments");
        }
    }
}
