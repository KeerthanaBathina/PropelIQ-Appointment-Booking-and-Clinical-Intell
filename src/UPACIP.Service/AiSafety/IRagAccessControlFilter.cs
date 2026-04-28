using UPACIP.Service.AiSafety.Models;
using UPACIP.Service.Rag.Models;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Post-retrieval access control filter that enforces document-level permissions on
/// RAG-retrieved chunks (US_079 task_002, AIR-S07, AC-2).
///
/// <para>
/// After <c>IRagRetrievalService</c> returns candidates, this filter cross-references
/// each chunk's <see cref="RetrievedChunk.SourceDocumentId"/> against the requesting
/// user's authorized document set:
/// </para>
/// <list type="bullet">
///   <item><b>Patient</b> — may only access chunks from their own documents.</item>
///   <item><b>Staff</b>  — may access chunks from documents of patients who have had
///   an appointment with this staff member (provider).</item>
///   <item><b>Admin</b>  — may access chunks from all documents.</item>
/// </list>
///
/// <para>
/// Chunks with a <see langword="null"/> <see cref="RetrievedChunk.SourceDocumentId"/>
/// are system knowledge-base items (MedicalTerminology, IntakeTemplate, CodingGuideline)
/// and always pass through without permission checks.
/// </para>
///
/// <para>
/// All denied-access events are logged to the audit trail per AIR-S04 — never the
/// content of the denied chunks.
/// </para>
/// </summary>
public interface IRagAccessControlFilter
{
    /// <summary>
    /// Filters <paramref name="chunks"/> by the authenticated user's document-access
    /// permissions and returns only authorized chunks.
    /// </summary>
    /// <param name="chunks">Raw retrieval results to filter.</param>
    /// <param name="userId">Authenticated user's identity ID.</param>
    /// <param name="userRole">User's primary role: "Patient", "Staff", or "Admin".</param>
    /// <param name="cancellationToken">Propagates cancellation from the caller.</param>
    /// <returns>
    /// <see cref="AccessControlResult"/> containing allowed chunks, denied count, and
    /// denied document IDs for audit logging.
    /// </returns>
    Task<AccessControlResult> FilterByAccessAsync(
        IReadOnlyList<RetrievedChunk> chunks,
        Guid                          userId,
        string                        userRole,
        CancellationToken             cancellationToken = default);
}
