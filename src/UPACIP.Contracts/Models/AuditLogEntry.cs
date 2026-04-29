namespace UPACIP.Contracts.Models;

/// <summary>
/// Command model (write DTO) for creating an immutable audit log entry (US_096, AC-3, DR-016).
///
/// Consumed exclusively by the write path (<c>IAuditLogCommandService.AppendAsync</c>).
/// There is no corresponding update or delete DTO — audit entries are append-only per HIPAA.
///
/// Field mapping to the underlying <c>AuditLog</c> entity:
/// <list type="table">
///   <item><term>EntityType</term><description>Stored as <c>ResourceType</c>.</description></item>
///   <item><term>EntityId</term><description>Stored as <c>ResourceId</c>.</description></item>
/// </list>
/// </summary>
public sealed class AuditLogEntry
{
    /// <summary>Id of the user performing the action. Null for system-generated events.</summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Action performed. Maps to <c>UPACIP.DataAccess.Enums.AuditAction</c> enum name
    /// (e.g., <c>"DataModify"</c>, <c>"DataDelete"</c>, <c>"Login"</c>).
    /// Unrecognised values default to <c>DataModify</c> on the command service.
    /// </summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Entity/resource type targeted by the action (e.g., "Patient", "Appointment").</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Primary key of the affected record. Null for non-entity actions (e.g. Login).</summary>
    public Guid? EntityId { get; init; }

    /// <summary>JSON snapshot of previous state (for Update/Delete). Stored for compliance.</summary>
    public string? OldValues { get; init; }

    /// <summary>JSON snapshot of new state (for Create/Update). Stored for compliance.</summary>
    public string? NewValues { get; init; }

    /// <summary>Client IP address (IPv4 or IPv6). Should be X-Forwarded-For-aware.</summary>
    public string? IpAddress { get; init; }

    /// <summary>Correlation ID linking this entry to an HTTP request or operation chain.</summary>
    public string? CorrelationId { get; init; }
}
