using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace UPACIP.Api.Claims;

/// <summary>
/// Normalizes the role claim so that ASP.NET Core role checks work regardless of whether
/// the JWT was issued with the short form (<c>role</c>) or the long-form
/// (<c>http://schemas.microsoft.com/ws/2008/06/identity/claims/role</c>) claim name.
///
/// UPACIP's <see cref="UPACIP.Service.Auth.TokenService"/> always issues the long-form
/// <c>ClaimTypes.Role</c>.  This transformer handles tokens from external identity providers
/// (e.g., Azure AD B2C, OAuth) that may use only the short-form <c>role</c> claim.
///
/// Runs after successful authentication on every request.  Kept lightweight (no DB call)
/// to avoid per-request database overhead.
/// </summary>
public sealed class RoleClaimsTransformer : IClaimsTransformation
{
    private const string ShortRoleClaim = "role";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // If ClaimTypes.Role already present, nothing to do.
        if (principal.HasClaim(c => c.Type == ClaimTypes.Role))
            return Task.FromResult(principal);

        // Look for short-form "role" claim and promote it to ClaimTypes.Role.
        var shortRoleClaims = principal.Claims
            .Where(c => c.Type == ShortRoleClaim)
            .ToList();

        if (shortRoleClaims.Count == 0)
            return Task.FromResult(principal);

        // Clone the identity so we don't mutate the original (IClaimsTransformation contract).
        var identity = (ClaimsIdentity?)principal.Identity;
        if (identity is null)
            return Task.FromResult(principal);

        var cloned = new ClaimsIdentity(
            identity.Claims,
            identity.AuthenticationType,
            identity.NameClaimType,
            ClaimTypes.Role);

        foreach (var roleClaim in shortRoleClaims)
            cloned.AddClaim(new Claim(ClaimTypes.Role, roleClaim.Value, roleClaim.ValueType));

        return Task.FromResult(new ClaimsPrincipal(cloned));
    }
}
