using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Seeding;

/// <summary>
/// EF Core entity configuration that seeds the three default ASP.NET Core Identity roles
/// via <c>HasData()</c>. These roles are applied by EF Core migrations and are therefore
/// present in ALL environments (Development, Staging, Production) as soon as migrations run.
///
/// Deterministic GUIDs match those used by <c>scripts/seed-data.sql</c> so that
/// both the migration-based role seed and the development SQL seed remain consistent.
///
/// This class is auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="ApplicationDbContext.OnModelCreating"/>.
/// </summary>
internal sealed class RoleSeedConfiguration : IEntityTypeConfiguration<ApplicationRole>
{
    // Stable, deterministic GUIDs that match the SQL seed script and the existing
    // AddIdentitySchema migration — never change these values.
    internal static readonly Guid PatientRoleId = new("a1b2c3d4-e5f6-7a8b-9c0d-e1f2a3b4c5d6");
    internal static readonly Guid StaffRoleId   = new("b2c3d4e5-f6a7-8b9c-0d1e-f2a3b4c5d6e7");
    internal static readonly Guid AdminRoleId   = new("c3d4e5f6-a7b8-9c0d-1e2f-a3b4c5d6e7f8");

    public void Configure(EntityTypeBuilder<ApplicationRole> builder)
    {
        builder.HasData(
            new ApplicationRole
            {
                Id               = PatientRoleId,
                Name             = "Patient",
                NormalizedName   = "PATIENT",
                ConcurrencyStamp = PatientRoleId.ToString(),
                Description      = "End-user patient portal access — read-only appointment and record visibility."
            },
            new ApplicationRole
            {
                Id               = StaffRoleId,
                Name             = "Staff",
                NormalizedName   = "STAFF",
                ConcurrencyStamp = StaffRoleId.ToString(),
                Description      = "Clinical staff — schedule management, appointment actions, and patient record access."
            },
            new ApplicationRole
            {
                Id               = AdminRoleId,
                Name             = "Admin",
                NormalizedName   = "ADMIN",
                ConcurrencyStamp = AdminRoleId.ToString(),
                Description      = "Platform administrator — full system access including user management and configuration."
            });
    }
}
