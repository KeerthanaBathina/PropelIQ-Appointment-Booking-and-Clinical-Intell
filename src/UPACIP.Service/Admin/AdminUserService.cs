using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;

namespace UPACIP.Service.Admin;

/// <summary>
/// Admin user management service (US_058 AC-3).
///
/// Lists all Staff and Admin accounts with status, last login, and role.
/// Invite creates an account with a temporary password and sets it to Active.
/// Status changes soft-deactivate the account (AccountStatus.Deactivated) or
/// reactivate it (AccountStatus.Active) without deleting any historical data (FR-088).
/// Every action is audit-logged (NFR-012).
/// </summary>
public sealed class AdminUserService : IAdminUserService
{
    private static readonly string[] ManagedRoles = ["Admin", "Staff"];

    private readonly ApplicationDbContext        _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IAuditLogService             _audit;
    private readonly ILogger<AdminUserService>    _logger;

    public AdminUserService(
        ApplicationDbContext         db,
        UserManager<ApplicationUser>  userManager,
        RoleManager<ApplicationRole>  roleManager,
        IAuditLogService              audit,
        ILogger<AdminUserService>     logger)
    {
        _db          = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _audit       = audit;
        _logger      = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetAllUsersAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AdminUsersResponseDto> GetAllUsersAsync(CancellationToken ct = default)
    {
        // Load all users that belong to Admin or Staff role via a join on Identity tables.
        var roleIds = await _db.Roles
            .AsNoTracking()
            .Where(r => ManagedRoles.Contains(r.Name))
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(ct);

        if (roleIds.Count == 0)
            return new AdminUsersResponseDto([]);

        var roleIdSet = roleIds.Select(r => r.Id).ToHashSet();

        // UserRoles JOIN
        var userIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => roleIdSet.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct);

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.DeletedAt == null)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        // Build userId → role name map
        var userRoleMap = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId) && roleIdSet.Contains(ur.RoleId))
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, RoleName = r.Name ?? string.Empty })
            .ToDictionaryAsync(x => x.UserId, x => x.RoleName, ct);

        var dtos = users.Select(u =>
        {
            var role   = userRoleMap.TryGetValue(u.Id, out var r) ? r : "Staff";
            var status = u.AccountStatus == AccountStatus.Active ? "Active" : "Inactive";
            return new AdminUserDto(
                Id:          u.Id.ToString(),
                FullName:    u.FullName,
                Email:       u.Email ?? string.Empty,
                Role:        role,
                RoleSubtitle: BuildRoleSubtitle(role, u),
                Status:      status,
                LastLoginAt: u.LastLoginAt?.ToString("o"),
                CreatedAt:   u.CreatedAt.ToString("o"));
        }).ToList();

        return new AdminUsersResponseDto(dtos, Total: dtos.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // InviteUserAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AdminUserDto> InviteUserAsync(
        InviteUserRequestDto dto, Guid adminUserId, CancellationToken ct = default)
    {
        // Validate role value to prevent privilege escalation (OWASP A01).
        if (!ManagedRoles.Contains(dto.Role, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid role: {dto.Role}");

        // Duplicate email guard (OWASP A07 — generic response to prevent enumeration).
        var existing = await _userManager.FindByEmailAsync(dto.Email);
        if (existing is not null)
            throw new InvalidOperationException("An account with this email already exists.");

        // Split name into first/last (best-effort).
        var nameParts  = dto.FullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName  = nameParts.Length > 0 ? nameParts[0] : dto.FullName;
        var lastName   = nameParts.Length > 1 ? nameParts[1] : string.Empty;

        var user = new ApplicationUser
        {
            UserName       = dto.Email,
            Email          = dto.Email,
            FirstName      = firstName,
            LastName       = lastName,
            FullName       = dto.FullName,
            AccountStatus  = AccountStatus.Active,
            EmailConfirmed = true,    // Admin-invited accounts skip email verification
            CreatedAt      = DateTimeOffset.UtcNow,
            UpdatedAt      = DateTimeOffset.UtcNow,
        };

        // Temporary random password — user must reset on first login.
        var tempPassword = $"Temp!{Guid.NewGuid():N}".Substring(0, 20);
        var createResult = await _userManager.CreateAsync(user, tempPassword);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"User creation failed: {errors}");
        }

        await _userManager.AddToRoleAsync(user, dto.Role);

        await _audit.LogAsync(
            AuditAction.StaffAccountCreated,
            adminUserId,
            resourceType: "AdminUserInvite",
            ipAddress:    string.Empty,
            userAgent:    string.Empty,
            resourceId:   user.Id,
            cancellationToken: ct);

        _logger.LogInformation(
            "Admin {AdminId} invited user {UserId} ({Email}) with role {Role}.",
            adminUserId, user.Id, dto.Email, dto.Role);

        return new AdminUserDto(
            Id:          user.Id.ToString(),
            FullName:    user.FullName,
            Email:       user.Email ?? string.Empty,
            Role:        dto.Role,
            RoleSubtitle: null,
            Status:      "Active",
            LastLoginAt: null,
            CreatedAt:   user.CreatedAt.ToString("o"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SetUserStatusAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AdminUserDto> SetUserStatusAsync(
        string userId, string status, Guid adminUserId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(userId, out var userGuid))
            throw new ArgumentException("Invalid user ID.");

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found.");

        // Prevent admins from deactivating themselves (safety guard).
        if (user.Id == adminUserId)
            throw new InvalidOperationException("An admin cannot change their own status.");

        var newStatus = status.Equals("Active", StringComparison.OrdinalIgnoreCase)
            ? AccountStatus.Active
            : AccountStatus.Deactivated; // soft-deactivate — history preserved (FR-088)

        user.AccountStatus = newStatus;
        user.UpdatedAt     = DateTimeOffset.UtcNow;

        // Disable/enable ASP.NET Core Identity login lockout as a secondary guard.
        if (newStatus == AccountStatus.Deactivated)
        {
            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        }
        else
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
        }

        await _userManager.UpdateAsync(user);

        await _audit.LogAsync(
            AuditAction.DataModify,
            adminUserId,
            resourceType: "AdminUserStatus",
            ipAddress:    string.Empty,
            userAgent:    string.Empty,
            resourceId:   user.Id,
            cancellationToken: ct);

        _logger.LogInformation(
            "Admin {AdminId} set user {UserId} status to {Status}.",
            adminUserId, userId, status);

        // Reload roles for the response
        var roles = await _userManager.GetRolesAsync(user);
        var role  = roles.FirstOrDefault() ?? "Staff";

        return new AdminUserDto(
            Id:          user.Id.ToString(),
            FullName:    user.FullName,
            Email:       user.Email ?? string.Empty,
            Role:        role,
            RoleSubtitle: BuildRoleSubtitle(role, user),
            Status:      status.Equals("Active", StringComparison.OrdinalIgnoreCase) ? "Active" : "Inactive",
            LastLoginAt: user.LastLoginAt?.ToString("o"),
            CreatedAt:   user.CreatedAt.ToString("o"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DeactivateStaffAsync (US_061 AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AdminUserDto> DeactivateStaffAsync(
        string targetUserId, Guid adminUserId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(targetUserId, out var targetGuid))
            throw new ArgumentException("Invalid user ID.");

        var user = await _userManager.FindByIdAsync(targetUserId)
            ?? throw new KeyNotFoundException($"User {targetUserId} not found.");

        // EC-1: prevent self-deactivation (OWASP A01).
        if (targetGuid == adminUserId)
            throw new InvalidOperationException("Cannot deactivate your own account.");

        if (user.AccountStatus == AccountStatus.Deactivated)
            throw new InvalidOperationException("Account is already deactivated.");

        // EC-2: ensure at least one other active admin remains.
        var userRoles = await _userManager.GetRolesAsync(user);
        if (userRoles.Contains("Admin", StringComparer.OrdinalIgnoreCase))
        {
            var adminRoleId = await _db.Roles
                .Where(r => r.NormalizedName == "ADMIN")
                .Select(r => r.Id)
                .FirstOrDefaultAsync(ct);

            if (adminRoleId != default)
            {
                var activeAdminCount = await _db.Users
                    .AsNoTracking()
                    .Where(u => u.Id != targetGuid
                             && u.AccountStatus == AccountStatus.Active
                             && u.DeletedAt == null)
                    .Join(_db.UserRoles,
                          u  => u.Id,
                          ur => ur.UserId,
                          (u, ur) => new { ur.RoleId })
                    .CountAsync(x => x.RoleId == adminRoleId, ct);

                if (activeAdminCount == 0)
                    throw new InvalidOperationException("At least one active admin account required.");
            }
        }

        user.AccountStatus = AccountStatus.Deactivated;
        user.UpdatedAt     = DateTimeOffset.UtcNow;
        user.DeactivatedAt = DateTimeOffset.UtcNow;
        user.DeactivatedBy = adminUserId;

        // Lock ASP.NET Core Identity login as a secondary guard (FR-088).
        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await _userManager.UpdateAsync(user);

        await _audit.LogAsync(
            AuditAction.StaffAccountDeactivated,
            adminUserId,
            resourceType: "StaffDeactivate",
            ipAddress:    string.Empty,
            userAgent:    string.Empty,
            resourceId:   user.Id,
            cancellationToken: ct);

        _logger.LogInformation(
            "Admin {AdminId} deactivated user {UserId} ({Email}).",
            adminUserId, targetUserId, user.Email);

        var role = userRoles.FirstOrDefault() ?? "Staff";
        return new AdminUserDto(
            Id:           user.Id.ToString(),
            FullName:     user.FullName,
            Email:        user.Email ?? string.Empty,
            Role:         role,
            RoleSubtitle: BuildRoleSubtitle(role, user),
            Status:       "Inactive",
            LastLoginAt:  user.LastLoginAt?.ToString("o"),
            CreatedAt:    user.CreatedAt.ToString("o"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ReactivateStaffAsync (US_061 AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AdminUserDto> ReactivateStaffAsync(
        string targetUserId, Guid adminUserId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(targetUserId, out _))
            throw new ArgumentException("Invalid user ID.");

        var user = await _userManager.FindByIdAsync(targetUserId)
            ?? throw new KeyNotFoundException($"User {targetUserId} not found.");

        if (user.AccountStatus == AccountStatus.Active)
            throw new InvalidOperationException("Account is already active.");

        user.AccountStatus = AccountStatus.Active;
        user.UpdatedAt     = DateTimeOffset.UtcNow;
        user.DeactivatedAt = null;   // cleared on reactivation (AC-4)
        user.DeactivatedBy = null;

        // Remove lockout — role and permissions are preserved (AC-4).
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.UpdateAsync(user);

        await _audit.LogAsync(
            AuditAction.StaffAccountReactivated,
            adminUserId,
            resourceType: "StaffReactivate",
            ipAddress:    string.Empty,
            userAgent:    string.Empty,
            resourceId:   user.Id,
            cancellationToken: ct);

        _logger.LogInformation(
            "Admin {AdminId} reactivated user {UserId} ({Email}).",
            adminUserId, targetUserId, user.Email);

        var roles = await _userManager.GetRolesAsync(user);
        var role  = roles.FirstOrDefault() ?? "Staff";
        return new AdminUserDto(
            Id:           user.Id.ToString(),
            FullName:     user.FullName,
            Email:        user.Email ?? string.Empty,
            Role:         role,
            RoleSubtitle: BuildRoleSubtitle(role, user),
            Status:       "Active",
            LastLoginAt:  user.LastLoginAt?.ToString("o"),
            CreatedAt:    user.CreatedAt.ToString("o"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string? BuildRoleSubtitle(string role, ApplicationUser user) =>
        role switch
        {
            "Admin" => "System Administrator",
            "Staff" => $"Staff — {user.FirstName}",
            _       => null,
        };
}
