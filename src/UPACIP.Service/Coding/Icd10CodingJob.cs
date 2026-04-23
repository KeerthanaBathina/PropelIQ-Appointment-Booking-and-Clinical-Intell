namespace UPACIP.Service.Coding;

/// <summary>
/// Payload serialised to the Redis coding queue and consumed by <see cref="Icd10CodingWorker"/>.
/// </summary>
public sealed record Icd10CodingJob
{
    /// <summary>Unique identifier for this coding job (returned as job ID in the 202 response).</summary>
    public Guid JobId { get; init; } = Guid.NewGuid();

    /// <summary>Patient whose diagnoses should be coded.</summary>
    public Guid PatientId { get; init; }

    /// <summary>IDs of <c>ExtractedData</c> rows to process.</summary>
    public IReadOnlyList<Guid> DiagnosisIds { get; init; } = [];

    /// <summary>Request correlation ID forwarded from the HTTP request (NFR-035).</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the job was enqueued.</summary>
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
}
