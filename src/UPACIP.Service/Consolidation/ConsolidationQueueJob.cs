using System.Text.Json.Serialization;

namespace UPACIP.Service.Consolidation;

/// <summary>
/// Immutable payload stored in the Redis consolidation queue (US_043, FR-052).
///
/// Each entry represents one pending consolidation request for a patient.
/// The worker deserializes this job and routes it to <see cref="IConsolidationService"/>.
///
/// Serialized to JSON via System.Text.Json for Redis storage.
/// </summary>
public sealed record ConsolidationQueueJob
{
    /// <summary>Patient whose profile should be consolidated.</summary>
    [JsonPropertyName("patientId")]
    public Guid PatientId { get; init; }

    /// <summary>
    /// When non-null, only these specific document IDs are merged (incremental mode).
    /// When null or empty, a full consolidation over all parsed documents is performed.
    /// </summary>
    [JsonPropertyName("newDocumentIds")]
    public List<Guid>? NewDocumentIds { get; init; }

    /// <summary>Staff user who triggered the consolidation. NULL for automated pipeline triggers.</summary>
    [JsonPropertyName("triggeredByUserId")]
    public Guid? TriggeredByUserId { get; init; }

    /// <summary>UTC timestamp when this job was enqueued.</summary>
    [JsonPropertyName("enqueuedAt")]
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
}
