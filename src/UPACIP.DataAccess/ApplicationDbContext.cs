using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess;

/// <summary>
/// Application database context for the UPACIP platform.
/// Inherits from <see cref="IdentityDbContext{TUser,TRole,TKey}"/> to co-locate
/// ASP.NET Core Identity tables in the same PostgreSQL schema.
/// </summary>
public sealed class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // -------------------------------------------------------------------------
    // Domain DbSets
    // -------------------------------------------------------------------------

    public DbSet<Patient>          Patients          => Set<Patient>();
    public DbSet<Appointment>      Appointments      => Set<Appointment>();
    public DbSet<IntakeData>       IntakeRecords     => Set<IntakeData>();
    public DbSet<ClinicalDocument> ClinicalDocuments => Set<ClinicalDocument>();
    public DbSet<ExtractedData>    ExtractedData     => Set<ExtractedData>();

    /// <summary>
    /// Per-attempt AI parsing failure records for exponential-backoff retry and terminal-failure audit (US_039 task_004, AC-4, AC-5, EC-1).
    /// </summary>
    public DbSet<DocumentParsingAttempt> DocumentParsingAttempts => Set<DocumentParsingAttempt>();
    public DbSet<MedicalCode>      MedicalCodes      => Set<MedicalCode>();
    public DbSet<AuditLog>         AuditLogs         => Set<AuditLog>();
    public DbSet<QueueEntry>       QueueEntries      => Set<QueueEntry>();
    public DbSet<NotificationLog>  NotificationLogs  => Set<NotificationLog>();

    /// <summary>
    /// Per-attempt audit records for every notification send and orchestration retry (US_037 AC-1, AC-4).
    /// </summary>
    public DbSet<NotificationDeliveryAttempt> NotificationDeliveryAttempts => Set<NotificationDeliveryAttempt>();

    /// <summary>Email verification tokens for the patient registration flow (US_012).</summary>
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();

    /// <summary>Session audit history for HIPAA compliance (7-year retention, DR-016). US_014.</summary>
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    /// <summary>Password reset tokens for the password-reset flow (US_015, FR-005).</summary>
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    /// <summary>
    /// Provider weekly availability templates used to generate bookable time slots (US_017).
    /// Seed data provides 3 sample providers with Mon–Fri schedules.
    /// </summary>
    public DbSet<ProviderAvailabilityTemplate> ProviderAvailabilityTemplates => Set<ProviderAvailabilityTemplate>();

    /// <summary>Patient waitlist registrations for fully-booked slots (US_020).</summary>
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();

    /// <summary>
    /// Reminder batch checkpoint cursors for 24-hour and 2-hour reminder workers (US_035 EC-1, EC-2).
    /// One row per (BatchType, WindowDateUtc); upserted after each successfully processed appointment.
    /// </summary>
    public DbSet<ReminderBatchCheckpoint> ReminderBatchCheckpoints => Set<ReminderBatchCheckpoint>();

    /// <summary>
    /// Dummy insurance validation reference records used by the soft pre-check
    /// during manual intake (US_031, AC-2, FR-033).
    /// Seeded by the AddMinorGuardianAndInsuranceValidation migration.
    /// </summary>
    public DbSet<InsuranceValidationRecord> InsuranceValidationRecords => Set<InsuranceValidationRecord>();

    /// <summary>
    /// Patient profile consolidation version history (US_043, AC-2, FR-056).
    /// Each row records a consolidation event with timestamp, user attribution,
    /// source document list, and the data delta snapshot.
    /// </summary>
    public DbSet<PatientProfileVersion> PatientProfileVersions => Set<PatientProfileVersion>();

    /// <summary>
    /// Clinical data conflicts detected by the AI conflict detection service (US_044, AC-2, AC-3, AC-5, FR-053).
    /// Each row records a conflict lifecycle from initial detection through staff review to resolution or dismissal.
    /// </summary>
    public DbSet<ClinicalConflict> ClinicalConflicts => Set<ClinicalConflict>();

    /// <summary>
    /// ICD-10 code reference library used for AI-generated diagnosis code validation (US_047, DR-015, FR-063).
    /// Each row represents one code entry in a specific quarterly library version.
    /// Active-code lookups use the composite index on (code_value, is_current).
    /// </summary>
    public DbSet<Icd10CodeLibrary> Icd10CodeLibrary => Set<Icd10CodeLibrary>();

    /// <summary>
    /// CPT procedure code reference library for AI-generated procedure code validation (US_048, AC-4, DR-015, FR-066).
    /// Quarterly refresh sets <c>is_active = false</c> for expired codes.
    /// The EF Core migration and seed data are created by task_003_db_cpt_code_library.
    /// </summary>
    public DbSet<CptCodeLibrary> CptCodeLibrary => Set<CptCodeLibrary>();

    /// <summary>
    /// CPT bundle rule reference table defining which individual CPT codes may be consolidated
    /// into a composite bundle code (US_048 AC-3, task_003_db_cpt_code_library).
    /// Surfaced to staff reviewers when the AI coding pipeline identifies bundling opportunities.
    /// </summary>
    public DbSet<CptBundleRule> CptBundleRules => Set<CptBundleRule>();

    /// <summary>
    /// Immutable append-only audit trail of every staff action taken on a <c>MedicalCode</c> record
    /// (US_049, AC-2, AC-4, FR-066, HIPAA).  Rows must never be updated or deleted.
    /// </summary>
    public DbSet<CodingAuditLog> CodingAuditLogs => Set<CodingAuditLog>();

    /// <summary>
    /// Daily snapshot of AI-human coding agreement rate metrics (US_050, AC-1, AC-2, FR-067).
    /// One row per calendar day, upserted by the agreement-rate calculation job.
    /// </summary>
    public DbSet<AgreementRateMetric> AgreementRateMetrics => Set<AgreementRateMetric>();

    /// <summary>
    /// Immutable records of individual discrepancies between AI-suggested and staff-selected codes
    /// (US_050, FR-068).  Written whenever staff override an AI suggestion.
    /// </summary>
    public DbSet<CodingDiscrepancy> CodingDiscrepancies => Set<CodingDiscrepancy>();

    /// <summary>
    /// Payer-specific and CMS-default code validation rules used by the payer rule
    /// validation service (US_051, AC-1, AC-2, task_003_db_payer_rules_schema).
    /// </summary>
    public DbSet<PayerRule> PayerRules => Set<PayerRule>();

    /// <summary>
    /// NCCI procedure-to-procedure bundling edits (US_051, AC-4, task_003_db_payer_rules_schema).
    /// </summary>
    public DbSet<BundlingEdit> BundlingEdits => Set<BundlingEdit>();

    /// <summary>
    /// CPT billing modifier reference data (US_051, AC-4, task_003_db_payer_rules_schema).
    /// </summary>
    public DbSet<CodeModifier> CodeModifiers => Set<CodeModifier>();

    /// <summary>
    /// Payer rule violations detected during validation runs, with full resolution audit trail
    /// (US_051, AC-2, task_003_db_payer_rules_schema).
    /// </summary>
    public DbSet<PayerRuleViolation> PayerRuleViolations => Set<PayerRuleViolation>();

    /// <summary>
    /// Immutable queue-specific audit trail recording every priority change and manual reorder
    /// performed by staff (US_054 AC-3, TR-028). Richer than the general AuditLog — includes
    /// original/new position and priority for queue reconstruction and compliance reporting.
    /// Append-only; no update or delete paths are exposed.
    /// </summary>
    public DbSet<QueueAuditLog> QueueAuditLogs => Set<QueueAuditLog>();

    /// <summary>
    /// System-wide key-value configuration table (US_055 AC-3, DR-009).
    /// Stores runtime-configurable platform settings such as the wait time alert threshold
    /// (<c>queue.wait_threshold_minutes</c>). Values are also cached in Redis (60s TTL)
    /// for low-latency reads; this table acts as the source of truth on cache miss.
    /// </summary>
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();

    /// <summary>
    /// Pre-aggregated daily queue metrics for history reporting and CSV export (US_056 AC-3, AC-4).
    /// One row per (summary_date, provider_id, appointment_type) partition.
    /// Null provider_id / appointment_type represent all-provider / all-type roll-ups.
    /// </summary>
    public DbSet<QueueDailySummary> QueueDailySummaries => Set<QueueDailySummary>();

    /// <summary>
    /// Pre-aggregated daily system metrics snapshots for the Admin Dashboard trend charts
    /// (US_058 AC-1, AC-2, NFR-004).  One row per UTC calendar day; upserted by the
    /// metrics service or a background aggregation job.
    /// </summary>
    public DbSet<SystemMetricsSnapshot> SystemMetricsSnapshots => Set<SystemMetricsSnapshot>();

    /// <summary>
    /// Notification message template definitions for the Admin Configuration UI
    /// (US_058 AC-3, FR-095).  Each row defines the channel, trigger event, and message body
    /// skeleton for a notification type (e.g. appointment reminder, cancellation notice).
    /// Distinct from <c>NotificationLog</c> which records individual delivery attempts.
    /// </summary>
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();

    /// <summary>
    /// Singleton risk configuration record including scoring parameter weights
    /// and deferred recalculation flag (US_060 AC-3, AC-4).
    /// The table is designed to hold exactly one row seeded by migration
    /// <c>20260426000003_AddNotificationTemplateAndRiskConfigSchema</c>.
    /// All writes go through the Admin Configuration UI and are protected
    /// by an optimistic-concurrency token.
    /// </summary>
    public DbSet<RiskConfiguration> RiskConfigurations => Set<RiskConfiguration>();

    /// <summary>
    /// Admin-configurable weekly slot template headers — one per (provider, day-of-week)
    /// combination (US_059 AC-1, AC-2).  Child blocks are stored in
    /// <see cref="SlotTemplateBlocks"/>.
    /// </summary>
    public DbSet<SlotTemplate> SlotTemplates => Set<SlotTemplate>();

    /// <summary>
    /// Child time-block records within a <see cref="SlotTemplate"/> defining per-hour
    /// appointment types and availability (US_059 AC-1).
    /// Cascade-deleted with their parent <see cref="SlotTemplate"/>.
    /// </summary>
    public DbSet<SlotTemplateBlock> SlotTemplateBlocks => Set<SlotTemplateBlock>();

    /// <summary>
    /// Clinic-wide business hours — one row per day of the week (US_059 AC-3).
    /// Seeded with Mon–Fri 08:00–17:00, Sat 09:00–13:00, Sun closed.
    /// </summary>
    public DbSet<BusinessHours> BusinessHours => Set<BusinessHours>();

    /// <summary>
    /// Holiday definitions that block appointment slots for a specific date (US_059 AC-4).
    /// Supports soft-delete (<c>DeletedAt</c>) and recurring annual holidays.
    /// </summary>
    public DbSet<Holiday> Holidays => Set<Holiday>();

    // ── AI Cost Tracking (US_071) ─────────────────────────────────────────────

    /// <summary>
    /// Immutable per-request AI cost records appended by the AI Gateway after every LLM call
    /// (US_071 TASK_001, AC-1).  Stores provider, request type, token counts, estimated cost,
    /// cost source (actual vs. approximate), and correlation ID for cross-service tracing.
    /// Append-only — no UPDATE or DELETE paths are exposed.
    /// </summary>
    public DbSet<AiRequestLog> AiRequestLogs => Set<AiRequestLog>();

    /// <summary>
    /// Per-provider AI cost budget configuration and rate card (US_071 TASK_001, AC-1, AC-2).
    /// One row per provider (unique constraint on <c>Provider</c>).
    /// Seeded by migration <c>AddAiCostTrackingTables</c> with GPT-4o-mini and Claude 3.5 Sonnet defaults.
    /// </summary>
    public DbSet<AiCostBudgetConfig> AiCostBudgetConfigs => Set<AiCostBudgetConfig>();

    /// <summary>
    /// Pre-aggregated daily AI cost rollup by provider and request type (US_071 TASK_001, AC-1).
    /// Written by the daily aggregation job; unique composite constraint on
    /// (<c>SummaryDate</c>, <c>Provider</c>, <c>RequestType</c>) enables safe upserts.
    /// Used by the admin cost dashboard for O(log n) date-range queries (NFR-004).
    /// </summary>
    public DbSet<AiCostDailySummary> AiCostDailySummaries => Set<AiCostDailySummary>();

    // ── AI Performance Metrics (US_072) ──────────────────────────────────────

    /// <summary>
    /// Daily aggregated AI accuracy metrics (agreement rate, precision, recall) for the
    /// AI monitoring dashboard (US_072 task_001, AC-1, AC-2).
    /// Composite unique constraint on (MetricDate, MetricType) prevents duplicate daily rows.
    /// </summary>
    public DbSet<AiAccuracyMetric> AiAccuracyMetrics => Set<AiAccuracyMetric>();

    /// <summary>
    /// Daily aggregated AI latency percentile metrics (P50, P95 per operation type) for the
    /// AI monitoring dashboard (US_072 task_001, AC-3).
    /// Composite unique constraint on (MetricDate, OperationType) prevents duplicate daily rows.
    /// </summary>
    public DbSet<AiLatencyMetric> AiLatencyMetrics => Set<AiLatencyMetric>();

    /// <summary>
    /// Configurable alert threshold definitions per AI metric name (US_072 task_001, AC-4).
    /// Unique constraint on MetricName enables safe upserts and direct lookup.
    /// </summary>
    public DbSet<AiMetricThreshold> AiMetricThresholds => Set<AiMetricThreshold>();

    /// <summary>
    /// Alert records generated when a daily AI metric calculation drops below its configured
    /// threshold (US_072 task_001, AC-4). Stores metric name, current/target values, and
    /// trend direction. Acknowledged by an admin user via the dashboard.
    /// </summary>
    public DbSet<AiMetricAlert> AiMetricAlerts => Set<AiMetricAlert>();

    // ── Confidence Score Calibration (US_073) ─────────────────────────────────

    /// <summary>
    /// Per-category Platt-scaling calibration parameters used to transform raw AI confidence
    /// scores into calibrated probabilities (US_073 task_001, AC-1).
    /// One active row per <c>DataType</c>; enforced by a unique filtered index on
    /// (<c>DataType</c>, <c>IsActive</c>) WHERE <c>is_active = true</c>.
    /// </summary>
    public DbSet<CalibrationParameter> CalibrationParameters => Set<CalibrationParameter>();

    /// <summary>
    /// Weekly calibration run results storing predicted vs. actual accuracy per confidence bin
    /// per clinical data category (US_073 task_001, AC-3).
    /// One row per (CalibrationRunDate, DataType, BinStart) produced by the weekly calibration job.
    /// </summary>
    public DbSet<CalibrationRecord> CalibrationRecords => Set<CalibrationRecord>();

    /// <summary>
    /// Alert records generated when calibration drift exceeds the 5% threshold between
    /// predicted and actual accuracy for a clinical data category (US_073 task_001, AC-4).
    /// Acknowledged by an admin user; append-only — no update paths are exposed.
    /// </summary>
    public DbSet<CalibrationDriftAlert> CalibrationDriftAlerts => Set<CalibrationDriftAlert>();

    // ── Hallucination Tracking (US_074 task_002) ──────────────────────────────

    /// <summary>
    /// Per-justification staff verification records classifying AI-generated medical
    /// justifications as Supported, Unsupported (hallucination), or PartiallySupported
    /// (US_074 task_002, AC-1, AIR-Q06).
    /// </summary>
    public DbSet<HallucinationRecord> HallucinationRecords => Set<HallucinationRecord>();

    /// <summary>
    /// Pre-aggregated daily hallucination rate metrics (US_074 task_002, AC-1).
    /// One row per calendar day; unique index on MetricDate prevents duplicates.
    /// </summary>
    public DbSet<HallucinationMetric> HallucinationMetrics => Set<HallucinationMetric>();

    /// <summary>
    /// Critical alert records generated when the daily hallucination rate exceeds 5%,
    /// or retroactively when staff discover hallucinations in approved entries (US_074 task_002, AC-2).
    /// Append-only; acknowledged by an admin user via the dashboard.
    /// </summary>
    public DbSet<HallucinationAlert> HallucinationAlerts => Set<HallucinationAlert>();

    // NOTE: Embedding entity types (MedicalTerminologyEmbedding, IntakeTemplateEmbedding,
    // CodingGuidelineEmbedding) are intentionally excluded from the EF Core model.
    // These tables are provisioned by scripts/provision-pgvector.sql (requires superuser to
    // CREATE EXTENSION vector) and are accessed exclusively via raw NpgsqlCommand in
    // VectorSearchService. The Pgvector.Vector type cannot be mapped by EF Core without
    // pgvector installed on the target PostgreSQL server at design time.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Embedding entity types use Pgvector.Vector which cannot be mapped by EF Core
        // without pgvector installed at design time. Exclude them so migrations and model
        // building succeed on any machine, regardless of pgvector OS installation.
        // VectorSearchService accesses these tables via raw NpgsqlCommand only.
        modelBuilder.Ignore<MedicalTerminologyEmbedding>();
        modelBuilder.Ignore<IntakeTemplateEmbedding>();
        modelBuilder.Ignore<CodingGuidelineEmbedding>();

        // ---------- Identity table name mapping ----------
        // Use asp_net_ prefix + snake_case to follow PostgreSQL naming conventions.
        modelBuilder.Entity<ApplicationUser>()       .ToTable("asp_net_users");
        modelBuilder.Entity<ApplicationRole>()       .ToTable("asp_net_roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("asp_net_user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("asp_net_role_claims");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens");

        // ---------- Seed roles ----------
        // Role HasData() is now managed by RoleSeedConfiguration (Seeding/RoleSeedConfiguration.cs),
        // which is auto-discovered below via ApplyConfigurationsFromAssembly.
        // Stable GUIDs: a1b2c3d4... (Patient), b2c3d4e5... (Staff), c3d4e5f6... (Admin).

        // Fluent API entity configurations — auto-discovered from all IEntityTypeConfiguration<T>
        // implementations in this assembly. No manual registration needed when a new configuration
        // class is added to the Configurations/ folder.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}

