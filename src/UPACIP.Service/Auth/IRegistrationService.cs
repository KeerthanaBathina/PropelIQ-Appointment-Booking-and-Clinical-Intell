namespace UPACIP.Service.Auth;

/// <summary>
/// Defines the registration, email verification, and related identity operations
/// for the patient self-registration flow (US_012).
/// </summary>
public interface IRegistrationService
{
    /// <summary>
    /// Creates a new patient account with status "pending verification" and dispatches
    /// a verification email. Returns a failure result if the email already exists (AC-4)
    /// or if password complexity is not satisfied (AC-5).
    /// </summary>
    Task<RegistrationResult> RegisterAsync(
        RegistrationRequest request,
        string requestIpAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms an email verification token. On success the account status becomes "active".
    /// Returns <see cref="VerifyEmailOutcome.Expired"/> for 1-hour-expired tokens (AC-3).
    /// </summary>
    Task<VerifyEmailResult> VerifyEmailAsync(
        string token,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-sends a verification email. Rate limited to 3 requests per 5 minutes per email
    /// address (edge case). Returns <see cref="ResendOutcome.RateLimited"/> when exceeded.
    /// </summary>
    Task<ResendVerificationResult> ResendVerificationAsync(
        string email,
        string requestIpAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an email address is already registered.
    /// Returns only <c>available: true/false</c> — never reveals verification status (AC-4).
    /// </summary>
    Task<bool> IsEmailAvailableAsync(
        string email,
        CancellationToken cancellationToken = default);
}

// ── Result value objects ──────────────────────────────────────────────────────

public sealed record RegistrationResult(
    bool Success,
    string Message,
    IDictionary<string, string[]>? ValidationErrors = null);

public enum VerifyEmailOutcome { Success, Expired, Invalid }

public sealed record VerifyEmailResult(VerifyEmailOutcome Outcome, string Message);

public enum ResendOutcome { Sent, RateLimited, AlreadyVerified }

public sealed record ResendVerificationResult(ResendOutcome Outcome, string Message);
