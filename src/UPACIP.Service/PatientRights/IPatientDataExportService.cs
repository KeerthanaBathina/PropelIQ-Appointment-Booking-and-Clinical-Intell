using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.PatientRights;

/// <summary>
/// Contract for the HIPAA Right of Access patient data export service (US_094, NFR-044).
/// </summary>
public interface IPatientDataExportService
{
    /// <summary>
    /// Submits a new data access request for the specified patient and records a 30-day SLA deadline.
    /// </summary>
    Task<DataAccessRequest> SubmitRequestAsync(Guid patientId, string requestedBy, CancellationToken ct);

    /// <summary>
    /// Processes an existing data access request: collects all data categories, generates
    /// JSON + PDF exports, packages them in a ZIP archive, and marks the request Completed.
    /// </summary>
    Task<DataAccessRequest> ProcessRequestAsync(Guid requestId, string processedBy, CancellationToken ct);

    /// <summary>
    /// Returns the ZIP archive stream for a completed export after verifying the request
    /// belongs to the requesting patient (OWASP A01).
    /// </summary>
    Task<Stream> DownloadExportAsync(Guid requestId, Guid patientId, CancellationToken ct);

    /// <summary>
    /// Returns all data access requests, optionally filtered by status and overdue flag.
    /// </summary>
    Task<List<DataAccessRequest>> GetRequestsAsync(string? status, bool overdueOnly, CancellationToken ct);

    /// <summary>
    /// Returns a single data access request by ID — null when not found.
    /// </summary>
    Task<DataAccessRequest?> GetRequestByIdAsync(Guid requestId, CancellationToken ct);
}
