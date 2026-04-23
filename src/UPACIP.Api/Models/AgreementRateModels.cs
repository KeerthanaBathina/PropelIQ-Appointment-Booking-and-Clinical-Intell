using System.Text.Json.Serialization;

namespace UPACIP.Api.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Agreement Rate DTOs (US_050, AC-1, AC-2, FR-067)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Daily and rolling agreement-rate metrics for the admin dashboard (US_050 AC-2).
/// When <see cref="MeetsMinimumThreshold"/> is <c>false</c> the UI must display
/// "Not enough data" rather than the rate values (EC-1, requires 50+ verified codes).
/// </summary>
public sealed record AgreementRateDto
{
    [JsonPropertyName("calculation_date")]
    public DateOnly CalculationDate { get; init; }

    /// <summary>Fraction of AI codes approved without override on this day [0.0, 100.0].</summary>
    [JsonPropertyName("daily_agreement_rate")]
    public decimal DailyAgreementRate { get; init; }

    /// <summary>
    /// Rolling 30-day weighted-average agreement rate [0.0, 100.0].
    /// <c>null</c> when fewer than 7 days of history are available.
    /// </summary>
    [JsonPropertyName("rolling_30day_rate")]
    public decimal? Rolling30DayRate { get; init; }

    [JsonPropertyName("total_codes_verified")]
    public int TotalCodesVerified { get; init; }

    [JsonPropertyName("codes_approved_without_override")]
    public int CodesApprovedWithoutOverride { get; init; }

    [JsonPropertyName("codes_overridden")]
    public int CodesOverridden { get; init; }

    [JsonPropertyName("codes_partially_overridden")]
    public int CodesPartiallyOverridden { get; init; }

    /// <summary>
    /// <c>true</c> when <see cref="TotalCodesVerified"/> ≥ 50 and the rate figures are
    /// statistically meaningful (US_050 EC-1).  When <c>false</c> display "Not enough data".
    /// </summary>
    [JsonPropertyName("meets_minimum_threshold")]
    public bool MeetsMinimumThreshold { get; init; }

    /// <summary>System target rate — always 98.0 (AC-1).</summary>
    [JsonPropertyName("target_rate")]
    public decimal TargetRate => 98.0m;
}

/// <summary>
/// A single coding discrepancy record — AI suggestion vs staff selection (US_050 AC-3, FR-068).
/// </summary>
public sealed record CodingDiscrepancyDto
{
    [JsonPropertyName("discrepancy_id")]
    public Guid DiscrepancyId { get; init; }

    [JsonPropertyName("patient_id")]
    public Guid PatientId { get; init; }

    [JsonPropertyName("ai_suggested_code")]
    public string AiSuggestedCode { get; init; } = string.Empty;

    [JsonPropertyName("staff_selected_code")]
    public string StaffSelectedCode { get; init; } = string.Empty;

    /// <summary>"Icd10" or "Cpt".</summary>
    [JsonPropertyName("code_type")]
    public string CodeType { get; init; } = string.Empty;

    /// <summary>"FullOverride" | "PartialOverride" | "MultipleCodes".</summary>
    [JsonPropertyName("discrepancy_type")]
    public string DiscrepancyType { get; init; } = string.Empty;

    [JsonPropertyName("override_justification")]
    public string? OverrideJustification { get; init; }

    [JsonPropertyName("detected_at")]
    public DateTimeOffset DetectedAt { get; init; }
}

/// <summary>
/// Alert emitted when the daily agreement rate drops below 98 % (US_050 AC-4).
/// Includes a pattern summary so admins can identify systemic issues.
/// </summary>
public sealed record AgreementAlertDto
{
    [JsonPropertyName("alert_date")]
    public DateOnly AlertDate { get; init; }

    [JsonPropertyName("current_rate")]
    public decimal CurrentRate { get; init; }

    /// <summary>Always 98.0.</summary>
    [JsonPropertyName("target_rate")]
    public decimal TargetRate => 98.0m;

    /// <summary>
    /// Top discrepancy patterns driving the shortfall — up to 5 entries ordered by frequency.
    /// Each string describes a category, e.g. "FullOverride (12 occurrences)".
    /// </summary>
    [JsonPropertyName("disagreement_patterns")]
    public IReadOnlyList<string> DisagreementPatterns { get; init; } = [];
}
