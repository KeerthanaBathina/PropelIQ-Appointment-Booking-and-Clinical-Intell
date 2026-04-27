using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag.Refresh.Models;

/// <summary>
/// Input payload for an admin-triggered knowledge-base refresh (US_078 AC-3, AIR-R05).
///
/// Naming note: this record lives in <c>UPACIP.Service.Rag.Refresh.Models</c> to avoid
/// collision with <c>UPACIP.Api.Controllers.AuthController.RefreshRequest</c> (JWT token
/// refresh) which is in the <c>UPACIP.Api</c> namespace.
/// </summary>
public sealed record KbRefreshRequest
{
    /// <summary>
    /// The full list of code entries for this library version.
    /// May not be null or empty (validated before the service is called).
    /// </summary>
    public required IReadOnlyList<CodeLibraryEntry> Entries { get; init; }

    /// <summary>
    /// The embedding category (table) this refresh targets.
    /// All entries in <see cref="Entries"/> are expected to have the same category;
    /// the service validates this and rejects mixed-category requests.
    /// </summary>
    public EmbeddingCategory TargetCategory { get; init; }

    /// <summary>
    /// Human-readable library version identifier, e.g. "ICD-10-CM-2026-Q2".
    /// Stored in the audit log entry for traceability (AIR-S04).
    /// </summary>
    public required string SourceVersion { get; init; }

    /// <summary>
    /// Identity of the admin who triggered the refresh.
    /// Written to the audit log; never embedded or forwarded to OpenAI (PII guard, AIR-S04).
    /// </summary>
    public required string InitiatedByUserId { get; init; }
}
