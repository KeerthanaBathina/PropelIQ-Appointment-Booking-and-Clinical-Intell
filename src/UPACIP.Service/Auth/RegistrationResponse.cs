namespace UPACIP.Service.Auth;

/// <summary>
/// Structured response returned by registration and verification endpoints.
/// </summary>
public sealed record RegistrationResponse
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable message safe to surface to the client.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Optional redirect URL (used by verify-email success to redirect to login).
    /// </summary>
    public string? RedirectUrl { get; init; }

    /// <summary>
    /// Optional field-level validation errors keyed by property name.
    /// Populated when password complexity or other field rules fail.
    /// </summary>
    public IDictionary<string, string[]>? ValidationErrors { get; init; }

    public static RegistrationResponse Ok(string message, string? redirectUrl = null)
        => new() { Success = true, Message = message, RedirectUrl = redirectUrl };

    public static RegistrationResponse Fail(string message,
        IDictionary<string, string[]>? validationErrors = null)
        => new() { Success = false, Message = message, ValidationErrors = validationErrors };
}
