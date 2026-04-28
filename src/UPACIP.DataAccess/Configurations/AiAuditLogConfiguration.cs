using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="AiAuditLogEntity"/>
/// (US_080 task_002, AC-3, AC-4, AIR-S04).
///
/// <para><b>Table:</b> <c>ai_audit_logs</c> (PostgreSQL range-partitioned by <c>created_at</c>).</para>
///
/// <para><b>Index strategy:</b></para>
/// <list type="bullet">
///   <item><c>created_at</c> — primary partition range scan; most queries are date-bounded.</item>
///   <item><c>model_version</c> — filter by provider model string.</item>
///   <item><c>request_type</c> — filter by operation category.</item>
///   <item><c>confidence_score</c> — range filter for quality analysis.</item>
///   <item>Composite <c>(patient_id, created_at)</c> — HIPAA compliance patient lookup.</item>
/// </list>
///
/// <para>
/// <c>Prompt</c> and <c>Response</c> are mapped to PostgreSQL <c>text</c> (unbounded length).
/// </para>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public sealed class AiAuditLogConfiguration : IEntityTypeConfiguration<AiAuditLogEntity>
{
    public void Configure(EntityTypeBuilder<AiAuditLogEntity> builder)
    {
        builder.ToTable("ai_audit_logs");

        // ── Primary key ───────────────────────────────────────────────────────
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever(); // App-assigned GUID.

        // ── Timestamps ────────────────────────────────────────────────────────
        // timestamptz ensures UTC storage and correct comparison with partition bounds.
        builder.Property(e => e.CreatedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        // ── Text columns — unbounded (TEXT), post-PII-redaction ───────────────
        builder.Property(e => e.Prompt)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(e => e.Response)
            .HasColumnType("text")
            .IsRequired();

        // ── Bounded string columns ────────────────────────────────────────────
        builder.Property(e => e.ModelVersion)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.RequestType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.UserId)
            .HasMaxLength(450) // Matches ASP.NET Core Identity max user ID length.
            .IsRequired();

        builder.Property(e => e.AbVariant)
            .HasMaxLength(20)
            .IsRequired(false);

        // ── Numeric columns ───────────────────────────────────────────────────
        builder.Property(e => e.InputTokens).IsRequired();
        builder.Property(e => e.OutputTokens).IsRequired();
        builder.Property(e => e.TotalTokens).IsRequired();
        builder.Property(e => e.LatencyMs).IsRequired();
        builder.Property(e => e.ConfidenceScore).IsRequired(false);

        // ── Nullable FK-style columns (no navigation properties) ─────────────
        builder.Property(e => e.PatientId).IsRequired(false);
        builder.Property(e => e.AbExperimentId).IsRequired(false);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Primary time-range scan: most admin queries are bounded by date.
        builder.HasIndex(e => e.CreatedAt)
            .HasDatabaseName("ix_ai_audit_logs_created_at");

        // Model version filter (AC-4).
        builder.HasIndex(e => e.ModelVersion)
            .HasDatabaseName("ix_ai_audit_logs_model_version");

        // Request type filter (AC-4).
        builder.HasIndex(e => e.RequestType)
            .HasDatabaseName("ix_ai_audit_logs_request_type");

        // Confidence score range filter (AC-4).
        builder.HasIndex(e => e.ConfidenceScore)
            .HasDatabaseName("ix_ai_audit_logs_confidence_score");

        // Patient-correlated compliance lookup (AIR-S04) — ordered by time descending
        // within each patient so recent interactions surface first.
        builder.HasIndex(e => new { e.PatientId, e.CreatedAt })
            .HasDatabaseName("ix_ai_audit_logs_patient_created");
    }
}
