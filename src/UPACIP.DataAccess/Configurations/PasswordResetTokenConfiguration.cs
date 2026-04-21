using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// Fluent API configuration for <see cref="PasswordResetToken"/> (US_015, FR-005).
///
/// Configures:
///   - Primary key, column types, and max lengths
///   - Index on <see cref="PasswordResetToken.TokenHash"/> for fast O(log n) lookup
///   - Index on <see cref="PasswordResetToken.UserId"/> for per-user token queries
///     (needed for bulk-invalidation of prior tokens)
///   - Index on <see cref="PasswordResetToken.ExpiresAt"/> for periodic cleanup queries
///     (background job deletes tokens where expires_at &lt; NOW() - 7 days)
///   - Foreign key to <see cref="ApplicationUser"/> with CASCADE DELETE
/// </summary>
public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        // SHA-256 hex digest = 64 chars (lowercase).
        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(t => t.ExpiresAt)
            .IsRequired();

        builder.Property(t => t.IsUsed)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.InvalidatedAt)
            .IsRequired(false);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        // Index on TokenHash: primary lookup path when validating a reset link.
        builder.HasIndex(t => t.TokenHash)
            .HasDatabaseName("ix_password_reset_tokens_token_hash");

        // Index on UserId: needed to bulk-invalidate prior tokens on a new request.
        builder.HasIndex(t => t.UserId)
            .HasDatabaseName("ix_password_reset_tokens_user_id");

        // Index on ExpiresAt: supports periodic cleanup queries that delete expired token rows
        // (recommended: IHostedService job running daily, deleting where expires_at < NOW() - 7d).
        builder.HasIndex(t => t.ExpiresAt)
            .HasDatabaseName("ix_password_reset_tokens_expires_at");

        // FK: cascade delete ensures tokens are purged when the user account is removed.
        builder.HasOne(t => t.User)
            .WithMany(u => u.PasswordResetTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
