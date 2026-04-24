using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for <c>POST /api/coding/cpt/generate</c> (US_048, AC-1, AC-3).
///
/// Enqueues an async CPT code generation job.  Returns 202 Accepted immediately.
/// Idempotency (NFR-034): supply <see cref="IdempotencyKey"/> to prevent duplicate job enqueue
/// on retry; the original job ID is returned when the key is replayed within 24 hours.
/// </summary>
public sealed record CptGenerateRequestDto
{
    /// <summary>Patient whose procedures should be coded.</summary>
    [Required]
    public Guid PatientId { get; init; }

    /// <summary>
    /// IDs of <c>ExtractedData</c> rows (procedure type) to include in this coding job.
    /// Must contain at least one ID.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one procedure ID is required.")]
    public IReadOnlyList<Guid> ProcedureIds { get; init; } = [];

    /// <summary>
    /// Optional idempotency key to prevent duplicate job enqueue on retry (NFR-034).
    /// When supplied, the controller performs an atomic Redis SET NX deduplication check.
    /// Max 128 characters.
    /// </summary>
    [MaxLength(128)]
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// Response returned by <c>POST /api/coding/cpt/generate</c> (US_048, AC-1).
///
/// Indicates the job was accepted for async processing (HTTP 202).
/// </summary>
public sealed record CptGenerateAcceptedDto
{
    /// <summary>Unique identifier of the enqueued coding job.</summary>
    public Guid JobId { get; init; }

    /// <summary>Patient the job was created for.</summary>
    public Guid PatientId { get; init; }

    /// <summary>UTC timestamp when the job was accepted.</summary>
    public DateTime AcceptedAt { get; init; }

    /// <summary>Human-readable confirmation message.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Request body for <c>PUT /api/coding/cpt/approve</c> (US_048, AC-1).
///
/// OWASP A01 note: the approving user's identity is read from the JWT bearer token in the
/// controller; it is never accepted from the request body to prevent privilege escalation.
/// </summary>
public sealed record CptApproveRequest
{
    /// <summary>
    /// Primary key of the <c>MedicalCode</c> row to approve.
    /// Must be a CPT-type code owned by the patient the caller is authorised to manage.
    /// </summary>
    [Required]
    public Guid MedicalCodeId { get; init; }
}

/// <summary>
/// Request body for <c>PUT /api/coding/cpt/override</c> (US_048, edge case — incorrect mapping).
///
/// Staff may substitute an AI-suggested CPT code with a correct one and supply a written
/// justification.  Both the replacement code and justification are stored on the
/// <c>MedicalCode</c> row and written to the HIPAA audit log (OWASP A01, HIPAA §164.312(b)).
///
/// OWASP A01 note: same as <see cref="CptApproveRequest"/> — user identity from JWT only.
/// </summary>
public sealed record CptOverrideRequest
{
    /// <summary>Primary key of the <c>MedicalCode</c> row to override.</summary>
    [Required]
    public Guid MedicalCodeId { get; init; }

    /// <summary>
    /// The correct CPT code that replaces the AI-suggested one (e.g. <c>"99214"</c>).
    /// Max 10 characters; must match the AMA CPT code format.
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string ReplacementCode { get; init; } = string.Empty;

    /// <summary>
    /// Clinical justification explaining why the AI suggestion was incorrect and the
    /// replacement is appropriate.
    /// Stored in <c>MedicalCode.Justification</c> and surfaced in the HIPAA audit trail.
    /// Max 1 000 characters.
    /// </summary>
    [Required]
    [MinLength(10, ErrorMessage = "Justification must be at least 10 characters.")]
    [MaxLength(1000)]
    public string Justification { get; init; } = string.Empty;
}

/// <summary>
/// A single CPT code entry within a <see cref="CptLibraryRefreshRequestDto"/>.
/// </summary>
public sealed record CptCodeEntryDto
{
    /// <summary>AMA CPT alphanumeric code value (e.g. <c>"99213"</c>, <c>"80053"</c>). Max 10 chars.</summary>
    [Required]
    [MaxLength(10)]
    public string CptCode { get; init; } = string.Empty;

    /// <summary>Full clinical description of the procedure. Max 500 chars.</summary>
    [Required]
    [MaxLength(500)]
    public string Description { get; init; } = string.Empty;

    /// <summary>CPT category label (e.g. "Evaluation &amp; Management"). Max 50 chars.</summary>
    [Required]
    [MaxLength(50)]
    public string Category { get; init; } = string.Empty;

    /// <summary>Date from which this code became effective in the AMA CPT standard.</summary>
    [Required]
    public DateOnly EffectiveDate { get; init; }

    /// <summary>
    /// Optional expiration date; <c>null</c> for codes that are still active.
    /// When supplied, the corresponding <c>CptCodeLibrary</c> row is set to <c>is_active = false</c>.
    /// </summary>
    public DateOnly? ExpirationDate { get; init; }
}

/// <summary>
/// Request body for <c>PUT /api/coding/cpt/library/refresh</c> (US_048, AC-4, Admin only).
///
/// Carries the new quarterly CPT library dataset.  The upsert is transactional so a
/// partial failure rolls back completely (DR-029).
/// </summary>
public sealed record CptLibraryRefreshRequestDto
{
    /// <summary>
    /// Semantic version identifier for this library release (e.g. <c>"2026.Q3"</c>).
    /// Max 20 characters.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// The complete set of CPT codes for this library version.
    /// Codes absent from this list will have <c>is_active</c> set to <c>false</c>.
    /// Must not be empty.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one code entry is required.")]
    public IReadOnlyList<CptCodeEntryDto> Codes { get; init; } = [];
}

// ─────────────────────────────────────────────────────────────────────────────
// Response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Individual CPT procedure code suggestion returned in a <see cref="CptMappingResponseDto"/>
/// (US_048, AC-1, AC-3).
/// </summary>
public sealed record CptCodeDto
{
    /// <summary>MedicalCode primary key in the database.</summary>
    public Guid? MedicalCodeId { get; init; }

