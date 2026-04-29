using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIdColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "waitlist_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "patients",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "notification_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "intake_data",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "clinical_documents",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "appointments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.CreateTable(
                name: "archived_appointment_references",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchiveTable = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archived_appointment_references", x => x.Id);
                    table.ForeignKey(
                        name: "FK_archived_appointment_references_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "archived_patient_references",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchiveSchema = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archived_patient_references", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "backup_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WasRetry = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "compliance_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    HipaaReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compliance_policies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "compliance_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EvaluationCriteriaJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HipaaReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RemediationGuidance = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compliance_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComplianceVerificationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExecutedBy = table.Column<string>(type: "text", nullable: false),
                    TotalChecks = table.Column<int>(type: "integer", nullable: false),
                    PassedChecks = table.Column<int>(type: "integer", nullable: false),
                    FailedChecks = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceVerificationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "data_access_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExportFilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExportFileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProcessedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_access_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_data_access_requests_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DisasterRecoveryRunbooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    ScenarioType = table.Column<string>(type: "text", nullable: false),
                    StepsJson = table.Column<string>(type: "text", nullable: false),
                    TotalEstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisasterRecoveryRunbooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "import_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    SuccessCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    DuplicateCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ErrorReportJson = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    FullErrorReportPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PerformedBy = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "migration_verification_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MigrationName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    StructureCheckPassed = table.Column<bool>(type: "boolean", nullable: false),
                    RowCountCheckPassed = table.Column<bool>(type: "boolean", nullable: false),
                    ForeignKeyCheckPassed = table.Column<bool>(type: "boolean", nullable: false),
                    HistoryChecksumPassed = table.Column<bool>(type: "boolean", nullable: false),
                    OverallPassed = table.Column<bool>(type: "boolean", nullable: false),
                    WarningDetails = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ErrorDetails = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_migration_verification_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recovery_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualRecoveryPointUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BaseBackupUsed = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    WalSegmentsReplayed = table.Column<int>(type: "integer", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    IntegrityPassed = table.Column<bool>(type: "boolean", nullable: false),
                    RecoveryDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    FallbackReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PerformedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsDryRun = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryTestRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExecutedBy = table.Column<string>(type: "text", nullable: false),
                    TestType = table.Column<string>(type: "text", nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    ActualRecoveryTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    RowsVerified = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    Quarter = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryTestRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "restoration_test_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BackupFileName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OverallSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    RowCountsPassed = table.Column<bool>(type: "boolean", nullable: false),
                    ReferentialIntegrityPassed = table.Column<bool>(type: "boolean", nullable: false),
                    ChecksumsPassed = table.Column<bool>(type: "boolean", nullable: false),
                    RestorationDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ErrorDetails = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    PerformedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restoration_test_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComplianceGaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VerificationLogId = table.Column<Guid>(type: "uuid", nullable: false),
                    ControlName = table.Column<string>(type: "text", nullable: false),
                    HipaaReference = table.Column<string>(type: "text", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: false),
                    RemediationPlan = table.Column<string>(type: "text", nullable: false),
                    IdentifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemediationDeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RemediatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceGaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplianceGaps_ComplianceVerificationLogs_VerificationLogId",
                        column: x => x.VerificationLogId,
                        principalTable: "ComplianceVerificationLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "DisasterRecoveryRunbooks",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedBy", "ScenarioType", "Status", "StepsJson", "Title", "TotalEstimatedMinutes", "UpdatedAtUtc", "Version" },
                values: new object[] { new Guid("d1000000-0000-0000-0000-000000000001"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "system", "FullDatabaseLoss", "Active", "[\r\n  {\"stepNumber\":1,\"action\":\"Identify failure scope and escalate to DBA and DevOps\",\"estimatedMinutes\":15,\"responsible\":\"Admin\"},\r\n  {\"stepNumber\":2,\"action\":\"Locate most recent completed backup in backup storage\",\"estimatedMinutes\":10,\"responsible\":\"DBA\"},\r\n  {\"stepNumber\":3,\"action\":\"Decrypt and restore base backup via pg_restore to recovery host\",\"estimatedMinutes\":60,\"responsible\":\"DBA\"},\r\n  {\"stepNumber\":4,\"action\":\"Replay WAL archive segments to latest consistent point\",\"estimatedMinutes\":30,\"responsible\":\"DBA\"},\r\n  {\"stepNumber\":5,\"action\":\"Verify data integrity: row counts, FK constraints, checksums\",\"estimatedMinutes\":30,\"responsible\":\"DBA\"},\r\n  {\"stepNumber\":6,\"action\":\"Restart Windows Services (UPACIP.Api, background jobs)\",\"estimatedMinutes\":30,\"responsible\":\"DevOps\"},\r\n  {\"stepNumber\":7,\"action\":\"Validate end-to-end functionality via health checks and sample queries\",\"estimatedMinutes\":30,\"responsible\":\"QA\"},\r\n  {\"stepNumber\":8,\"action\":\"Notify stakeholders and document incident in audit log\",\"estimatedMinutes\":15,\"responsible\":\"Admin\"}\r\n]", "Full Database Recovery Procedure", 220, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntry_TenantId_Id",
                table: "waitlist_entries",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Patient_TenantId_Id",
                table: "patients",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLog_TenantId_NotificationId",
                table: "notification_logs",
                columns: new[] { "TenantId", "NotificationId" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeData_TenantId_Id",
                table: "intake_data",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocument_TenantId_Id",
                table: "clinical_documents",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointment_TenantId_Id",
                table: "appointments",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "ix_archived_appointment_references_patient_id",
                table: "archived_appointment_references",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "ix_backup_logs_created_at_utc",
                table: "backup_logs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_policies_status",
                table: "compliance_policies",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "ix_compliance_policies_type_version",
                table: "compliance_policies",
                columns: new[] { "PolicyType", "Version" });

            migrationBuilder.CreateIndex(
                name: "ix_compliance_rules_category_active",
                table: "compliance_rules",
                columns: new[] { "Category", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "uix_compliance_rules_rule_name",
                table: "compliance_rules",
                column: "RuleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceGaps_VerificationLogId",
                table: "ComplianceGaps",
                column: "VerificationLogId");

            migrationBuilder.CreateIndex(
                name: "ix_data_access_requests_patient_id",
                table: "data_access_requests",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "ix_data_access_requests_status",
                table: "data_access_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "ix_data_access_requests_status_deadline",
                table: "data_access_requests",
                columns: new[] { "Status", "DeadlineUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_import_logs_created_at_utc",
                table: "import_logs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_import_logs_entity_type_status",
                table: "import_logs",
                columns: new[] { "EntityType", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_migration_verification_logs_verified_at_utc",
                table: "migration_verification_logs",
                column: "VerifiedAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_recovery_logs_created_at_utc",
                table: "recovery_logs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "ix_restoration_test_logs_created_at_utc",
                table: "restoration_test_logs",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archived_appointment_references");

            migrationBuilder.DropTable(
                name: "archived_patient_references");

            migrationBuilder.DropTable(
                name: "backup_logs");

            migrationBuilder.DropTable(
                name: "compliance_policies");

            migrationBuilder.DropTable(
                name: "compliance_rules");

            migrationBuilder.DropTable(
                name: "ComplianceGaps");

            migrationBuilder.DropTable(
                name: "data_access_requests");

            migrationBuilder.DropTable(
                name: "DisasterRecoveryRunbooks");

            migrationBuilder.DropTable(
                name: "import_logs");

            migrationBuilder.DropTable(
                name: "migration_verification_logs");

            migrationBuilder.DropTable(
                name: "recovery_logs");

            migrationBuilder.DropTable(
                name: "RecoveryTestRecords");

            migrationBuilder.DropTable(
                name: "restoration_test_logs");

            migrationBuilder.DropTable(
                name: "ComplianceVerificationLogs");

            migrationBuilder.DropIndex(
                name: "IX_WaitlistEntry_TenantId_Id",
                table: "waitlist_entries");

            migrationBuilder.DropIndex(
                name: "IX_Patient_TenantId_Id",
                table: "patients");

            migrationBuilder.DropIndex(
                name: "IX_NotificationLog_TenantId_NotificationId",
                table: "notification_logs");

            migrationBuilder.DropIndex(
                name: "IX_IntakeData_TenantId_Id",
                table: "intake_data");

            migrationBuilder.DropIndex(
                name: "IX_ClinicalDocument_TenantId_Id",
                table: "clinical_documents");

            migrationBuilder.DropIndex(
                name: "IX_Appointment_TenantId_Id",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "waitlist_entries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "intake_data");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "appointments");
        }
    }
}
