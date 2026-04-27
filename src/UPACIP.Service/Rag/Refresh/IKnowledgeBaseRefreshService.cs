using UPACIP.Service.Rag.Refresh.Models;

namespace UPACIP.Service.Rag.Refresh;

/// <summary>
/// Contract for the quarterly knowledge-base refresh pipeline (US_078 AC-3, AIR-R05).
///
/// Refresh lifecycle:
/// <list type="number">
///   <item>Diff incoming <see cref="KbRefreshRequest.Entries"/> against live embedding table.</item>
///   <item>Chunk + embed new/updated entries via <c>IDocumentChunkingService</c> and
///         <c>IEmbeddingGenerationService</c>; write to staging table.</item>
///   <item>Verify staging row count meets expectation.</item>
///   <item>Execute atomic table swap inside a PostgreSQL transaction.</item>
///   <item>Soft-mark deprecated entries with <c>deprecated_at</c> timestamp.</item>
///   <item>Rebuild IVFFlat index concurrently (outside transaction).</item>
/// </list>
///
/// Mid-refresh query safety (edge case): queries always target the live table.
/// Staging table is invisible to <see cref="VectorSearch.IVectorSearchService"/> until the
/// atomic rename commits.
///
/// OWASP A01: only Admin callers may invoke this service (enforced at controller layer).
/// AIR-S04: all pipeline steps are audit-logged with user ID, version, and counts; no PII
///   (code values and descriptions are public clinical data, not patient-identifiable).
/// </summary>
public interface IKnowledgeBaseRefreshService
{
    /// <summary>
    /// Executes the full refresh pipeline for the category specified in
    /// <paramref name="request"/>.
    /// </summary>
    /// <param name="request">Validated refresh payload (entries, target category, version).</param>
    /// <param name="cancellationToken">Propagates host-shutdown or request cancellation.</param>
    /// <returns>
    /// <see cref="KbRefreshResult"/> reflecting final counts and status.
    /// On failure, <see cref="Models.RefreshStatus.Failed"/> is returned (not thrown) so the
    /// controller can surface a structured error response.
    /// </returns>
    Task<KbRefreshResult> RefreshAsync(
        KbRefreshRequest  request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the result of the most recent refresh run for admin status polling.
    /// Returns <c>null</c> if no refresh has been executed since service startup.
    /// </summary>
    Task<KbRefreshResult?> GetRefreshStatusAsync(CancellationToken cancellationToken = default);
}
