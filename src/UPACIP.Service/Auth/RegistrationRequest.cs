namespace UPACIP.Service.Auth;

/// <summary>
/// Request DTO for patient self-registration (POST /api/auth/register).
/// FluentValidation rules are declared in <see cref="RegistrationRequestValidator"/>.
/// </summary>
public sealed record RegistrationRequest
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string DateOfBirth { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool AcceptedTerms { get; init; }
}
