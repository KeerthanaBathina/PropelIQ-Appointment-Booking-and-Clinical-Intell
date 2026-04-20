using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class CreateDomainEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    LogId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.LogId);
                    table.ForeignKey(
                        name: "FK_audit_logs_asp_net_users_UserId",
                        column: x => x.UserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmergencyContact = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsWalkIn = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreferredSlotCriteria = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_appointments_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "clinical_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentCategory = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UploadDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploaderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessingStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clinical_documents_asp_net_users_UploaderUserId",
                        column: x => x.UploaderUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinical_documents_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "intake_data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntakeMethod = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InsuranceInfo = table.Column<string>(type: "jsonb", nullable: true),
                    MandatoryFields = table.Column<string>(type: "jsonb", nullable: true),
                    OptionalFields = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intake_data", x => x.Id);
                    table.ForeignKey(
                        name: "FK_intake_data_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medical_codes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CodeValue = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SuggestedByAi = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AiConfidenceScore = table.Column<float>(type: "real", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_medical_codes_asp_net_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_medical_codes_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_logs",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeliveryChannel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_logs", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_notification_logs_appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "queue_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArrivalTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WaitTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_queue_entries_appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "extracted_data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DataType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConfidenceScore = table.Column<float>(type: "real", nullable: false),
                    SourceAttribution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FlaggedForReview = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataContent = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_extracted_data", x => x.Id);
                    table.ForeignKey(
                        name: "FK_extracted_data_asp_net_users_VerifiedByUserId",
                        column: x => x.VerifiedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_extracted_data_clinical_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "clinical_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_appointment_time",
                table: "appointments",
                column: "AppointmentTime");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_patient_id",
                table: "appointments",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp",
                table: "audit_logs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id",
                table: "audit_logs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_documents_patient_id",
                table: "clinical_documents",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_documents_processing_status",
                table: "clinical_documents",
                column: "ProcessingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_documents_UploaderUserId",
                table: "clinical_documents",
                column: "UploaderUserId");

            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_document_id",
                table: "extracted_data",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_extracted_data_VerifiedByUserId",
                table: "extracted_data",
                column: "VerifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_intake_data_patient_id",
                table: "intake_data",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_codes_ApprovedByUserId",
                table: "medical_codes",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_medical_codes_patient_codetype_codevalue",
                table: "medical_codes",
                columns: new[] { "PatientId", "CodeType", "CodeValue" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_appointment_id",
                table: "notification_logs",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "ix_patients_email",
                table: "patients",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_queue_entries_appointment_id",
                table: "queue_entries",
                column: "AppointmentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "extracted_data");

            migrationBuilder.DropTable(
                name: "intake_data");

            migrationBuilder.DropTable(
                name: "medical_codes");

            migrationBuilder.DropTable(
                name: "notification_logs");

            migrationBuilder.DropTable(
                name: "queue_entries");

            migrationBuilder.DropTable(
                name: "clinical_documents");

            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropTable(
                name: "patients");
        }
    }
}
