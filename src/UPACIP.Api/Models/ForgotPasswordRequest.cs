using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Models;

/// <summary>
/// Request body for POST /api/auth/forgot-password.
/// Anti-enumeration: identical 200 responses are returned for registered and
/// non-registered emails (OWASP Forgot Password Cheat Sheet).
/// </summary>
public sealed record ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "A valid email address is required.")]
    [MaxLength(256, ErrorMessage = "Email must not exceed 256 characters.")]
    public string Email { get; init; } = string.Empty;
}
