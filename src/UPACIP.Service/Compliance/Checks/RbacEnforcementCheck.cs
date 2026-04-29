using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Compliance.Checks;

/// <summary>
/// Verifies Role-Based Access Control (RBAC) enforcement (US_093, AC-1, NFR-011, §164.312(a)(1)).
///
/// Checks:
///   (a) Expected roles (Patient, Staff, Admin) exist in ASP.NET Core Identity.
///   (b) No user simultaneously holds Patient and Admin roles (conflicting roles).
///   (c) Controller actions are protected with [Authorize] attributes.
///   (d) Admin-only endpoints (path contains /admin/) require the Admin role.
/// </summary>
public sealed class RbacEnforcementCheck : IComplianceCheck
{
    private readonly ApplicationDbContext                    _db;
    private readonly RoleManager<ApplicationRole>            _roleManager;
    private readonly IActionDescriptorCollectionProvider     _actionDescriptors;
    private readonly ILogger<RbacEnforcementCheck>           _logger;

    private static readonly string[] RequiredRoles = { "Patient", "Staff", "Admin" };

    public string ControlName     => "Role-Based Access Control (RBAC)";
    public string ControlCategory => "Technical";
    public string HipaaReference  => "§164.312(a)(1) — Access Control";

    public RbacEnforcementCheck(
        ApplicationDbContext                db,
        RoleManager<ApplicationRole>        roleManager,
        IActionDescriptorCollectionProvider actionDescriptors,
        ILogger<RbacEnforcementCheck>       logger)
    {
        _db                = db;
        _roleManager       = roleManager;
        _actionDescriptors = actionDescriptors;
        _logger            = logger;
    }

    public async Task<ComplianceCheckResult> ExecuteAsync(CancellationToken ct)
    {
        var details = new Dictionary<string, string>();

        try
        {
            // (a) Verify required roles exist
            var missingRoles = new List<string>();
            foreach (var roleName in RequiredRoles)
            {
                var exists = await _roleManager.RoleExistsAsync(roleName);
                details[$"Role_{roleName}"] = exists ? "exists" : "missing";
                if (!exists)
                    missingRoles.Add(roleName);
            }

            if (missingRoles.Count > 0)
            {
                return Fail(
                    $"Required roles are missing from Identity: {string.Join(", ", missingRoles)}",
                    details);
            }

            // (b) Verify no user holds both Patient and Admin roles (conflicting roles)
            var conflictingUserCount = await CountConflictingUsersAsync(ct);
            details["ConflictingRoleUsers"] = conflictingUserCount.ToString();

            if (conflictingUserCount > 0)
            {
                return Fail(
                    $"{conflictingUserCount} user(s) hold both Patient and Admin roles simultaneously",
                    details);
            }

            // (c-d) Enumerate controller actions and check authorization metadata
            var (totalEndpoints, protectedEndpoints, unprotectedAdminEndpoints) =
                InspectEndpointAuthorization();

            details["TotalEndpoints"]            = totalEndpoints.ToString();
            details["ProtectedEndpoints"]        = protectedEndpoints.ToString();
            details["UnprotectedAdminEndpoints"] = unprotectedAdminEndpoints.ToString();

            if (unprotectedAdminEndpoints > 0)
            {
                return Fail(
                    $"{unprotectedAdminEndpoints} admin endpoint(s) (path /admin/) are missing Admin role requirement",
                    details);
            }

            return Pass(
                $"RBAC verified: {RequiredRoles.Length} roles configured, {protectedEndpoints}/{totalEndpoints} endpoints protected",
                details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RbacEnforcementCheck: unexpected error during verification.");
            return Fail($"Verification error: {ex.Message}", details);
        }
    }

    private async Task<int> CountConflictingUsersAsync(CancellationToken ct)
    {
        // Find users who are in both Patient and Admin roles
        var patientRole = await _roleManager.FindByNameAsync("Patient");
        var adminRole   = await _roleManager.FindByNameAsync("Admin");

        if (patientRole is null || adminRole is null)
            return 0;

        var patientUserIds = _db.UserRoles
            .Where(ur => ur.RoleId == patientRole.Id)
            .Select(ur => ur.UserId);

        var conflictCount = await _db.UserRoles
            .Where(ur => ur.RoleId == adminRole.Id && patientUserIds.Contains(ur.UserId))
            .CountAsync(ct);

        return conflictCount;
    }

    private (int total, int protected_, int unprotectedAdmin) InspectEndpointAuthorization()
    {
        var actions = _actionDescriptors.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .ToList();

        int total                = actions.Count;
        int protectedEndpoints   = 0;
        int unprotectedAdmin     = 0;

        foreach (var action in actions)
        {
            var hasAuthorize = action.EndpointMetadata
                .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                .Any();

            var allowAnonymous = action.EndpointMetadata
                .OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>()
                .Any();

            // Determine the route template to identify /admin/ paths
            var routeTemplate = action.AttributeRouteInfo?.Template ?? string.Empty;
            var isAdminPath   = routeTemplate.Contains("/admin/", StringComparison.OrdinalIgnoreCase)
                             || routeTemplate.StartsWith("admin/", StringComparison.OrdinalIgnoreCase);

            if (hasAuthorize && !allowAnonymous)
            {
                protectedEndpoints++;

                // (d) Admin path must specifically require Admin role
                if (isAdminPath)
                {
                    var requiresAdminRole = action.EndpointMetadata
                        .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>()
                        .Any(a =>
                            (a.Roles?.Contains("Admin", StringComparison.OrdinalIgnoreCase) ?? false)
                            || (a.Policy?.Contains("Admin", StringComparison.OrdinalIgnoreCase) ?? false));

                    if (!requiresAdminRole)
                    {
                        _logger.LogWarning(
                            "RbacEnforcementCheck: admin endpoint '{Route}' does not require Admin role.",
                            routeTemplate);
                        unprotectedAdmin++;
                    }
                }
            }
            else if (!allowAnonymous && isAdminPath)
            {
                // Admin path with no authorization at all
                _logger.LogWarning(
                    "RbacEnforcementCheck: admin endpoint '{Route}' has no [Authorize] attribute.",
                    routeTemplate);
                unprotectedAdmin++;
            }
        }

        return (total, protectedEndpoints, unprotectedAdmin);
    }

    private ComplianceCheckResult Pass(string evidence, Dictionary<string, string> details) =>
        new()
        {
            Passed         = true,
            ControlName    = ControlName,
            HipaaReference = HipaaReference,
            Evidence       = evidence,
            VerifiedAtUtc  = DateTime.UtcNow,
            Details        = details
        };

    private ComplianceCheckResult Fail(string reason, Dictionary<string, string> details) =>
        new()
        {
            Passed         = false,
            ControlName    = ControlName,
            HipaaReference = HipaaReference,
            Evidence       = $"RBAC verification FAILED: {reason}",
            FailureReason  = reason,
            VerifiedAtUtc  = DateTime.UtcNow,
            Details        = details
        };
}
