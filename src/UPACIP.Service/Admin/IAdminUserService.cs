namespace UPACIP.Service.Admin;

/// <summary>
/// Contract for admin user management operations (US_058 AC-3, US_061 AC-1 – AC-4).
///
/// Wraps ASP.NET Core Identity's UserManager with UPACIP-specific business rules:
/// status changes are soft-deactivations (FR-088 — historical data preserved),
/// and every action is audit-logged (NFR-012).
/// </summary>
public interface IAdminUserService
{
    /// <summary>Returns all staff and admin accounts with status, last login, and role (AC-3).</summary>
    Task<AdminUsersResponseDto> GetAllUsersAsync(CancellationToken ct = default);

    /// <summary>Creates a new staff/admin user account with the given role (invite flow).</summary>
    Task<AdminUserDto> InviteUserAsync(InviteUserRequestDto dto, Guid adminUserId, CancellationToken ct = default);

    /// <summary>Sets the account status (Active → Inactive or vice-versa). Preserves all history (FR-088).</summary>
    Task<AdminUserDto> SetUserStatusAsync(string userId, string status, Guid adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Soft-deactivates a staff/admin account (US_061 AC-3, FR-088).
    /// Enforces:
    ///   EC-1 — target must not be the requesting admin (self-deactivation guard).
    ///   EC-2 — at least one active admin must remain after deactivation.
    /// Throws <see cref="InvalidOperationException"/> for business rule violations.
    /// Throws <see cref="KeyNotFoundException"/> when the target user is not found.
    /// </summary>
    Task<AdminUserDto> DeactivateStaffAsync(string targetUserId, Guid adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Restores a deactivated staff/admin account (US_061 AC-4, FR-088).
    /// Previous role and permissions are preserved — no changes to role membership.
    /// Throws <see cref="KeyNotFoundException"/> when the target user is not found.
    /// Throws <see cref="InvalidOperationException"/> when the account is not currently deactivated.
    /// </summary>
    Task<AdminUserDto> ReactivateStaffAsync(string targetUserId, Guid adminUserId, CancellationToken ct = default);
}
