using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_audit_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Prompt = table.Column<string>(type: "text", nullable: false),
                    Response = table.Column<string>(type: "text", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    ConfidenceScore = table.Column<float>(type: "real", nullable: true),
                    RequestType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    AbExperimentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AbVariant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_audit_logs_confidence_score",
                table: "ai_audit_logs",
                column: "ConfidenceScore");

            migrationBuilder.CreateIndex(
                name: "ix_ai_audit_logs_created_at",
                table: "ai_audit_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_ai_audit_logs_model_version",
                table: "ai_audit_logs",
                column: "ModelVersion");

            migrationBuilder.CreateIndex(
                name: "ix_ai_audit_logs_patient_created",
                table: "ai_audit_logs",
                columns: new[] { "PatientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_audit_logs_request_type",
                table: "ai_audit_logs",
                column: "RequestType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_audit_logs");
        }
    }
}
