using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="AiRequestLog"/> entity (US_071 TASK_001).
///
/// <para>Table: <c>ai_request_logs</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Uses <c>LogId</c> as the primary key (not <see cref="BaseEntity.Id"/>) because
///   this is an append-only audit log — same pattern as <see cref="AuditLog"/>.</item>
///   <item>All enum columns stored as <c>character varying(N)</c> strings via
///   <c>HasConversion&lt;string&gt;()</c> — human-readable without lookup joins.</item>
///   <item><c>EstimatedCost</c> uses <c>numeric(12,6)</c> — covers per-request costs
///   from sub-cent GPT-4o-mini calls up to edge-case long completions.</item>
///   <item>Index on <c>CreatedAt DESC</c> — primary access path for time-range aggregation
///   queries in the daily summary job.</item>
///   <item>Composite index on (<c>Provider</c>, <c>CreatedAt</c>) — accelerates
///   per-provider daily cost aggregation queries (AC-1).</item>
///   <item>Index on <c>CorrelationId</c> — enables cross-service trace lookups (TR-028).</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class AiRequestLogConfiguration : IEntityTypeConfiguration<AiRequestLog>
{
    public void Configure(EntityTypeBuilder<AiRequestLog> builder)
    {
        builder.ToTable("ai_request_logs");

        // AiRequestLog uses LogId as PK (append-only, no UpdatedAt needed).
        builder.HasKey(l => l.LogId);
        builder.Property(l => l.LogId).ValueGeneratedOnAdd();

        // ── Enum columns (stored as strings — human-readable, no join overhead) ─
        builder.Property(l => l.Provider)
            .HasConversion<string>()
            .HasMaxLength(20)     // "Anthropic" = 9 chars; 20 provides headroom
            .IsRequired();

        builder.Property(l => l.RequestType)
            .HasConversion<string>()
            .HasMaxLength(30)     // "ConversationalIntake" = 20 chars; 30 provides headroom
            .IsRequired();

        builder.Property(l => l.CostSource)
            .HasConversion<string>()
            .HasMaxLength(15)     // "Approximate" = 11 chars; 15 provides headroom
            .IsRequired();

        // ── Token counts ──────────────────────────────────────────────────────
        builder.Property(l => l.InputTokens)
            .IsRequired();

        builder.Property(l => l.OutputTokens)
            .IsRequired();

        // ── Cost column — numeric(12,6) covers sub-cent to $999,999/request ──
        builder.Property(l => l.EstimatedCost)
            .HasColumnType("numeric(12,6)")
            .IsRequired();

        // ── Correlation ID — UUID for cross-service tracing (TR-028) ─────────
        builder.Property(l => l.CorrelationId)
            .HasColumnType("uuid")
            .IsRequired();

        // ── Timestamp — immutable after insert ────────────────────────────────
        builder.Property(l => l.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        // ── Indexes ───────────────────────────────────────────────────────────

        // Primary access path: time-range scans by the daily aggregation job.
        // DESC order matches ORDER BY CreatedAt DESC used by most queries.
        builder.HasIndex(l => l.CreatedAt)
            .HasDatabaseName("ix_ai_request_logs_created_at")
            .IsDescending();

        // Per-provider aggregation: WHERE provider = @p AND created_at >= @start
        builder.HasIndex(l => new { l.Provider, l.CreatedAt })
            .HasDatabaseName("ix_ai_request_logs_provider_created_at");

        // Cross-service trace lookup: WHERE correlation_id = @id
        builder.HasIndex(l => l.CorrelationId)
            .HasDatabaseName("ix_ai_request_logs_correlation_id");
    }
}
