using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Models;

/// <summary>
/// Request body for POST /api/auth/reset-password.
/// Validated before being passed to <c>UserManager.ResetPasswordAsync</c>.
/// </summary>
public sealed record ResetPasswordRequest
{
    [Required(ErrorMessage = "Token is required.")]
    public string Token { get; init; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "A valid email address is required.")]
    [MaxLength(256, ErrorMessage = "Email must not exceed 256 characters.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "New password is required.")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
    [MaxLength(128, ErrorMessage = "Password must not exceed 128 characters.")]
    public string NewPassword { get; init; } = string.Empty;
}
