using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        // AuditLog uses LogId as its primary key (not the BaseEntity Id convention).
        builder.HasKey(a => a.LogId);
        builder.Property(a => a.LogId).ValueGeneratedOnAdd();

        builder.Property(a => a.Action)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.ResourceType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.ResourceId).IsRequired(false);
        builder.Property(a => a.IpAddress).IsRequired().HasMaxLength(45);   // IPv6 max = 39 chars
        builder.Property(a => a.UserAgent).IsRequired().HasMaxLength(500);

        // Indexes for the two most common query patterns: per-user audit trail and time-range scans.
        builder.HasIndex(a => a.UserId)
            .HasDatabaseName("ix_audit_logs_user_id");

        builder.HasIndex(a => a.Timestamp)
            .HasDatabaseName("ix_audit_logs_timestamp");

        // Restrict delete so that removing a user account does not erase their audit history.
        builder.HasOne(a => a.User)
            .WithMany(u => u.AuditLogs)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