    /// <summary>CPT code value (e.g. <c>"99213"</c>).</summary>
    public string CodeValue { get; init; } = string.Empty;

    /// <summary>Clinical description of the procedure.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>AI model confidence in the range [0.0, 1.0].</summary>
    public float ConfidenceScore { get; init; }

    /// <summary>
    /// AI-generated or override justification text explaining the mapping (AC-1, HIPAA audit).
    /// </summary>
    public string Justification { get; init; } = string.Empty;

    /// <summary>Rank among sibling codes for the same procedure (1 = highest relevance, AC-3).</summary>
    public int? RelevanceRank { get; init; }

    /// <summary>
    /// Workflow status string: <c>"Pending"</c>, <c>"Approved"</c>, or <c>"Overridden"</c>.
    /// Matches the <c>CptCodeStatus</c> discriminated union on the frontend.
    /// </summary>
    public string Status { get; init; } = "Pending";

    /// <summary>
    /// <c>true</c> when this code is part of a billable bundle (US_048 AC-3 edge case).
    /// Full bundle detection is implemented in task_004_ai_cpt_prompt_rag; returns <c>false</c>
    /// until that task is complete.
    /// </summary>
    public bool IsBundled { get; init; }

    /// <summary>
    /// Groups CPT codes belonging to the same bundle; <c>null</c> for non-bundled codes.
    /// Populated by task_004_ai_cpt_prompt_rag once bundle detection logic is in place.
    /// </summary>
    public Guid? BundleGroupId { get; init; }

    /// <summary>Current CPT library version against which this code was validated.</summary>
    public string? LibraryVersion { get; init; }

    /// <summary>Current revalidation lifecycle state string (mirrors <c>RevalidationStatus</c> enum).</summary>
    public string? ValidationStatus { get; init; }
}

/// <summary>
/// Response returned by <c>GET /api/coding/cpt/pending/{patientId}</c> (US_048, AC-1, AC-3).
///
/// Contains AI-suggested CPT procedure codes awaiting staff review, sorted by relevance rank.
/// </summary>
public sealed record CptMappingResponseDto
{
    /// <summary>Patient this result set belongs to.</summary>
    public Guid PatientId { get; init; }

    /// <summary>All pending CPT codes suggested for this patient, ranked by relevance.</summary>
    public IReadOnlyList<CptCodeDto> Codes { get; init; } = [];

    /// <summary>UTC timestamp of the most recent CPT coding run for this patient.</summary>
    public DateTime? LastCodingRunAt { get; init; }
}

/// <summary>
/// Result returned by <c>PUT /api/coding/cpt/approve</c> (US_048, AC-1).
/// </summary>
public sealed record CptApproveResultDto
{
    /// <summary>Primary key of the approved <c>MedicalCode</c> row.</summary>
    public Guid MedicalCodeId { get; init; }

    /// <summary>The approved CPT code value.</summary>
    public string CodeValue { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the approval was recorded.</summary>
    public DateTime ApprovedAt { get; init; }

    /// <summary>Confirmation message for the UI.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Result returned by <c>PUT /api/coding/cpt/override</c> (US_048, edge case — incorrect mapping).
/// </summary>
public sealed record CptOverrideResultDto
{
    /// <summary>Primary key of the overridden <c>MedicalCode</c> row.</summary>
    public Guid MedicalCodeId { get; init; }

    /// <summary>The new CPT code value that replaced the AI suggestion.</summary>
    public string ReplacementCode { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the override was recorded.</summary>
    public DateTime OverriddenAt { get; init; }

    /// <summary>Confirmation message for the UI.</summary>
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Summary result returned by <c>PUT /api/coding/cpt/library/refresh</c> (US_048, AC-4).
/// </summary>
public sealed record CptLibraryRefreshResultDto
{
    /// <summary>Library version that was just applied.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Number of new CPT code entries inserted.</summary>
    public int CodesAdded { get; init; }

    /// <summary>Number of existing entries marked as inactive in this refresh.</summary>
    public int CodesDeactivated { get; init; }

    /// <summary>Number of pending <c>MedicalCode</c> records revalidated as part of this refresh (AC-4).</summary>
    public int PendingCodesRevalidated { get; init; }

    /// <summary>UTC timestamp when the refresh completed.</summary>
    public DateTime RefreshedAt { get; init; }
}

/// <summary>
/// Summary result returned by <c>POST /api/coding/cpt/library/revalidate</c> (US_048, AC-4).
/// </summary>
public sealed record CptRevalidationResultDto
{
    /// <summary>Total number of pending <c>MedicalCode</c> records examined.</summary>
    public int TotalExamined { get; init; }

    /// <summary>Number of records confirmed as still valid against the current CPT library.</summary>
    public int MarkedValid { get; init; }

    /// <summary>Number of records flagged as <c>DeprecatedReplaced</c> (code expired).</summary>
    public int MarkedInvalid { get; init; }

    /// <summary>Number of records set to <c>PendingReview</c> (code not found in library).</summary>
    public int MarkedPendingReview { get; init; }

    /// <summary>UTC timestamp when the revalidation run completed.</summary>
    public DateTime RevalidatedAt { get; init; }
}
