using Microsoft.AspNetCore.Identity;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Platform role extending ASP.NET Core Identity.
/// Guid PK aligns with <see cref="ApplicationUser"/> and prevents enumerable IDs.
/// The three seeded roles are: Patient, Staff, Admin.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>Human-readable description of the role's access scope.</summary>
    public string? Description { get; set; }
}
