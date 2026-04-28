using Microsoft.EntityFrameworkCore;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Import.Models;

namespace UPACIP.Service.Import.Profiles;

/// <summary>
/// CSV import profile for <see cref="Appointment"/> entities (US_092 task_001, AC-1, AC-4, DR-014).
///
/// Required columns: patient_email, appointment_time, status.
/// Optional columns: is_walk_in.
///
/// Duplicate detection: unique (patient_id, appointment_time) composite constraint (DR-014).
/// FK resolution: patient_email is resolved to PatientId via a database lookup.
///
/// Security (OWASP A03): all DB queries are parameterised via EF Core.
/// PII: patient_email raw values are redacted in error reports.
/// </summary>
public sealed class AppointmentImportProfile : ICsvImportProfile<Appointment>
{
    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",  "yyyy-MM-dd HH:mm",
        "MM/dd/yyyy HH:mm:ss",  "MM/dd/yyyy HH:mm",
        "dd/MM/yyyy HH:mm:ss",  "dd/MM/yyyy HH:mm",
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // ICsvImportProfile<Appointment>
    // ─────────────────────────────────────────────────────────────────────────

    public string EntityTypeName => "Appointment";

    public string[] RequiredColumns => ["patient_email", "appointment_time", "status"];

    public string[] OptionalColumns => ["is_walk_in"];

    public HashSet<string> PiiColumns => ["patient_email"];

    public Appointment MapRow(Dictionary<string, string> row)
    {
        _ = DateTime.TryParseExact(
            row.GetValueOrDefault("appointment_time", string.Empty).Trim(),
            DateTimeFormats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var appointmentTime);

        var status = ParseStatus(row.GetValueOrDefault("status", string.Empty));

        bool isWalkIn = false;
        if (row.TryGetValue("is_walk_in", out var walkInStr))
            _ = bool.TryParse(walkInStr?.Trim(), out isWalkIn);

        return new Appointment
        {
            // PatientId must be set by the engine after FK resolution;
            // we set a sentinel value here — the engine replaces it before persistence.
            PatientId       = Guid.Empty,
            AppointmentTime = DateTime.SpecifyKind(appointmentTime, DateTimeKind.Utc),
            Status          = status,
            IsWalkIn        = isWalkIn,
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow,
        };
    }

    public List<RowError> ValidateRow(Dictionary<string, string> row, int rowNumber)
    {
        var errors = new List<RowError>();

        // patient_email
        var patientEmail = row.GetValueOrDefault("patient_email", string.Empty).Trim();
        if (string.IsNullOrEmpty(patientEmail))
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "patient_email",
                ErrorMessage = "Patient email is required.", RawValue = "[REDACTED]" });

        // appointment_time
        var apptTimeStr = row.GetValueOrDefault("appointment_time", string.Empty).Trim();
        if (string.IsNullOrEmpty(apptTimeStr))
        {
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "appointment_time",
                ErrorMessage = "Appointment time is required.", RawValue = string.Empty });
        }
        else if (!DateTime.TryParseExact(apptTimeStr, DateTimeFormats,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out _))
        {
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "appointment_time",
                ErrorMessage = "Invalid appointment time. Use ISO 8601 format (e.g. 2026-04-28T09:00:00Z).",
                RawValue = apptTimeStr.Length > 100 ? apptTimeStr[..100] : apptTimeStr });
        }

        // status
        var statusStr = row.GetValueOrDefault("status", string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(statusStr))
        {
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "status",
                ErrorMessage = "Status is required.", RawValue = string.Empty });
        }
        else if (!IsValidStatus(statusStr))
        {
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "status",
                ErrorMessage = "Invalid status. Allowed values: scheduled, completed, cancelled, no-show.",
                RawValue = statusStr.Length > 100 ? statusStr[..100] : statusStr });
        }

        return errors;
    }

    public async Task<bool> IsDuplicateAsync(
        Appointment          entity,
        ApplicationDbContext context,
        CancellationToken    ct = default)
    {
        if (entity.PatientId == Guid.Empty)
            return false;

        return await context.Appointments
            .AnyAsync(a => a.PatientId       == entity.PatientId &&
                           a.AppointmentTime == entity.AppointmentTime, ct)
            .ConfigureAwait(false);
    }

    public string DuplicateKey(Appointment entity)
        => $"appointment:patient_id={entity.PatientId},time={entity.AppointmentTime:O}";

    // ─────────────────────────────────────────────────────────────────────────
    // FK resolution (called by CsvImportEngine before duplicate check)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves <c>patient_email</c> to a <see cref="Patient.Id"/> and assigns it to
    /// <paramref name="entity"/>. Returns <c>true</c> if the patient was found.
    /// </summary>
    public async Task<bool> TryResolvePatientIdAsync(
        Appointment          entity,
        Dictionary<string, string> row,
        ApplicationDbContext context,
        List<RowError>       errors,
        int                  rowNumber,
        CancellationToken    ct = default)
    {
        var email     = row.GetValueOrDefault("patient_email", string.Empty).Trim().ToLowerInvariant();
        var patientId = await context.Patients
            .Where(p => p.Email == email)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (patientId is null)
        {
            errors.Add(new RowError
            {
                RowNumber    = rowNumber,
                ColumnName   = "patient_email",
                ErrorMessage = "Patient with the specified email was not found.",
                RawValue     = "[REDACTED]",
            });
            return false;
        }

        entity.PatientId = patientId.Value;
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsValidStatus(string s) =>
        s is "scheduled" or "completed" or "cancelled" or "no-show";

    private static AppointmentStatus ParseStatus(string s) =>
        s.Trim().ToLowerInvariant() switch
        {
            "completed"  => AppointmentStatus.Completed,
            "cancelled"  => AppointmentStatus.Cancelled,
            "no-show"    => AppointmentStatus.NoShow,
            _            => AppointmentStatus.Scheduled,
        };
}
