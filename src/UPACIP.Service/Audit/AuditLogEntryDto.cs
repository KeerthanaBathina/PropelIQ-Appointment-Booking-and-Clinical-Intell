using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Audit;

/// <summary>
/// Read-model projection of a single <c>audit_logs</c> row, joined to the user table
/// for display name and email (US_064 AC-3).
/// PII fields (IpAddress, UserAgent) are included for authorised admin consumption only.
/// They MUST NOT be written to application logs (NFR-017).
/// </summary>
public sealed record AuditLogEntryDto(
    Guid        LogId,
    Guid?       UserId,
    string?     UserEmail,
    string?     UserFullName,
    AuditAction Action,
    string      ResourceType,
    Guid?       ResourceId,
    DateTime    Timestamp,
    string      IpAddress,
    string      UserAgent);
