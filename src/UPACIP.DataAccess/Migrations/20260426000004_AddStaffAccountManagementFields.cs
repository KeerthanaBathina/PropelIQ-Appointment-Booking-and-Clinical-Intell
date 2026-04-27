using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffAccountManagementFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── New columns on asp_net_users (US_061 AC-3) ───────────────────────

            // DeactivatedAt — UTC timestamp set when account is deactivated; cleared on
            // reactivation.  Preserved across subsequent cycles for HIPAA audit trail (FR-088).
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeactivatedAt",
                table: "asp_net_users",
                type: "timestamp with time zone",
                nullable: true);

            // DeactivatedBy — FK to asp_net_users.Id identifying which admin performed the
            // deactivation.  Self-referencing nullable FK; ON DELETE SET NULL preserves the
            // metadata if the acting admin account is later removed (DR-016).
            migrationBuilder.AddColumn<Guid>(
                name: "DeactivatedBy",
                table: "asp_net_users",
                type: "uuid",
                nullable: true);

            // ── Self-referencing FK (US_061 AC-3, DR-016) ───────────────────────
            migrationBuilder.AddForeignKey(
                name: "fk_asp_net_users_deactivated_by",
                table: "asp_net_users",
                column: "DeactivatedBy",
                principalTable: "asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Performance indexes for staff list endpoint (US_061 AC-2, NFR-033) ──

            // Enables fast status-based filtering in GetAllUsersAsync and DeactivateStaffAsync
            // EC-2 query (WHERE account_status = 'Active').
            migrationBuilder.CreateIndex(
                name: "IX_asp_net_users_AccountStatus",
                table: "asp_net_users",
                column: "AccountStatus");

            // Enables efficient ORDER BY LastLoginAt DESC on the staff account list (AC-2).
            migrationBuilder.CreateIndex(
                name: "IX_asp_net_users_LastLoginAt",
                table: "asp_net_users",
                column: "LastLoginAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_asp_net_users_deactivated_by",
                table: "asp_net_users");

            migrationBuilder.DropIndex(
                name: "IX_asp_net_users_AccountStatus",
                table: "asp_net_users");

            migrationBuilder.DropIndex(
                name: "IX_asp_net_users_LastLoginAt",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "DeactivatedBy",
                table: "asp_net_users");
        }
    }
}
