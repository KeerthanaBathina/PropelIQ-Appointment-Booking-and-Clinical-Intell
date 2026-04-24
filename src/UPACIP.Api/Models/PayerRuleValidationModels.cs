using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Api.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Payer Rule Validation DTOs (US_051, AC-1, AC-2, AC-3, AC-4)
// ─────────────────────────────────────────────────────────────────────────────

// ── Response DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// Aggregate response for GET /api/coding/payer-rules/{patientId} (US_051 AC-1, AC-2).
/// Contains all validation results, denial risks, and bundling violations for the
/// patient's current code set.
/// </summary>
public sealed record PayerValidationResponse
{
    /// <summary>Payer name displayed in the UI alert header. <c>null</c> when CMS default rules apply.</summary>
    [JsonPropertyName("payer_name")]
    public string? PayerName { get; init; }

    /// <summary>
    /// <c>true</c> when no payer-specific rules were found and CMS default rules were applied (US_051 EC-1).
    /// The UI should display a "CMS Default" badge when this is true.
    /// </summary>
    [JsonPropertyName("is_cms_default")]
    public bool IsCmsDefault { get; init; }

    /// <summary>Payer rule violations for the patient's current code set.</summary>
    [JsonPropertyName("validation_results")]
    public IReadOnlyList<PayerValidationResultDto> ValidationResults { get; init; } = [];

    /// <summary>Claim denial risk assessments for the patient's code combinations.</summary>
    [JsonPropertyName("denial_risks")]
    public IReadOnlyList<ClaimDenialRiskDto> DenialRisks { get; init; } = [];

    /// <summary>NCCI bundling rule violations. Empty list when no violations detected.</summary>
    [JsonPropertyName("bundling_violations")]
    public IReadOnlyList<BundlingRuleResultDto> BundlingViolations { get; init; } = [];
}

/// <summary>
/// A single payer rule violation with severity and corrective actions (US_051 AC-2).
/// </summary>
public sealed record PayerValidationResultDto
{
    [JsonPropertyName("violation_id")]
    public Guid ViolationId { get; init; }

    [JsonPropertyName("rule_id")]
    public Guid RuleId { get; init; }

    /// <summary>Violation urgency: "Error", "Warning", or "Info".</summary>
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = string.Empty;

    /// <summary>Human-readable description of the violated rule.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Payer's stated denial reason.</summary>
    [JsonPropertyName("denial_reason")]
    public string DenialReason { get; init; } = string.Empty;

    /// <summary>Code values involved in the violation.</summary>
    [JsonPropertyName("affected_codes")]
    public IReadOnlyList<string> AffectedCodes { get; init; } = [];

    /// <summary>Suggested actions staff can take to resolve the violation.</summary>
    [JsonPropertyName("corrective_actions")]
    public IReadOnlyList<CorrectiveActionDto> CorrectiveActions { get; init; } = [];

    /// <summary><c>true</c> when CMS default rules were applied (no payer-specific rule found).</summary>
    [JsonPropertyName("is_cms_default")]
    public bool IsCmsDefault { get; init; }
}

/// <summary>
/// A suggested corrective action for resolving a payer rule violation (US_051 AC-2).
/// </summary>
public sealed record CorrectiveActionDto
{
    /// <summary>
    /// Type of corrective action: "AlternativeCode", "AddModifier", "AddDocumentation", "RemoveCode".
    /// Used by the UI to select the appropriate icon and dialog.
    /// </summary>
    [JsonPropertyName("action_type")]
    public string ActionType { get; init; } = string.Empty;

    /// <summary>Human-readable description of the action to take.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The alternative/replacement code when <see cref="ActionType"/> is "AlternativeCode".
    /// <c>null</c> for other action types.
    /// </summary>
    [JsonPropertyName("suggested_code")]
    public string? SuggestedCode { get; init; }
}

/// <summary>
/// Claim denial risk assessment for a code or code combination (US_051 AC-2).
/// </summary>
public sealed record ClaimDenialRiskDto
{
    [JsonPropertyName("risk_level")]
    public string RiskLevel { get; init; } = string.Empty;  // "high" | "medium" | "low"

    /// <summary>The two-code pair that triggered the denial risk flag.</summary>
    [JsonPropertyName("code_pair")]
    public IReadOnlyList<string> CodePair { get; init; } = [];

    [JsonPropertyName("denial_reason")]
    public string DenialReason { get; init; } = string.Empty;

    /// <summary>Historical denial rate for this code combination [0.0, 1.0].</summary>
    [JsonPropertyName("historical_denial_rate")]
    public decimal? HistoricalDenialRate { get; init; }

    [JsonPropertyName("corrective_actions")]
    public IReadOnlyList<CorrectiveActionDto> CorrectiveActions { get; init; } = [];
}

/// <summary>
/// NCCI bundling rule violation for a CPT code pair (US_051 AC-4).
/// </summary>
public sealed record BundlingRuleResultDto
{
    [JsonPropertyName("column1_code")]
    public string Column1Code { get; init; } = string.Empty;

    [JsonPropertyName("column2_code")]
    public string Column2Code { get; init; } = string.Empty;

    /// <summary>Edit type: "MutuallyExclusive", "ComponentPart", or "Standard".</summary>
    [JsonPropertyName("edit_type")]
    public string EditType { get; init; } = string.Empty;

