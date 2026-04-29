using Microsoft.EntityFrameworkCore;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess;

/// <summary>
/// Read-optimized DbContext for CQRS audit log queries (US_096, AC-3, TR-013).
///
/// Key read-side optimizations:
/// <list type="bullet">
///   <item><c>QueryTrackingBehavior.NoTracking</c> globally — eliminates change-tracker
///     overhead for all queries; no entity state is ever tracked in this context.</item>
///   <item>No navigation properties — avoids lazy loading and forces explicit projections,
///     keeping SQL predicates minimal.</item>
///   <item>Index hints declared for common query patterns (timestamp range, entity lookup,
///     UserId lookup) — these match the indexes created by migration
///     <c>AddAuditLogPartitioningAndImmutability</c>.</item>
///   <item>Shares the same underlying <c>audit_logs</c> PostgreSQL table as
///     <c>ApplicationDbContext</c> — single source of truth with no data replication.</item>
/// </list>
///
/// Registered with <c>Scoped</c> lifetime — one instance per HTTP request, aligned with
/// query service lifetime.
/// </summary>
public sealed class AuditLogReadDbContext : DbContext
{
    public AuditLogReadDbContext(DbContextOptions<AuditLogReadDbContext> options)
        : base(options)
    {
    }

    /// <summary>Read-only access to audit log entries.</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Global NoTracking — all queries from this context skip the change tracker.
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(e => e.LogId);

            // Ignore the User navigation property — the read context does not
            // include ApplicationUser to keep the model minimal and avoid
            // auto-discovery of all Identity entities.
            entity.Ignore(e => e.User);

            // Declare indexes that exist on the physical table.
            // These declarations inform the EF Core query planner but do NOT
            // create new indexes (no migrations run for this read context).
            entity.HasIndex(e => e.Timestamp)
                .IsDescending()
                .HasDatabaseName("ix_audit_logs_timestamp");

            entity.HasIndex(e => new { e.ResourceType, e.ResourceId })
                .HasDatabaseName("IX_AuditLogs_Entity");

            entity.HasIndex(e => new { e.UserId, e.Timestamp })
                .HasDatabaseName("ix_audit_logs_security_events");
        });
    }
}
