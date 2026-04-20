using Microsoft.AspNetCore.Identity;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Platform user extending ASP.NET Core Identity with UPACIP-specific profile fields.
/// Guid PK ensures IDs are non-enumerable (security — prevents ID-enumeration attacks).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Patient/staff full display name.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Optional date of birth used for patient eligibility checks.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>UTC timestamp when the user record was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp of the last profile update.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Soft-delete sentinel (DR-021). Null = active; non-null = logically deleted.
    /// Hard deletes are forbidden to preserve audit trails.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    /// <summary>Documents uploaded by this user.</summary>
    public ICollection<ClinicalDocument> UploadedDocuments { get; set; } = [];

    /// <summary>Audit trail entries attributed to this user.</summary>
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
}
