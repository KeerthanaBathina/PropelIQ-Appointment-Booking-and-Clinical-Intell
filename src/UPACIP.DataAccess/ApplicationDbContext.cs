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
    public DbSet<MedicalCode>      MedicalCodes      => Set<MedicalCode>();
    public DbSet<AuditLog>         AuditLogs         => Set<AuditLog>();
    public DbSet<QueueEntry>       QueueEntries      => Set<QueueEntry>();
    public DbSet<NotificationLog>  NotificationLogs  => Set<NotificationLog>();

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

