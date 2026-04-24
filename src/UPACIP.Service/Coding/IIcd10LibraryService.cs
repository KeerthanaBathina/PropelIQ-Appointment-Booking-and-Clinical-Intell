using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Coding;

/// <summary>Summary result from a library refresh operation (US_047, AC-3).</summary>
public sealed record LibraryRefreshResult
{
    public string Version { get; init; } = string.Empty;
    public int CodesAdded { get; init; }
    public int CodesDeprecated { get; init; }
    public int PendingCodesRevalidated { get; init; }
    public int DeprecatedRecordsFlagged { get; init; }
    public DateTime RefreshedAt { get; init; }
}

/// <summary>Summary result from a pending-code revalidation run (US_047, AC-3).</summary>
public sealed record RevalidationResult
{
    public int TotalExamined { get; init; }
    public int MarkedValid { get; init; }
    public int MarkedDeprecated { get; init; }
    public int MarkedPendingReview { get; init; }
    public DateTime RevalidatedAt { get; init; }
}

/// <summary>
/// Contract for the ICD-10 library management service (US_047, AC-3, edge case: deprecated-code handling).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>
///     <strong>Library refresh</strong> — applies a new quarterly ICD-10 dataset:
///     inserts new entries, marks removed codes as <c>is_current = false</c> with
///     <c>deprecated_date</c>, and triggers a revalidation pass on pending
///     <c>MedicalCode</c> records.
///   </item>
///   <item>
///     <strong>Revalidation</strong> — checks all pending (unapproved) <c>MedicalCode</c>
///     rows against the current library and sets <see cref="DataAccess.Enums.RevalidationStatus"/>
///     to <c>Valid</c>, <c>PendingReview</c>, or <c>DeprecatedReplaced</c>.
///   </item>
/// </list>
///
/// OWASP A01 note: both methods are restricted to the Admin role in the controller layer.
/// </summary>
public interface IIcd10LibraryService
{
    /// <summary>
    /// Transactionally applies a new ICD-10 library version:
    /// <list type="number">
    ///   <item>Marks all existing <c>icd10_code_library</c> entries that are absent from
    ///         <paramref name="incomingCodes"/> as <c>is_current = false</c> and stamps
    ///         <c>deprecated_date</c>.</item>
    ///   <item>Upserts each entry from <paramref name="incomingCodes"/> for the given
    ///         <paramref name="version"/>.</item>
    ///   <item>Calls <see cref="RevalidatePendingCodesAsync"/> and returns the combined
    ///         summary.</item>
    /// </list>
    /// Rolls back the entire transaction on failure so the library is never left in a
    /// partially updated state (DR-029).
    /// </summary>
    /// <param name="version">Version string (e.g. <c>"2026.2"</c>). Must be unique.</param>
    /// <param name="incomingCodes">New code entries for this version.</param>
    /// <param name="correlationId">Correlation ID for audit logging.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<LibraryRefreshResult> RefreshLibraryAsync(
        string                          version,
        IReadOnlyList<Icd10CodeEntry>   incomingCodes,
        string                          correlationId,
        CancellationToken               ct = default);

    /// <summary>
    /// Scans all pending (unapproved) ICD-10 <c>MedicalCode</c> rows and updates
    /// <c>revalidation_status</c> and <c>library_version</c> against the current library.
    ///
    /// Outcome per row:
    /// <list type="bullet">
    ///   <item><c>Valid</c> — code exists and is current in the latest library version.</item>
    ///   <item><c>DeprecatedReplaced</c> — code is deprecated; <c>library_version</c> is
    ///         updated to point at the entry that introduced the deprecation (edge case).</item>
    ///   <item><c>PendingReview</c> — code is not present in any known library version.</item>
    /// </list>
    /// </summary>
    /// <param name="correlationId">Correlation ID for audit logging.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RevalidationResult> RevalidatePendingCodesAsync(
        string            correlationId,
        CancellationToken ct = default);
}

/// <summary>
/// Lightweight DTO used internally by <see cref="IIcd10LibraryService"/> to carry
/// an incoming library code entry without coupling to the EF entity directly.
/// </summary>
public sealed record Icd10CodeEntry
{
    public string  CodeValue       { get; init; } = string.Empty;
    public string  Description     { get; init; } = string.Empty;
    public string  Category        { get; init; } = string.Empty;
    public DateOnly EffectiveDate  { get; init; }
    public DateOnly? DeprecatedDate{ get; init; }
    public string? ReplacementCode { get; init; }
}
