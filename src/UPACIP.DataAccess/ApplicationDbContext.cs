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

