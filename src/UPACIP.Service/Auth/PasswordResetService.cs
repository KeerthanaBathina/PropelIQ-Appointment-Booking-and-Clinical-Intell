using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Auth;

/// <summary>
/// Business logic for the password reset flow (US_015).
///
/// Security design (OWASP Forgot Password Cheat Sheet):
///   - Anti-enumeration: identical responses are returned regardless of whether the
///     supplied email is registered (AC-1 / OWASP A07).
///   - Token hash: only the SHA-256 hash of the raw Identity token is stored in the
///     database; the plaintext token is never persisted.
///   - Single-use: each token is marked <c>is_used = true</c> on successful reset.
///   - Latest-only: all previous unused/unexpired tokens for the user are invalidated
///     when a new token is generated (edge case: multiple reset requests).
///   - Post-reset session cleanup: all active Redis sessions are invalidated so no
///     existing session can continue using the old password (AC-4).
///   - Audit logging: all password reset events are written to the immutable audit
///     trail (FR-006).
/// </summary>
public sealed class PasswordResetService : IPasswordResetService
{
    private static readonly TimeSpan TokenExpiry = TimeSpan.FromHours(1);

    // Redis rate-limit key prefix: "pr:rl:{email}" (shared with controller layer)
    // Rate limiting itself is applied by the ASP.NET Core rate-limiter middleware;
    // this constant is documented here for traceability.
    private const string RateLimitKeyPrefix = "pr:rl:";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _emailService;
    private readonly ISessionService _sessionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        IEmailService emailService,
        ISessionService sessionService,
        IConfiguration configuration,
        ILogger<PasswordResetService> logger)
    {
        _userManager    = userManager;
        _db             = db;
        _emailService   = emailService;
        _sessionService = sessionService;
        _configuration  = configuration;
        _logger         = logger;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RequestResetAsync
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<PasswordResetRequestResult> RequestResetAsync(
        string email,
        string requestIpAddress,
        CancellationToken cancellationToken = default)
    {
        const string AntiEnumerationMessage =
            "If an account exists with that email, a password reset link has been sent.";

        // Sanitise input at the system boundary.
        email = email.Trim().ToLowerInvariant();

        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            // Anti-enumeration (OWASP A07): log without leaking whether the email exists.
            _logger.LogInformation(
                "Password reset requested for non-existent email. IP={IpAddress}.", requestIpAddress);
            return new PasswordResetRequestResult(true, AntiEnumerationMessage);
        }

        // Invalidate all previous unused tokens for this user before issuing a new one.
        // This ensures only the latest token is valid (edge case requirement).
        await InvalidatePriorTokensAsync(user.Id, cancellationToken);

        // Generate the raw token via ASP.NET Core Identity (URL-safe by default).
        var rawToken = await _userManager.GeneratePasswordResetTokenAsync(user);

        // Store only the SHA-256 hash — never the plaintext token (security boundary).
        var tokenHash = ComputeSha256Hash(rawToken);
        var expiry    = DateTime.UtcNow.Add(TokenExpiry);

        var tokenRecord = new PasswordResetToken
        {
            Id        = Guid.NewGuid(),
            UserId    = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = expiry,
            CreatedAt = DateTime.UtcNow,
        };

        _db.PasswordResetTokens.Add(tokenRecord);

        // Audit log: password reset requested (FR-006).
        _db.AuditLogs.Add(new AuditLog
        {
            LogId        = Guid.NewGuid(),
            UserId       = user.Id,
            Action       = AuditAction.PasswordResetRequest,
            ResourceType = "ApplicationUser",
            ResourceId   = user.Id,
            Timestamp    = DateTime.UtcNow,
            IpAddress    = requestIpAddress,
            UserAgent    = string.Empty,
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Build the reset link — token is already URL-safe (Identity data protection).
        var frontendBaseUrl = _configuration["AppSettings:FrontendBaseUrl"]?.TrimEnd('/')
            ?? "http://localhost:3000";

        var resetLink = $"{frontendBaseUrl}/reset-password"
            + $"?token={Uri.EscapeDataString(rawToken)}"
            + $"&email={Uri.EscapeDataString(user.Email ?? email)}";

        // Send reset email with Polly retry (implemented inside SmtpEmailService).
        try
        {
            await _emailService.SendPasswordResetEmailAsync(
                user.Email ?? email,
                user.FullName ?? user.UserName ?? email,
                resetLink,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // SMTP failure must not reveal the user's existence — log and return success.
            _logger.LogError(ex,
                "Failed to dispatch password reset email for UserId={UserId}.", user.Id);
        }

        _logger.LogInformation(
            "Password reset token issued for UserId={UserId}. IP={IpAddress}.", user.Id, requestIpAddress);

        return new PasswordResetRequestResult(true, AntiEnumerationMessage);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ResetPasswordAsync
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<PasswordResetResult> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        string requestIpAddress,
        CancellationToken cancellationToken = default)
    {
        email = email.Trim().ToLowerInvariant();
        token = token.Trim();

        // Validate password complexity before hitting Identity (fail fast, better errors).
        var complexityErrors = ValidatePasswordComplexity(newPassword);
        if (complexityErrors.Count > 0)
        {
            return new PasswordResetResult(
                ResetPasswordOutcome.PasswordComplexityFailed,
                "Password does not meet complexity requirements.",
                complexityErrors);
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            _logger.LogWarning(
                "Password reset attempted for non-existent user. IP={IpAddress}.", requestIpAddress);
            return new PasswordResetResult(ResetPasswordOutcome.InvalidUser, "Invalid reset request.");
        }

        // Verify token record: check hash, expiry, and use/invalidation status.
        var tokenHash   = ComputeSha256Hash(token);
        var tokenRecord = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.TokenHash == tokenHash)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (tokenRecord is null || tokenRecord.IsUsed || tokenRecord.InvalidatedAt is not null)
        {
            _logger.LogWarning(
                "Password reset with invalid or already-used token. UserId={UserId} IP={IpAddress}.",
                user.Id, requestIpAddress);
            await WriteAuditAsync(user.Id, AuditAction.PasswordResetFailure, requestIpAddress, cancellationToken);
            return new PasswordResetResult(ResetPasswordOutcome.InvalidToken, "Invalid reset link.");
        }

        if (tokenRecord.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning(
                "Password reset with expired token. UserId={UserId} IP={IpAddress}.",
                user.Id, requestIpAddress);
            await WriteAuditAsync(user.Id, AuditAction.PasswordResetFailure, requestIpAddress, cancellationToken);
            return new PasswordResetResult(ResetPasswordOutcome.ExpiredToken,
                "Reset link has expired. Please request a new one.");
        }

        // Attempt the reset via ASP.NET Core Identity.
        // ResetPasswordAsync: verifies the raw token cryptographically, hashes the new
        // password with BCrypt (work factor 10, NFR-013), and updates SecurityStamp
        // (invalidates ALL existing refresh tokens + forces re-login — AC-4).
        var resetResult = await _userManager.ResetPasswordAsync(user, token, newPassword);

        if (!resetResult.Succeeded)
        {
            // Determine whether it's an expiry or invalid-token failure.
            var isExpired = resetResult.Errors
                .Any(e => e.Code is "InvalidToken" or "PasswordMismatch");

            _logger.LogWarning(
                "Identity ResetPasswordAsync failed for UserId={UserId}: {Errors}",
                user.Id, string.Join(", ", resetResult.Errors.Select(e => e.Code)));

            await WriteAuditAsync(user.Id, AuditAction.PasswordResetFailure, requestIpAddress, cancellationToken);

            return isExpired
                ? new PasswordResetResult(ResetPasswordOutcome.ExpiredToken,
                    "Reset link has expired. Please request a new one.")
                : new PasswordResetResult(ResetPasswordOutcome.InvalidToken, "Invalid reset link.");
        }

        // Mark the token as used to prevent replay attacks.
        tokenRecord.IsUsed = true;

        // Audit log: successful password reset (FR-006).
        _db.AuditLogs.Add(new AuditLog
        {
            LogId        = Guid.NewGuid(),
            UserId       = user.Id,
            Action       = AuditAction.PasswordResetSuccess,
            ResourceType = "ApplicationUser",
            ResourceId   = user.Id,
            Timestamp    = DateTime.UtcNow,
            IpAddress    = requestIpAddress,
            UserAgent    = string.Empty,
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Invalidate all active Redis sessions so no existing session can continue
        // using the old credentials (AC-4: old password rejected).
        await InvalidateSessionsAsync(user.Id, cancellationToken);

        _logger.LogInformation(
            "Password successfully reset for UserId={UserId}. IP={IpAddress}.", user.Id, requestIpAddress);

        return new PasswordResetResult(ResetPasswordOutcome.Success,
            "Your password has been reset successfully. You can now sign in with your new password.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    private async Task InvalidatePriorTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var priorTokens = await _db.PasswordResetTokens
            .Where(t => t.UserId == userId && !t.IsUsed && t.InvalidatedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        foreach (var prior in priorTokens)
        {
            prior.InvalidatedAt = now;
        }
    }

    private async Task InvalidateSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await _sessionService.InvalidateSessionAsync(userId.ToString(), cancellationToken);
            _logger.LogInformation(
                "Redis session invalidated after password reset for UserId={UserId}.", userId);
        }
        catch (Exception ex)
        {
            // Redis unavailability must not fail the password reset — log and continue.
            _logger.LogError(ex,
                "Failed to invalidate Redis session after password reset for UserId={UserId}.", userId);
        }
    }

    private async Task WriteAuditAsync(
        Guid userId,
        AuditAction action,
        string ipAddress,
        CancellationToken cancellationToken)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            LogId        = Guid.NewGuid(),
            UserId       = userId,
            Action       = action,
            ResourceType = "ApplicationUser",
            ResourceId   = userId,
            Timestamp    = DateTime.UtcNow,
            IpAddress    = ipAddress,
            UserAgent    = string.Empty,
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static Dictionary<string, string[]> ValidatePasswordComplexity(string password)
    {
        var errors = new Dictionary<string, string[]>();
        var messages = new List<string>();

        if (password.Length < 8)
            messages.Add("Password must be at least 8 characters.");
        if (!password.Any(char.IsUpper))
            messages.Add("Password must contain at least 1 uppercase letter.");
        if (!password.Any(char.IsDigit))
            messages.Add("Password must contain at least 1 number.");
        if (password.All(c => char.IsLetterOrDigit(c)))
            messages.Add("Password must contain at least 1 special character.");

        if (messages.Count > 0)
            errors["newPassword"] = [.. messages];

        return errors;
    }
}
