using System.Text.Json.Serialization;

namespace UPACIP.Service.Audit;

/// <summary>
/// Serializable DTO used to persist an <c>AuditLog</c> entry in the Redis failover queue
/// when the PostgreSQL write fails (US_064 edge case: DB unavailable).
///
/// Kept separate from the EF Core entity to avoid serialization issues with navigation
/// properties and EF change-tracker proxies.  Uses <c>System.Text.Json</c> with camelCase
/// property naming (the default for .NET 8 <see cref="System.Text.Json.JsonSerializerOptions"/>).
/// </summary>
public sealed class AuditLogQueueEntry
{
    /// <summary>Surrogate UUID primary key — also used for idempotent de-duplicate during flush.</summary>
    [JsonPropertyName("logId")]
    public Guid LogId { get; init; } = Guid.NewGuid();

    /// <summary>FK to the user who performed the action (nullable for system events).</summary>
    [JsonPropertyName("userId")]
    public Guid? UserId { get; init; }

    /// <summary>Action type serialized as the enum name string (e.g. "DataAccess").</summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    /// <summary>Entity type targeted by the action (e.g. "Patient", "Appointment").</summary>
    [JsonPropertyName("resourceType")]
    public string ResourceType { get; init; } = string.Empty;

    /// <summary>Primary key of the affected record. Null for non-entity actions.</summary>
    [JsonPropertyName("resourceId")]
    public Guid? ResourceId { get; init; }

    /// <summary>UTC timestamp when the action was recorded in the application.</summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Client IP address at the time of the action (IPv4 or IPv6, max 45 chars).</summary>
    [JsonPropertyName("ipAddress")]
    public string IpAddress { get; init; } = string.Empty;

    /// <summary>HTTP User-Agent header value (max 500 chars).</summary>
    [JsonPropertyName("userAgent")]
    public string UserAgent { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp when this entry was pushed to the Redis failover queue.
    /// Used for staleness monitoring — entries queued for more than 1 hour indicate
    /// a prolonged DB outage and should trigger an operational alert.
    /// </summary>
    [JsonPropertyName("enqueuedAt")]
    public DateTime EnqueuedAt { get; init; } = DateTime.UtcNow;
}
