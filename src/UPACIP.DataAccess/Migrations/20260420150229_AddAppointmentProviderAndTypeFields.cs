using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentProviderAndTypeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppointmentType",
                table: "appointments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderId",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "appointments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppointmentType",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "ProviderId",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "appointments");
        }
    }
}
