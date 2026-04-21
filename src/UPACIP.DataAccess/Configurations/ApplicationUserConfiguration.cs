using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// Fluent API configuration for <see cref="ApplicationUser"/> (ASP.NET Core Identity extension).
///
/// Configures:
///   - Column lengths and nullability for new registration fields
///   - <see cref="AccountStatus"/> stored as string for human-readable PostgreSQL column values
///   - Soft-delete global query filter (DeletedAt IS NULL) — DR-021
///   - Navigation to <see cref="EmailVerificationToken"/> collection
///
/// NOTE: The ASP.NET Core Identity unique index on NormalizedEmail already covers the
/// email uniqueness requirement (DR-001). No additional index is added here.
/// </summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // ---------- New registration fields (US_012) ----------
        builder.Property(u => u.FirstName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.LastName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.FullName)
            .IsRequired()
            .HasMaxLength(200);

        // AccountStatus stored as string for readability in PostgreSQL (e.g. "Active").
        // Default value set via HasDefaultValue so existing rows get PendingVerification.
        builder.Property(u => u.AccountStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(DataAccess.Enums.AccountStatus.PendingVerification);

        // DateOfBirth is nullable — not all staff users supply a DOB.
        builder.Property(u => u.DateOfBirth)
            .IsRequired(false);

        // Soft-delete global query filter — excludes logically deleted users from all queries
        // unless .IgnoreQueryFilters() is explicitly called (admin dashboards, audit views).
        builder.HasQueryFilter(u => u.DeletedAt == null);

        // ---------- MFA columns (US_016) ----------
        builder.Property(u => u.TotpSecretEncrypted)
            .IsRequired(false)
            .HasMaxLength(512);

        builder.Property(u => u.MfaRecoveryCodes)
            .IsRequired(false)
            .HasMaxLength(2048);

        builder.Property(u => u.MfaEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        // ---------- Login tracking columns (US_016 AC-4) ----------
        builder.Property(u => u.LastLoginAt)
            .IsRequired(false);

        builder.Property(u => u.LastLoginIp)
            .IsRequired(false)
            .HasMaxLength(45); // IPv6 max = 39 chars; 45 allows for IPv4-mapped IPv6

        // ---------- Navigation: User → EmailVerificationTokens ----------
        builder.HasMany(u => u.EmailVerificationTokens)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
