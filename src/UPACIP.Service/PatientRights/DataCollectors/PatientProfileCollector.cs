using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.Service.PatientRights.Models;

namespace UPACIP.Service.PatientRights.DataCollectors;

/// <summary>
/// Collects patient demographic data for the export package (US_094, AC-1).
/// Excludes internal fields: password_hash, deleted_at.
/// </summary>
public sealed class PatientProfileCollector
{
    private readonly ApplicationDbContext             _db;
    private readonly ILogger<PatientProfileCollector> _logger;

    public PatientProfileCollector(
        ApplicationDbContext             db,
        ILogger<PatientProfileCollector> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Collects the patient's demographic profile, excluding sensitive internal fields.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the patient is not found.</exception>
    public async Task<PatientProfileData> CollectAsync(Guid patientId, CancellationToken ct)
    {
        var patient = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Id == patientId)
            .Select(p => new PatientProfileData
            {
                PatientId       = p.Id,
                FullName        = p.FullName,
                Email           = p.Email,
                DateOfBirth     = p.DateOfBirth,
                PhoneNumber     = p.PhoneNumber,
                EmergencyContact = p.EmergencyContact,
                CreatedAt       = p.CreatedAt,
                UpdatedAt       = p.UpdatedAt,
            })
            .FirstOrDefaultAsync(ct);

        if (patient is null)
        {
            _logger.LogWarning("PatientProfileCollector: patient {PatientId} not found.", patientId);
            throw new InvalidOperationException($"Patient {patientId} not found.");
        }

        _logger.LogDebug("PatientProfileCollector: collected profile for patient {PatientId}.", patientId);
        return patient;
    }
}
