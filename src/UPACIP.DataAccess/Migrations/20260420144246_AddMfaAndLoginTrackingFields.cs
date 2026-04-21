using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaAndLoginTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastLoginAt",
                table: "asp_net_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastLoginIp",
                table: "asp_net_users",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "asp_net_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MfaRecoveryCodes",
                table: "asp_net_users",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpSecretEncrypted",
                table: "asp_net_users",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "LastLoginIp",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "MfaRecoveryCodes",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "TotpSecretEncrypted",
                table: "asp_net_users");
        }
    }
}
