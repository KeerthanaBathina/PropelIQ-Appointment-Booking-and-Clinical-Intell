using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RefineAuditLogSchemaIndexesAndNullableUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_logs_asp_net_users_UserId",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_timestamp",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_user_id",
                table: "audit_logs");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "audit_logs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "audit_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_action_timestamp",
                table: "audit_logs",
                columns: new[] { "Action", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp",
                table: "audit_logs",
                column: "Timestamp",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id_timestamp",
                table: "audit_logs",
                columns: new[] { "UserId", "Timestamp" });

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_asp_net_users_UserId",
                table: "audit_logs",
                column: "UserId",
                principalTable: "asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_logs_asp_net_users_UserId",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_action_timestamp",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_timestamp",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_user_id_timestamp",
                table: "audit_logs");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "audit_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "audit_logs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp",
                table: "audit_logs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id",
                table: "audit_logs",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_asp_net_users_UserId",
                table: "audit_logs",
                column: "UserId",
                principalTable: "asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
