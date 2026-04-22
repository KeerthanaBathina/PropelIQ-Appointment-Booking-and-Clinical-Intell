using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Models;

/// <summary>
/// Request body for verifying or correcting a single extracted clinical data row (US_041 AC-4).
///
/// Two operations share this contract:
///   <c>action = "verified"</c>  — staff confirms the extracted value is accurate.
///   <c>action = "corrected"</c> — staff replaces the extracted value before accepting it.
///
/// When <c>action = "corrected"</c>, at least one of the correction payload fields must be
/// non-null to prevent no-op correction requests.
/// </summary>
public sealed record VerifyExtractedDataRequest
{
    /// <summary>
    /// Verification action — must be <c>"verified"</c> or <c>"corrected"</c>.
    /// </summary>
    [Required]
    [RegularExpression("^(verified|corrected)$",
        ErrorMessage = "Action must be 'verified' or 'corrected'.")]
    public string Action { get; init; } = string.Empty;

    /// <summary>
    /// Optional replacement normalized value for correction flows.
    /// Required when <c>action = "corrected"</c> and no other correction field is supplied.
    /// </summary>
    [MaxLength(1000)]
    public string? CorrectedNormalizedValue { get; init; }

    /// <summary>
    /// Optional replacement raw text for correction flows.
    /// </summary>
    [MaxLength(2000)]
    public string? CorrectedRawText { get; init; }

    /// <summary>
    /// Optional replacement unit of measure for correction flows (e.g. "mg", "mmHg").
    /// </summary>
    [MaxLength(50)]
    public string? CorrectedUnit { get; init; }

    /// <summary>
    /// Optional staff-facing note for auditing the reason the correction was made.
    /// Not persisted as PHI — stored only in structured audit logs.
    /// </summary>
    [MaxLength(500)]
    public string? CorrectionNote { get; init; }
}
