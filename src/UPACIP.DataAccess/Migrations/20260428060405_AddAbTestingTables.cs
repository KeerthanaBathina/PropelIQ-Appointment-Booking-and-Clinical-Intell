using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAbTestingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use conditional DDL so this migration is idempotent when the database was
            // provisioned via the SQL script (which already added these columns).
            migrationBuilder.Sql(@"ALTER TABLE extracted_data ADD COLUMN IF NOT EXISTS ""CalibratedConfidenceScore"" real;");
            migrationBuilder.Sql(@"ALTER TABLE extracted_data ADD COLUMN IF NOT EXISTS ""CalibrationStatus"" character varying(20) NOT NULL DEFAULT 'Uncalibrated';");
            migrationBuilder.Sql(@"ALTER TABLE asp_net_users ADD COLUMN IF NOT EXISTS ""DeactivatedAt"" timestamp with time zone;");
            migrationBuilder.Sql(@"ALTER TABLE asp_net_users ADD COLUMN IF NOT EXISTS ""DeactivatedBy"" uuid;");
            migrationBuilder.Sql(@"ALTER TABLE appointments ADD COLUMN IF NOT EXISTS ""SlotTemplateId"" uuid;");

            migrationBuilder.CreateTable(
                name: "ab_experiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ControlModelId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CandidateModelId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TrafficSplitPercentage = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ab_experiments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_accuracy_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricDate = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    MetricType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    TargetValue = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_accuracy_metrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_cost_budget_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DailyBudgetThreshold = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    AlertEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CostPer1kInputTokens = table.Column<decimal>(type: "numeric(10,6)", nullable: false),
                    CostPer1kOutputTokens = table.Column<decimal>(type: "numeric(10,6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_cost_budget_configs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_cost_daily_summaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummaryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TotalInputTokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    TotalOutputTokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    TotalEstimatedCost = table.Column<decimal>(type: "numeric(12,6)", nullable: false, defaultValue: 0m),
                    RequestCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ApproximateRequestCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_cost_daily_summaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_latency_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricDate = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    P50Milliseconds = table.Column<double>(type: "double precision", nullable: false),
                    P95Milliseconds = table.Column<double>(type: "double precision", nullable: false),
                    TargetP95Milliseconds = table.Column<double>(type: "double precision", nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_latency_metrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_metric_alerts",
                columns: table => new
                {
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    MetricName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CurrentValue = table.Column<double>(type: "double precision", nullable: false),
                    TargetValue = table.Column<double>(type: "double precision", nullable: false),
                    TrendDirection = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_metric_alerts", x => x.AlertId);
                    table.ForeignKey(
                        name: "FK_ai_metric_alerts_asp_net_users_AcknowledgedByUserId",
                        column: x => x.AcknowledgedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ai_metric_thresholds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetValue = table.Column<double>(type: "double precision", nullable: false),
                    WarningValue = table.Column<double>(type: "double precision", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_metric_thresholds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ai_request_logs",
                columns: table => new
                {
                    LogId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    CostSource = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_request_logs", x => x.LogId);
                });

            migrationBuilder.CreateTable(
                name: "business_hours",
                columns: table => new
                {
                    BusinessHoursId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    OpenTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    CloseTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_hours", x => x.BusinessHoursId);
                    table.CheckConstraint("ck_business_hours_open_close_valid", "\"IsClosed\" = true OR (\"OpenTime\" IS NOT NULL AND \"CloseTime\" IS NOT NULL AND \"OpenTime\" < \"CloseTime\")");
                    table.ForeignKey(
                        name: "FK_business_hours_asp_net_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "calibration_drift_alerts",
                columns: table => new
                {
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    DataType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PredictedAccuracy = table.Column<double>(type: "double precision", nullable: false),
                    ActualAccuracy = table.Column<double>(type: "double precision", nullable: false),
                    DriftPercentage = table.Column<double>(type: "double precision", nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calibration_drift_alerts", x => x.AlertId);
                    table.ForeignKey(
                        name: "FK_calibration_drift_alerts_asp_net_users_AcknowledgedByUserId",
                        column: x => x.AcknowledgedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "calibration_parameters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DataType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Slope = table.Column<double>(type: "double precision", nullable: false),
                    Intercept = table.Column<double>(type: "double precision", nullable: false),
                    LastCalibratedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    VerificationSampleSize = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calibration_parameters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "calibration_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationRunDate = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    DataType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PredictedAccuracy = table.Column<double>(type: "double precision", nullable: false),
                    ActualAccuracy = table.Column<double>(type: "double precision", nullable: false),
                    DriftPercentage = table.Column<double>(type: "double precision", nullable: false),
                    BinStart = table.Column<int>(type: "integer", nullable: false),
                    BinEnd = table.Column<int>(type: "integer", nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    DriftDetected = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calibration_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "hallucination_alerts",
                columns: table => new
                {
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    CurrentRate = table.Column<double>(type: "double precision", nullable: false),
                    TargetRate = table.Column<double>(type: "double precision", nullable: false),
                    Recommendation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsRetroactive = table.Column<bool>(type: "boolean", nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hallucination_alerts", x => x.AlertId);
                    table.ForeignKey(
                        name: "FK_hallucination_alerts_asp_net_users_AcknowledgedByUserId",
                        column: x => x.AcknowledgedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hallucination_metrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricDate = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    TotalVerified = table.Column<int>(type: "integer", nullable: false),
                    HallucinationCount = table.Column<int>(type: "integer", nullable: false),
                    PartiallySupportedCount = table.Column<int>(type: "integer", nullable: false),
                    HallucinationRate = table.Column<double>(type: "double precision", nullable: false),
                    TargetRate = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hallucination_metrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "hallucination_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSupportStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VerificationNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    IsRetroactive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hallucination_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_hallucination_records_medical_codes_MedicalCodeId",
                        column: x => x.MedicalCodeId,
                        principalTable: "medical_codes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                columns: table => new
                {
                    HolidayId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsHalfDay = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holidays", x => x.HolidayId);
                    table.ForeignKey(
                        name: "FK_holidays_asp_net_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notification_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TriggerEvent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MessageBody = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AllowedVariables = table.Column<string>(type: "jsonb", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "queue_daily_summary",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummaryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AvgWaitTimeMinutes = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    NoShowCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CompletedCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalPatients = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_daily_summary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_queue_daily_summary_asp_net_users_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "risk_configuration",
                columns: table => new
                {
                    RiskConfigId = table.Column<Guid>(type: "uuid", nullable: false),
                    HighRiskThreshold = table.Column<int>(type: "integer", nullable: false, defaultValue: 75),
                    MediumRiskThreshold = table.Column<int>(type: "integer", nullable: false, defaultValue: 45),
                    MinAppointmentsForAiScore = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    AutoOutreach = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ScoringParameters = table.Column<string>(type: "jsonb", nullable: false),
                    RecalculationPending = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastRecalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_configuration", x => x.RiskConfigId);
                    table.CheckConstraint("ck_risk_configuration_high_threshold_range", "\"HighRiskThreshold\" >= 0 AND \"HighRiskThreshold\" <= 100");
                    table.CheckConstraint("ck_risk_configuration_medium_threshold_range", "\"MediumRiskThreshold\" >= 0 AND \"MediumRiskThreshold\" <= 100");
                    table.ForeignKey(
                        name: "FK_risk_configuration_asp_net_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "slot_templates",
                columns: table => new
                {
                    SlotTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slot_templates", x => x.SlotTemplateId);
                    table.ForeignKey(
                        name: "FK_slot_templates_asp_net_users_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "system_metrics_snapshots",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ActiveUsers = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    DailyAppointments = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    NoShowRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    AiAgreementRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    UptimePercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 100m),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_metrics_snapshots", x => x.SnapshotId);
                });

            migrationBuilder.CreateTable(
                name: "ab_metric_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Variant = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Accuracy = table.Column<float>(type: "real", nullable: true),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    TokensUsed = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    RequestType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ab_metric_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ab_metric_records_ab_experiments_ExperimentId",
                        column: x => x.ExperimentId,
                        principalTable: "ab_experiments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "slot_template_blocks",
                columns: table => new
                {
                    BlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    AppointmentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slot_template_blocks", x => x.BlockId);
                    table.CheckConstraint("ck_slot_template_blocks_end_after_start", "\"EndTime\" > \"StartTime\"");
                    table.ForeignKey(
                        name: "FK_slot_template_blocks_slot_templates_SlotTemplateId",
                        column: x => x.SlotTemplateId,
                        principalTable: "slot_templates",
                        principalColumn: "SlotTemplateId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ai_cost_budget_configs",
                columns: new[] { "Id", "AlertEnabled", "CostPer1kInputTokens", "CostPer1kOutputTokens", "CreatedAt", "DailyBudgetThreshold", "Provider", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("e0f1a2b3-c4d5-6789-abcd-ef0123456701"), true, 0.000150m, 0.000600m, new DateTime(2026, 4, 27, 0, 0, 0, 0, DateTimeKind.Utc), 5.00m, "OpenAI", new DateTime(2026, 4, 27, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("e0f1a2b3-c4d5-6789-abcd-ef0123456702"), true, 0.003000m, 0.015000m, new DateTime(2026, 4, 27, 0, 0, 0, 0, DateTimeKind.Utc), 20.00m, "Anthropic", new DateTime(2026, 4, 27, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "business_hours",
                columns: new[] { "BusinessHoursId", "CloseTime", "DayOfWeek", "IsClosed", "OpenTime", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { new Guid("b0590001-0059-0001-0000-000000000000"), null, 0, true, null, new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.InsertData(
                table: "business_hours",
                columns: new[] { "BusinessHoursId", "CloseTime", "DayOfWeek", "OpenTime", "UpdatedAt", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("b0590002-0059-0001-0000-000000000000"), new TimeOnly(17, 0, 0), 1, new TimeOnly(8, 0, 0), new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("b0590003-0059-0001-0000-000000000000"), new TimeOnly(17, 0, 0), 2, new TimeOnly(8, 0, 0), new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("b0590004-0059-0001-0000-000000000000"), new TimeOnly(17, 0, 0), 3, new TimeOnly(8, 0, 0), new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("b0590005-0059-0001-0000-000000000000"), new TimeOnly(17, 0, 0), 4, new TimeOnly(8, 0, 0), new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("b0590006-0059-0001-0000-000000000000"), new TimeOnly(17, 0, 0), 5, new TimeOnly(8, 0, 0), new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("b0590007-0059-0001-0000-000000000000"), new TimeOnly(13, 0, 0), 6, new TimeOnly(9, 0, 0), new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.InsertData(
                table: "notification_templates",
                columns: new[] { "Id", "AllowedVariables", "Channel", "CreatedAt", "IsActive", "MessageBody", "Subject", "TemplateName", "TriggerEvent", "UpdatedAt", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("a1b2c3d4-e5f6-7890-abcd-000000000001"), null, "Email", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), true, "Dear {{PatientName}},\n\nThis is a reminder of your appointment on {{AppointmentDate}} at {{AppointmentTime}} with {{ProviderName}}.\n\nIf you need to reschedule or cancel, please contact us at least 24 hours in advance.\n\nThank you,\nThe Care Team", null, "Appointment Reminder", "Reminder24h", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("a7b8c9d0-e1f2-3456-0123-000000000007"), "[\"patient_name\",\"date\",\"time\"]", "SMS", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), true, "Reminder: {{patient_name}}, you have an appointment tomorrow {{date}} at {{time}}. Reply CANCEL to cancel.", null, "24h Reminder (SMS)", "Reminder24h", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("b2c3d4e5-f6a7-8901-bcde-000000000002"), null, "SMS", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), true, "Hi {{PatientName}}, your appointment on {{AppointmentDate}} at {{AppointmentTime}} has been cancelled. Call us to reschedule.", null, "Cancellation Notice", "AppointmentCancelled", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("b8c9d0e1-f2a3-4567-1234-000000000008"), "[\"patient_name\",\"date\",\"time\",\"provider\"]", "Email", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), true, "Dear {{patient_name}},\n\nYour appointment with {{provider}} is in 2 hours at {{time}} today, {{date}}.\n\nPlease make sure you arrive on time.\n\nThank you,\nThe Care Team", "Appointment in 2 Hours", "2h Reminder (Email)", "Reminder2h", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("c3d4e5f6-a7b8-9012-cdef-000000000003"), null, "Email", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), true, "Dear {{PatientName}},\n\nWe noticed you missed your appointment on {{AppointmentDate}} at {{AppointmentTime}}.\n\nPlease contact us to schedule a new appointment at your earliest convenience.\n\nThank you,\nThe Care Team", null, "No-Show Alert", "PatientNoShow", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("c9d0e1f2-a3b4-5678-2345-000000000009"), "[\"patient_name\",\"time\",\"provider\"]", "SMS", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), true, "{{patient_name}}, your appointment with {{provider}} is in 2 hours at {{time}}. See you soon!", null, "2h Reminder (SMS)", "Reminder2h", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("d4e5f6a7-b8c9-0123-def0-000000000004"), "[\"patient_name\",\"date\",\"time\",\"provider\"]", "Email", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), true, "Dear {{patient_name}},\n\nYour appointment has been confirmed for {{date}} at {{time}} with {{provider}}.\n\nPlease arrive 10 minutes early. If you need to reschedule, contact us at least 24 hours in advance.\n\nThank you,\nThe Care Team", "Appointment Confirmed", "Booking Confirmation (Email)", "AppointmentBooked", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("e5f6a7b8-c9d0-1234-ef01-000000000005"), "[\"patient_name\",\"date\",\"time\"]", "SMS", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), true, "Hi {{patient_name}}, your appointment is confirmed for {{date}} at {{time}}. Reply CANCEL to cancel.", null, "Booking Confirmation (SMS)", "AppointmentBooked", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("f6a7b8c9-d0e1-2345-f012-000000000006"), "[\"patient_name\",\"date\",\"time\",\"provider\"]", "Email", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), true, "Dear {{patient_name}},\n\nThis is a reminder that you have an appointment tomorrow, {{date}}, at {{time}} with {{provider}}.\n\nIf you need to cancel, please let us know at least 24 hours in advance.\n\nThank you,\nThe Care Team", "Appointment Reminder — Tomorrow", "24h Reminder (Email)", "Reminder24h", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.InsertData(
                table: "risk_configuration",
                columns: new[] { "RiskConfigId", "AutoOutreach", "HighRiskThreshold", "LastRecalculatedAt", "MediumRiskThreshold", "MinAppointmentsForAiScore", "ScoringParameters", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { new Guid("d0e1f2a3-b4c5-6789-abcd-000000000010"), true, 75, null, 45, 3, "{\"priorNoShowsWeight\":0.50,\"cancellationHistoryWeight\":0.30,\"appointmentLeadTimeWeight\":0.20}", new DateTime(2026, 4, 26, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.CreateIndex(
                name: "ix_queue_entries_status_arrival_timestamp",
                table: "queue_entries",
                columns: new[] { "Status", "ArrivalTimestamp" },
                filter: "status IN ('Waiting', 'InVisit')");

            migrationBuilder.CreateIndex(
                name: "ix_queue_entries_status_priority_created_at",
                table: "queue_entries",
                columns: new[] { "Status", "Priority", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_medical_codes_ai_pending_approval",
                table: "medical_codes",
                columns: new[] { "SuggestedByAi", "ApprovedByUserId" },
                filter: "suggested_by_ai = true AND approved_by_user_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_pending_review",
                table: "extracted_data",
                columns: new[] { "FlaggedForReview", "VerifiedByUserId" },
                filter: "flagged_for_review = true AND verified_by_user_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_asp_net_users_AccountStatus",
                table: "asp_net_users",
                column: "AccountStatus");

            migrationBuilder.CreateIndex(
                name: "IX_asp_net_users_DeactivatedBy",
                table: "asp_net_users",
                column: "DeactivatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_asp_net_users_LastLoginAt",
                table: "asp_net_users",
                column: "LastLoginAt");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_provider_id_appointment_time",
                table: "appointments",
                columns: new[] { "ProviderId", "AppointmentTime" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_SlotTemplateId",
                table: "appointments",
                column: "SlotTemplateId");

            migrationBuilder.CreateIndex(
                name: "ix_ab_experiments_active_unique",
                table: "ab_experiments",
                column: "Status",
                unique: true,
                filter: "\"status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_ab_experiments_start_date",
                table: "ab_experiments",
                column: "StartDate",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_ab_metric_records_experiment_id",
                table: "ab_metric_records",
                column: "ExperimentId");

            migrationBuilder.CreateIndex(
                name: "ix_ab_metric_records_experiment_variant_created",
                table: "ab_metric_records",
                columns: new[] { "ExperimentId", "Variant", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_accuracy_metrics_date_type",
                table: "ai_accuracy_metrics",
                columns: new[] { "MetricDate", "MetricType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_cost_budget_configs_provider",
                table: "ai_cost_budget_configs",
                column: "Provider",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_cost_daily_summaries_date",
                table: "ai_cost_daily_summaries",
                column: "SummaryDate",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_ai_cost_daily_summaries_date_provider_type",
                table: "ai_cost_daily_summaries",
                columns: new[] { "SummaryDate", "Provider", "RequestType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_latency_metrics_date_operation",
                table: "ai_latency_metrics",
                columns: new[] { "MetricDate", "OperationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ai_metric_alerts_AcknowledgedByUserId",
                table: "ai_metric_alerts",
                column: "AcknowledgedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_ai_metric_alerts_generated_at",
                table: "ai_metric_alerts",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "ix_ai_metric_alerts_is_acknowledged",
                table: "ai_metric_alerts",
                column: "IsAcknowledged");

            migrationBuilder.CreateIndex(
                name: "ix_ai_metric_thresholds_metric_name",
                table: "ai_metric_thresholds",
                column: "MetricName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_request_logs_correlation_id",
                table: "ai_request_logs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "ix_ai_request_logs_created_at",
                table: "ai_request_logs",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_ai_request_logs_provider_created_at",
                table: "ai_request_logs",
                columns: new[] { "Provider", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_business_hours_day_of_week",
                table: "business_hours",
                column: "DayOfWeek",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_hours_UpdatedByUserId",
                table: "business_hours",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_calibration_drift_alerts_AcknowledgedByUserId",
                table: "calibration_drift_alerts",
                column: "AcknowledgedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_drift_alerts_data_type",
                table: "calibration_drift_alerts",
                column: "DataType");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_drift_alerts_generated_at",
                table: "calibration_drift_alerts",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_drift_alerts_is_acknowledged",
                table: "calibration_drift_alerts",
                column: "IsAcknowledged");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_parameters_data_type",
                table: "calibration_parameters",
                column: "DataType");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_parameters_data_type_active",
                table: "calibration_parameters",
                columns: new[] { "DataType", "IsActive" },
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_records_drift_detected",
                table: "calibration_records",
                column: "DriftDetected");

            migrationBuilder.CreateIndex(
                name: "ix_calibration_records_run_date_data_type",
                table: "calibration_records",
                columns: new[] { "CalibrationRunDate", "DataType" });

            migrationBuilder.CreateIndex(
                name: "IX_hallucination_alerts_AcknowledgedByUserId",
                table: "hallucination_alerts",
                column: "AcknowledgedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_hallucination_alerts_generated_at",
                table: "hallucination_alerts",
                column: "GeneratedAt");

            migrationBuilder.CreateIndex(
                name: "ix_hallucination_alerts_is_acknowledged",
                table: "hallucination_alerts",
                column: "IsAcknowledged");

            migrationBuilder.CreateIndex(
                name: "ix_hallucination_alerts_is_retroactive",
                table: "hallucination_alerts",
                column: "IsRetroactive");

            migrationBuilder.CreateIndex(
                name: "ix_hallucination_metrics_metric_date",
                table: "hallucination_metrics",
                column: "MetricDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hallucination_records_medical_code_id_created_at",
                table: "hallucination_records",
                columns: new[] { "MedicalCodeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_hallucination_records_source_support_status",
                table: "hallucination_records",
                column: "SourceSupportStatus");

            migrationBuilder.CreateIndex(
                name: "ix_hallucination_records_verified_at",
                table: "hallucination_records",
                column: "VerifiedAt");

            migrationBuilder.CreateIndex(
                name: "IX_holidays_CreatedByUserId",
                table: "holidays",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_holidays_date",
                table: "holidays",
                column: "Date",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_channel_trigger",
                table: "notification_templates",
                columns: new[] { "Channel", "TriggerEvent" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_template_name",
                table: "notification_templates",
                column: "TemplateName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_queue_daily_summary_date",
                table: "queue_daily_summary",
                column: "SummaryDate");

            migrationBuilder.CreateIndex(
                name: "ix_queue_daily_summary_date_provider_type",
                table: "queue_daily_summary",
                columns: new[] { "SummaryDate", "ProviderId", "AppointmentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_queue_daily_summary_ProviderId",
                table: "queue_daily_summary",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_risk_configuration_UpdatedByUserId",
                table: "risk_configuration",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_slot_template_blocks_slot_template_id",
                table: "slot_template_blocks",
                column: "SlotTemplateId");

            migrationBuilder.CreateIndex(
                name: "ix_slot_templates_provider_id",
                table: "slot_templates",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "ix_slot_templates_provider_id_day_of_week",
                table: "slot_templates",
                columns: new[] { "ProviderId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_system_metrics_snapshots_metric_date",
                table: "system_metrics_snapshots",
                column: "MetricDate",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_appointments_slot_templates_SlotTemplateId",
                table: "appointments",
                column: "SlotTemplateId",
                principalTable: "slot_templates",
                principalColumn: "SlotTemplateId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_asp_net_users_asp_net_users_DeactivatedBy",
                table: "asp_net_users",
                column: "DeactivatedBy",
                principalTable: "asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_appointments_slot_templates_SlotTemplateId",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_asp_net_users_asp_net_users_DeactivatedBy",
                table: "asp_net_users");

            migrationBuilder.DropTable(
                name: "ab_metric_records");

            migrationBuilder.DropTable(
                name: "ai_accuracy_metrics");

            migrationBuilder.DropTable(
                name: "ai_cost_budget_configs");

            migrationBuilder.DropTable(
                name: "ai_cost_daily_summaries");

            migrationBuilder.DropTable(
                name: "ai_latency_metrics");

            migrationBuilder.DropTable(
                name: "ai_metric_alerts");

            migrationBuilder.DropTable(
                name: "ai_metric_thresholds");

            migrationBuilder.DropTable(
                name: "ai_request_logs");

            migrationBuilder.DropTable(
                name: "business_hours");

            migrationBuilder.DropTable(
                name: "calibration_drift_alerts");

            migrationBuilder.DropTable(
                name: "calibration_parameters");

            migrationBuilder.DropTable(
                name: "calibration_records");

            migrationBuilder.DropTable(
                name: "hallucination_alerts");

            migrationBuilder.DropTable(
                name: "hallucination_metrics");

            migrationBuilder.DropTable(
                name: "hallucination_records");

            migrationBuilder.DropTable(
                name: "holidays");

            migrationBuilder.DropTable(
                name: "notification_templates");

            migrationBuilder.DropTable(
                name: "queue_daily_summary");

            migrationBuilder.DropTable(
                name: "risk_configuration");

            migrationBuilder.DropTable(
                name: "slot_template_blocks");

            migrationBuilder.DropTable(
                name: "system_metrics_snapshots");

            migrationBuilder.DropTable(
                name: "ab_experiments");

            migrationBuilder.DropTable(
                name: "slot_templates");

            migrationBuilder.DropIndex(
                name: "ix_queue_entries_status_arrival_timestamp",
                table: "queue_entries");

            migrationBuilder.DropIndex(
                name: "ix_queue_entries_status_priority_created_at",
                table: "queue_entries");

            migrationBuilder.DropIndex(
                name: "ix_medical_codes_ai_pending_approval",
                table: "medical_codes");

            migrationBuilder.DropIndex(
                name: "ix_extracted_data_pending_review",
                table: "extracted_data");

            migrationBuilder.DropIndex(
                name: "IX_asp_net_users_AccountStatus",
                table: "asp_net_users");

            migrationBuilder.DropIndex(
                name: "IX_asp_net_users_DeactivatedBy",
                table: "asp_net_users");

            migrationBuilder.DropIndex(
                name: "IX_asp_net_users_LastLoginAt",
                table: "asp_net_users");

            migrationBuilder.DropIndex(
                name: "ix_appointments_provider_id_appointment_time",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "IX_appointments_SlotTemplateId",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "CalibratedConfidenceScore",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "CalibrationStatus",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "DeactivatedAt",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "DeactivatedBy",
                table: "asp_net_users");

            migrationBuilder.DropColumn(
                name: "SlotTemplateId",
                table: "appointments");
        }
    }
}