    /// <summary>Modifier codes that can override this bundling restriction (empty when not applicable).</summary>
    [JsonPropertyName("applicable_modifiers")]
    public IReadOnlyList<string> ApplicableModifiers { get; init; } = [];

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

// ── Multi-code assignment request/response DTOs ───────────────────────────────

/// <summary>
/// A single code entry within a multi-code assignment request (US_051 AC-3).
/// </summary>
public sealed record CodeAssignmentEntryDto
{
    [Required(ErrorMessage = "code_value is required.")]
    [MaxLength(20, ErrorMessage = "code_value must not exceed 20 characters.")]
    [JsonPropertyName("code_value")]
    public string CodeValue { get; init; } = string.Empty;

    [Required(ErrorMessage = "code_type is required.")]
    [JsonPropertyName("code_type")]
    public CodeType CodeType { get; init; }

    [Required(ErrorMessage = "description is required.")]
    [MaxLength(500, ErrorMessage = "description must not exceed 500 characters.")]
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "justification must not exceed 1 000 characters.")]
    [JsonPropertyName("justification")]
    public string Justification { get; init; } = string.Empty;

    /// <summary>Billing priority (1 = highest). Defaults to the position within the request list.</summary>
    [JsonPropertyName("sequence_order")]
    public int SequenceOrder { get; init; } = 0;
}

/// <summary>
/// Request body for POST /api/coding/multi-assign (US_051 AC-3).
/// </summary>
public sealed record MultiCodeAssignmentRequest
{
    [Required]
    [JsonPropertyName("patient_id")]
    public Guid PatientId { get; init; }

    [Required]
    [MinLength(1, ErrorMessage = "At least one code entry is required.")]
    [MaxLength(50, ErrorMessage = "Maximum 50 codes per request.")]
    [JsonPropertyName("codes")]
    public IReadOnlyList<CodeAssignmentEntryDto> Codes { get; init; } = [];

    /// <summary>Optional client idempotency key (NFR-034). Max 64 chars.</summary>
    [MaxLength(64)]
    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// A single assigned code in the multi-assignment response.
/// </summary>
public sealed record AssignedCodeDto
{
    [JsonPropertyName("code_id")]
    public Guid CodeId { get; init; }

    [JsonPropertyName("code_value")]
    public string CodeValue { get; init; } = string.Empty;

    [JsonPropertyName("code_type")]
    public string CodeType { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("sequence_order")]
    public int SequenceOrder { get; init; }

    [JsonPropertyName("payer_validation_status")]
    public string PayerValidationStatus { get; init; } = string.Empty;
}

/// <summary>
/// Response for POST /api/coding/multi-assign (US_051 AC-3).
/// </summary>
public sealed record MultiCodeAssignmentResponse
{
    [JsonPropertyName("patient_id")]
    public Guid PatientId { get; init; }

    [JsonPropertyName("assigned_codes")]
    public IReadOnlyList<AssignedCodeDto> AssignedCodes { get; init; } = [];
}

// ── Bundling validation request ───────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/coding/validate-bundling (US_051 AC-4).
/// </summary>
public sealed record BundlingValidationRequest
{
    [Required]
    [JsonPropertyName("patient_id")]
    public Guid PatientId { get; init; }

    [Required]
    [MinLength(2, ErrorMessage = "At least 2 code values are required to check bundling.")]
    [MaxLength(50, ErrorMessage = "Maximum 50 code values per request.")]
    [JsonPropertyName("code_values")]
    public IReadOnlyList<string> CodeValues { get; init; } = [];
}

/// <summary>
/// Response for POST /api/coding/validate-bundling (US_051 AC-4).
/// </summary>
public sealed record BundlingValidationResponse
{
    [JsonPropertyName("patient_id")]
    public Guid PatientId { get; init; }

    /// <summary><c>true</c> when no bundling violations were found.</summary>
    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    [JsonPropertyName("violations")]
    public IReadOnlyList<BundlingRuleResultDto> Violations { get; init; } = [];
}

// ── Conflict resolution request ───────────────────────────────────────────────

/// <summary>
/// Request body for POST /api/coding/resolve-conflict (US_051 edge case).
/// Records the staff decision when a payer rule conflicts with clinical documentation.
/// </summary>
public sealed record ConflictResolutionRequest
{
    [Required]
    [JsonPropertyName("violation_id")]
    public Guid ViolationId { get; init; }

    [Required]
    [JsonPropertyName("patient_id")]
    public Guid PatientId { get; init; }

    /// <summary>
    /// Staff decision: "AcceptCorrective", "UseClinicCode", "UsePayerCode", "FlagManualReview".
    /// </summary>
    [Required]
    [JsonPropertyName("resolution_type")]
    public string ResolutionType { get; init; } = string.Empty;

    [Required(ErrorMessage = "justification is required.")]
    [MinLength(10, ErrorMessage = "justification must be at least 10 characters.")]
    [MaxLength(1000, ErrorMessage = "justification must not exceed 1 000 characters.")]
    [JsonPropertyName("justification")]
    public string Justification { get; init; } = string.Empty;

    /// <summary>
    /// Code value selected by staff (when resolution_type is UseClinicCode or UsePayerCode).
    /// Max 20 characters.
    /// </summary>
    [MaxLength(20)]
    [JsonPropertyName("selected_code")]
    public string? SelectedCode { get; init; }
}

/// <summary>
/// Response for POST /api/coding/resolve-conflict.
/// </summary>
public sealed record ConflictResolutionResponse
{
    [JsonPropertyName("violation_id")]
    public Guid ViolationId { get; init; }

    [JsonPropertyName("resolution_status")]
    public string ResolutionStatus { get; init; } = string.Empty;

    [JsonPropertyName("resolved_at")]
    public DateTime ResolvedAt { get; init; }
}
