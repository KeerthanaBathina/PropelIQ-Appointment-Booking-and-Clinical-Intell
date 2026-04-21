namespace UPACIP.Api.Authorization;

/// <summary>
/// Compile-time string constants for all named authorization policies.
/// Reference these constants instead of raw string literals in
/// <c>[Authorize(Policy = ...)]</c> attributes and <c>policy.RequireRole()</c> calls
/// to eliminate typo-induced security gaps.
/// </summary>
public static class RbacPolicies
{
    /// <summary>Only the <c>Patient</c> role may access this endpoint.</summary>
    public const string PatientOnly = "PatientOnly";

    /// <summary>Only the <c>Staff</c> role may access this endpoint.</summary>
    public const string StaffOnly = "StaffOnly";

    /// <summary>Only the <c>Admin</c> role may access this endpoint.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Either <c>Staff</c> or <c>Admin</c> role is permitted.</summary>
    public const string StaffOrAdmin = "StaffOrAdmin";

    /// <summary>Any authenticated user, regardless of role.</summary>
    public const string AnyAuthenticated = "AnyAuthenticated";
}
