using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");

        // UserSession uses LogId as its PK — mirrors AuditLog pattern for append-only tables.
        builder.HasKey(s => s.LogId);
        builder.Property(s => s.LogId).ValueGeneratedOnAdd();

        builder.Property(s => s.SessionId).IsRequired();
        builder.Property(s => s.LoginAt).IsRequired();
        builder.Property(s => s.LogoutAt).IsRequired(false);

        // Store ExpirationReason as integer — compact, fast to query, no string-length concerns.
        builder.Property(s => s.ExpirationReason)
            .HasConversion<int?>()
            .IsRequired(false);

        // IPv6 max = 39 chars; 45 allows for IPv4-mapped-IPv6 notation.
        builder.Property(s => s.IpAddress).IsRequired().HasMaxLength(45);

        // 512 chars covers the majority of realistic user-agent strings.
        builder.Property(s => s.UserAgent).IsRequired().HasMaxLength(512);

        builder.Property(s => s.CreatedAt).IsRequired();

        // Index: per-user session history (most common audit query).
        builder.HasIndex(s => s.UserId)
            .HasDatabaseName("ix_user_sessions_user_id");

        // Index: Redis key correlation (cross-reference active session to history).
        builder.HasIndex(s => s.SessionId)
            .HasDatabaseName("ix_user_sessions_session_id");

        // Index: time-range audit queries (e.g. "all sessions in the last 30 days").
        builder.HasIndex(s => s.LoginAt)
            .HasDatabaseName("ix_user_sessions_login_at");

        // FK with RESTRICT: removing a user account must NOT cascade-delete session history
        // (HIPAA DR-016 — immutable audit records must be retained for 7 years).
        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
