using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientRegistrationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "asp_net_users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "AccountStatus",
                table: "asp_net_users",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PendingVerification");

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "asp_net_users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "asp_net_users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "email_verification_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_verification_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_email_verification_tokens_asp_net_users_UserId",
                        column: x => x.UserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_verification_tokens_token_hash",
                table: "email_verification_tokens",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "ix_email_verification_tokens_user_id",
                table: "email_verification_tokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_verification_tokens");

            migrationBuilder.DropColumn(
                name: "AccountStatus",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "asp_net_users");

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "asp_net_users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }
    }
}
