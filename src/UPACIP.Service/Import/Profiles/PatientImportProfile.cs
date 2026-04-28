using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Import.Models;

namespace UPACIP.Service.Import.Profiles;

/// <summary>
/// CSV import profile for <see cref="Patient"/> entities (US_092 task_001, AC-1, AC-4, DR-001).
///
/// Required columns: email, full_name, date_of_birth.
/// Optional columns: phone_number, emergency_contact.
///
/// Duplicate detection: unique email constraint (DR-001).
/// PII columns: email — raw values are redacted in error reports.
///
/// Password: a placeholder <c>PasswordHash</c> is set; admin must trigger a password reset
/// before the imported patient can log in.
/// </summary>
public sealed class PatientImportProfile : ICsvImportProfile<Patient>
{
    // RFC 5321 / DR-011 email regex (max 254 chars enforced separately)
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex PhoneRegex = new(
        @"^\+?[0-9]{10,15}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy/MM/dd"];

    private static readonly DateTime MinDateOfBirth = new(1900, 1, 1);

    // ─────────────────────────────────────────────────────────────────────────
    // ICsvImportProfile<Patient>
    // ─────────────────────────────────────────────────────────────────────────

    public string EntityTypeName => "Patient";

    public string[] RequiredColumns => ["email", "full_name", "date_of_birth"];

    public string[] OptionalColumns => ["phone_number", "emergency_contact"];

    public HashSet<string> PiiColumns => ["email"];

    public Patient MapRow(Dictionary<string, string> row)
    {
        row.TryGetValue("phone_number",     out var phone);
        row.TryGetValue("emergency_contact", out var emergencyContact);

        DateOnly dob = DateOnly.MinValue;
        if (row.TryGetValue("date_of_birth", out var dobStr) &&
            DateTime.TryParseExact(dobStr.Trim(), DateFormats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsedDob))
        {
            dob = DateOnly.FromDateTime(parsedDob);
        }

        return new Patient
        {
            Email            = row.GetValueOrDefault("email",     string.Empty).Trim().ToLowerInvariant(),
            FullName         = row.GetValueOrDefault("full_name", string.Empty).Trim(),
            DateOfBirth      = dob,
            PhoneNumber      = phone?.Trim()            ?? string.Empty,
            EmergencyContact = emergencyContact?.Trim() ?? null,
            // Placeholder hash — admin must force a password reset before the patient can log in.
            PasswordHash     = "$2a$10$PLACEHOLDER_HASH_IMPORT_RESET_REQUIRED_xxxxxxxxxxx",
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };
    }

    public List<RowError> ValidateRow(Dictionary<string, string> row, int rowNumber)
    {
        var errors = new List<RowError>();

        // email
        var email = row.GetValueOrDefault("email", string.Empty).Trim();
        if (string.IsNullOrEmpty(email))
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "email",
                ErrorMessage = "Email is required.", RawValue = "[REDACTED]" });
        else if (email.Length > 254 || !EmailRegex.IsMatch(email))
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "email",
                ErrorMessage = "Invalid email format.", RawValue = "[REDACTED]" });

        // full_name
        var fullName = row.GetValueOrDefault("full_name", string.Empty).Trim();
        if (string.IsNullOrEmpty(fullName))
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "full_name",
                ErrorMessage = "Full name is required.", RawValue = string.Empty });
        else if (fullName.Length > 200)
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "full_name",
                ErrorMessage = "Full name exceeds 200 characters.",
                RawValue = fullName[..Math.Min(100, fullName.Length)] });

        // date_of_birth
        var dobStr = row.GetValueOrDefault("date_of_birth", string.Empty).Trim();
        if (string.IsNullOrEmpty(dobStr))
        {
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "date_of_birth",
                ErrorMessage = "Date of birth is required.", RawValue = string.Empty });
        }
        else if (!DateTime.TryParseExact(dobStr, DateFormats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsedDob))
        {
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "date_of_birth",
                ErrorMessage = "Invalid date of birth. Accepted formats: yyyy-MM-dd, MM/dd/yyyy, dd/MM/yyyy.",
                RawValue = dobStr.Length > 100 ? dobStr[..100] : dobStr });
        }
        else if (parsedDob > DateTime.UtcNow)
        {
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "date_of_birth",
                ErrorMessage = "Date of birth cannot be in the future.",
                RawValue = dobStr.Length > 100 ? dobStr[..100] : dobStr });
        }
        else if (parsedDob < MinDateOfBirth)
        {
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "date_of_birth",
                ErrorMessage = "Date of birth cannot be before 1900-01-01.",
                RawValue = dobStr.Length > 100 ? dobStr[..100] : dobStr });
        }

        // phone_number (optional)
        if (row.TryGetValue("phone_number", out var phone) && !string.IsNullOrEmpty(phone?.Trim()))
        {
            var p = phone.Trim();
            if (!PhoneRegex.IsMatch(p))
                errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "phone_number",
                    ErrorMessage = "Phone number must be 10-15 digits (optionally starting with +).",
                    RawValue = p.Length > 100 ? p[..100] : p });
        }

        return errors;
    }

    public async Task<bool> IsDuplicateAsync(
        Patient             entity,
        ApplicationDbContext context,
        CancellationToken   ct = default)
    {
        return await context.Patients
            .AnyAsync(p => p.Email == entity.Email, ct)
            .ConfigureAwait(false);
    }

    public string DuplicateKey(Patient entity) => "patient:email=[REDACTED]";
}
