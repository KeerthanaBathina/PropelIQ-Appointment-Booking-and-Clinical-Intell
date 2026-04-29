using UPACIP.DataAccess.Entities;
using UPACIP.Service.PatientRights.Models;

namespace UPACIP.Service.PatientRights;

/// <summary>
/// HIPAA Right to Deletion service contract (US_094, NFR-045).
/// </summary>
public interface IPatientDataDeletionService
{
    /// <summary>
    /// Creates a new data deletion request with a 30-day SLA deadline and persists it.
    /// Returns the created request entity.
    /// </summary>
    Task<DataAccessRequest> SubmitDeletionRequestAsync(
        Guid patientId, string requestedBy, CancellationToken ct);

    /// <summary>
    /// Executes the six-phase deletion pipeline for the patient identified by the request
    /// and returns a detailed <see cref="DeletionResult"/> report.
    /// Throws <see cref="KeyNotFoundException"/> if the request does not exist.
    /// Throws <see cref="InvalidOperationException"/> if the request is already completed.
    /// </summary>
    Task<DeletionResult> ProcessDeletionAsync(
        Guid requestId, string processedBy, CancellationToken ct);
}
