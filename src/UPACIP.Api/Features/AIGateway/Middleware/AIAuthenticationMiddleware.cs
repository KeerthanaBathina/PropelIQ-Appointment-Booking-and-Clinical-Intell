using System.Security.Claims;

namespace UPACIP.Api.Features.AIGateway.Middleware;

/// <summary>
/// Validates the <see cref="ClaimsPrincipal"/> attached to an AI Gateway request,
/// enforcing that only authenticated Staff or Admin users may invoke AI operations
/// (US_067 AC-3, AC-1 RBAC).
///
/// Authentication is already performed by ASP.NET Core's
/// <c>UseAuthentication()</c> / JWT Bearer middleware. This component applies
/// the additional <em>authorization</em> check specific to the AI Gateway: only
/// the Staff and Admin roles are permitted; Patient-role tokens are rejected.
///
/// Returns an <see cref="AIAuthenticationResult"/> so the caller decides how to
/// surface the failure — HTTP concerns are kept out of this class.
/// </summary>
public sealed class AIAuthenticationMiddleware
{
    private static readonly string[] AllowedRoles =
        [RbacRoles.Staff, RbacRoles.Admin];

    /// <summary>
    /// Verifies that <paramref name="principal"/> is authenticated and holds
    /// one of the Staff or Admin roles.
    /// </summary>
    public AIAuthenticationResult Authorize(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return AIAuthenticationResult.Fail("Authentication is required to invoke AI Gateway operations.");

        var hasAllowedRole = AllowedRoles.Any(role => principal.IsInRole(role));
        if (!hasAllowedRole)
            return AIAuthenticationResult.Fail(
                "Insufficient permissions. AI Gateway operations require Staff or Admin role.");

        return AIAuthenticationResult.Ok();
    }
}

/// <summary>Result of <see cref="AIAuthenticationMiddleware.Authorize"/>.</summary>
public sealed record AIAuthenticationResult(bool IsAuthorized, string? ErrorMessage)
{
    internal static AIAuthenticationResult Ok() => new(true, null);
    internal static AIAuthenticationResult Fail(string errorMessage) => new(false, errorMessage);
}

/// <summary>
/// Role name constants used by the AI Gateway authorization middleware.
/// Mirrors the role strings registered in <c>Program.cs</c> RBAC policies.
/// </summary>
internal static class RbacRoles
{
    internal const string Staff = "Staff";
    internal const string Admin = "Admin";
}
