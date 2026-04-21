using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Seeding;

/// <summary>
/// Startup service that idempotently seeds the default platform administrator account.
///
/// <para>
/// Roles (Patient, Staff, Admin) are already seeded via EF Core <c>HasData()</c> in
/// <see cref="RoleSeedConfiguration"/> and are applied by the <c>AddIdentitySchema</c>
/// migration.  This service is only responsible for the <em>user</em> record.
/// </para>
///
/// <para>Security design:</para>
/// <list type="bullet">
///   <item>Credentials are read from <c>DefaultAdmin:Email</c> / <c>DefaultAdmin:Password</c>
///         in application configuration — never hardcoded.</item>
///   <item>Password must satisfy the platform's complexity rules (8+ chars, uppercase, digit,
///         special character) — enforced by <see cref="UserManager{TUser}.CreateAsync"/>.</item>
///   <item>A <c>must_change_password</c> user claim is added so the application layer can
///         prompt the admin to set a new password on first login (OWASP Secure Defaults).</item>
///   <item>The service is idempotent — calling it repeatedly produces no duplicates.</item>
///   <item>Skipped entirely in Production to prevent accidental seeding (defence-in-depth).</item>
/// </list>
/// </summary>
public sealed class AdminSeedService : IHostedService
{
    /// <summary>User claim type added to the default admin to force a password change on first login.</summary>
    public const string MustChangePasswordClaim = "must_change_password";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration       _configuration;
    private readonly IHostEnvironment     _environment;
    private readonly ILogger<AdminSeedService> _logger;

    public AdminSeedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration       configuration,
        IHostEnvironment     environment,
        ILogger<AdminSeedService> logger)
    {
        _scopeFactory  = scopeFactory;
        _configuration = configuration;
        _environment   = environment;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Production safety guard — never seed credentials in Production.
        if (_environment.IsProduction())
        {
            _logger.LogInformation(
                "AdminSeedService: Skipped — admin seeding is disabled in the Production environment.");
            return;
        }

        var adminEmail    = _configuration["DefaultAdmin:Email"];
        var adminPassword = _configuration["DefaultAdmin:Password"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            _logger.LogWarning(
                "AdminSeedService: DefaultAdmin:Email or DefaultAdmin:Password is not configured. "
                + "Skipping admin seed. Set these values via user secrets: "
                + "dotnet user-secrets set \"DefaultAdmin:Email\" \"admin@example.com\"");
            return;
        }

        await using var scope       = _scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Idempotency check — skip if admin account already exists.
        var existing = await userManager.FindByEmailAsync(adminEmail);
        if (existing is not null)
        {
            _logger.LogDebug(
                "AdminSeedService: Default admin account {Email} already exists. No action taken.",
                adminEmail);
            return;
        }

        _logger.LogInformation(
            "AdminSeedService: Creating default admin account for {Email}.", adminEmail);

        var admin = new ApplicationUser
        {
            UserName      = adminEmail,
            Email         = adminEmail,
            FirstName     = "Platform",
            LastName      = "Admin",
            FullName      = "Platform Admin",
            EmailConfirmed = true,          // Skip email verification for seed admin account.
            AccountStatus = AccountStatus.Active,
        };

        var createResult = await userManager.CreateAsync(admin, adminPassword);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            _logger.LogError(
                "AdminSeedService: Failed to create default admin account {Email}. Errors: {Errors}",
                adminEmail, errors);
            return;
        }

        // Assign the Admin role.
        var roleResult = await userManager.AddToRoleAsync(admin, "Admin");
        if (!roleResult.Succeeded)
        {
            var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
            _logger.LogError(
                "AdminSeedService: Failed to assign Admin role to {Email}. Errors: {Errors}",
                adminEmail, errors);
            return;
        }

        // Force a password change on first login (OWASP Secure Defaults).
        // The claim is checked by the login endpoint and removed after the user sets a new password.
        await userManager.AddClaimAsync(
            admin,
            new System.Security.Claims.Claim(MustChangePasswordClaim, "true"));

        _logger.LogInformation(
            "AdminSeedService: Default admin account {Email} created and assigned to Admin role. "
            + "MustChangePassword claim set — admin must change password on first login.",
            adminEmail);
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
