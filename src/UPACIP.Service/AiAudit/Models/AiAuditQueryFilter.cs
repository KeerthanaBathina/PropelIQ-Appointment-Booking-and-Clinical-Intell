namespace UPACIP.Service.AiAudit.Models;

/// <summary>
/// Query filter for the AI audit log admin API (AC-4).
///
/// <para>
/// All filter fields are optional. When multiple fields are set they are combined
/// with AND. When a cursor is present, filters are applied BEFORE keyset pagination
/// so that the result set remains consistent across pages.
/// </para>
/// </summary>
public sealed record AiAuditQueryFilter
{
    // ── Date range ────────────────────────────────────────────────────────────

    /// <summary>Inclusive start of the date range (UTC). Null = no lower bound.</summary>
    public DateTime? DateFrom { get; init; }

    /// <summary>Inclusive end of the date range (UTC). Null = no upper bound.</summary>
    public DateTime? DateTo { get; init; }

    // ── Categorical filters ───────────────────────────────────────────────────

    /// <summary>
    /// Exact model version match (e.g. <c>gpt-4o-mini-2024-07-18</c>).
    /// Null = all model versions.
    /// </summary>
    public string? ModelVersion { get; init; }

    /// <summary>
    /// Exact request type match (e.g. <c>document-parsing</c>).
    /// Null = all request types.
    /// </summary>
    public string? RequestType { get; init; }

    // ── Confidence range ──────────────────────────────────────────────────────

    /// <summary>Minimum confidence score (inclusive). Null = no lower bound.</summary>
    public float? ConfidenceMin { get; init; }

    /// <summary>Maximum confidence score (inclusive). Null = no upper bound.</summary>
    public float? ConfidenceMax { get; init; }

    // ── Cursor-based pagination ───────────────────────────────────────────────

    /// <summary>
    /// Opaque continuation token from a previous call to
    /// <see cref="IAiAuditService.QueryAuditLogsAsync"/>.
    /// Encoded as <c>base64({CreatedAt:O}|{Id:N})</c>; null = first page.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// Maximum number of records to return (default 50, maximum 200).
    /// Values outside [1, 200] are clamped silently.
    /// </summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Returns <see cref="PageSize"/> clamped to [1, 200].</summary>
    public int EffectivePageSize => Math.Clamp(PageSize, 1, 200);
}
