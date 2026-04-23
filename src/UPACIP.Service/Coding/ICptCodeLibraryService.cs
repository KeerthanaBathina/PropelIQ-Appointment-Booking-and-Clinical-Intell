namespace UPACIP.Service.Coding;

/// <summary>
/// A single CPT code entry passed to <see cref="ICptCodeLibraryService.RefreshLibraryAsync"/>
/// (US_048, AC-4).
/// </summary>
public sealed record CptCodeEntry
{
    /// <summary>AMA CPT code value (e.g. <c>"99213"</c>). Max 10 characters.</summary>
    public string CptCode { get; init; } = string.Empty;

    /// <summary>Full clinical description of the procedure.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>CPT category label (e.g. "Evaluation &amp; Management"). Max 50 characters.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Date from which this code became effective.</summary>
    public DateOnly EffectiveDate { get; init; }

    /// <summary>Optional expiration date; <c>null</c> for currently active codes.</summary>
    public DateOnly? ExpirationDate { get; init; }
}

/// <summary>Summary result from a CPT library refresh operation (US_048, AC-4).</summary>
public sealed record CptLibraryRefreshResult
{
    /// <summary>Library version that was applied.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Number of new CPT code entries inserted into <c>cpt_code_library</c>.</summary>
    public int CodesAdded { get; init; }

    /// <summary>Number of existing entries marked <c>is_active = false</c> in this refresh.</summary>
    public int CodesDeactivated { get; init; }

    /// <summary>Number of pending <c>MedicalCode</c> records revalidated as part of this refresh.</summary>
    public int PendingCodesRevalidated { get; init; }

    /// <summary>UTC timestamp when the refresh completed.</summary>
    public DateTime RefreshedAt { get; init; }
}

/// <summary>Summary result from a CPT pending-code revalidation run (US_048, AC-4).</summary>
public sealed record CptRevalidationResult
{
    /// <summary>Total number of pending CPT <c>MedicalCode</c> records examined.</summary>
    public int TotalExamined { get; init; }

    /// <summary>Number of records confirmed as valid against the current CPT library.</summary>
    public int MarkedValid { get; init; }

    /// <summary>Number of records flagged as <c>DeprecatedReplaced</c> (code expired or retired).</summary>
    public int MarkedInvalid { get; init; }

    /// <summary>Number of records set to <c>PendingReview</c> (code absent from library).</summary>
    public int MarkedPendingReview { get; init; }

    /// <summary>UTC timestamp when the revalidation run completed.</summary>
    public DateTime RevalidatedAt { get; init; }
}

/// <summary>
/// Contract for the CPT code library management service (US_048, AC-4).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>
///     <strong>Library refresh</strong> — applies a new quarterly AMA CPT dataset:
///     inserts new entries, marks removed codes as <c>is_active = false</c>, and
///     triggers a revalidation pass on pending <c>MedicalCode</c> rows.
///   </item>
///   <item>
///     <strong>Revalidation</strong> — checks all pending (unapproved) CPT
///     <c>MedicalCode</c> rows against the active library and sets
///     <see cref="DataAccess.Enums.RevalidationStatus"/> accordingly.
///   </item>
/// </list>
///
/// OWASP A01 note: both methods are restricted to the Admin role in the controller layer.
/// </summary>
public interface ICptCodeLibraryService
{
    /// <summary>
    /// Transactionally applies a new CPT library version:
    /// <list type="number">
    ///   <item>Marks all existing <c>cpt_code_library</c> entries absent from
    ///         <paramref name="incomingCodes"/> as <c>is_active = false</c> with
    ///         <c>expiration_date = today</c>.</item>
    ///   <item>Upserts each entry from <paramref name="incomingCodes"/> for the given
    ///         <paramref name="version"/>.</item>
    ///   <item>Calls <see cref="RevalidatePendingCodesAsync"/> and returns the combined
    ///         summary.</item>
    /// </list>
    /// Rolls back the entire transaction on failure (DR-029).
    /// Writes an <c>AuditLog</c> entry with action <c>CptLibraryRefreshed</c> on success.
    /// </summary>
    /// <param name="version">Semantic version label for this library release (e.g. <c>"2026.Q3"</c>).</param>
    /// <param name="incomingCodes">Full set of CPT codes for the new version.</param>
    /// <param name="correlationId">Request trace correlation ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CptLibraryRefreshResult> RefreshLibraryAsync(
        string                    version,
        IReadOnlyList<CptCodeEntry> incomingCodes,
        string                    correlationId,
        CancellationToken         ct = default);

    /// <summary>
    /// Checks all pending (unapproved) CPT <c>MedicalCode</c> rows against the currently
    /// active <c>cpt_code_library</c> and updates their <c>RevalidationStatus</c>:
    /// <list type="bullet">
    ///   <item><c>Valid</c> — code is present and <c>is_active = true</c>.</item>
    ///   <item><c>DeprecatedReplaced</c> — code is in the library but <c>is_active = false</c>.</item>
    ///   <item><c>PendingReview</c> — code not found in the library at all.</item>
    /// </list>
    /// </summary>
    /// <param name="correlationId">Request trace correlation ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CptRevalidationResult> RevalidatePendingCodesAsync(
        string            correlationId,
        CancellationToken ct = default);
}
