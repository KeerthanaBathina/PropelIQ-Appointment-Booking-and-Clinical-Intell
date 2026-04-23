namespace UPACIP.Service.Coding;

/// <summary>
/// Validates ICD-10 and CPT code combinations against payer-specific and CMS-default rules,
/// detects claim denial risks, and validates bundling rule compliance
/// (US_051, AC-1, AC-2, AC-4, FR-066).
/// </summary>
public interface IPayerRuleValidationService
{
    /// <summary>
    /// Validates the patient's current code set against payer-specific rules and CMS defaults.
    /// Persists any detected violations to <c>payer_rule_violations</c> and returns the
    /// aggregate validation result (US_051 AC-1, AC-2).
    ///
    /// When payer-specific rules are not found for <paramref name="payerId"/>, CMS-default
    /// rules are applied and <c>is_cms_default = true</c> is set in the result (EC-1).
    /// </summary>
    Task<PayerValidationRunResult> ValidateCodeCombinationsAsync(
        Guid patientId,
        string? payerId,
        CancellationToken ct = default);

    /// <summary>
    /// Checks a set of CPT code values against NCCI bundling edits (US_051 AC-4).
    /// Returns the list of violated code pairs; an empty list means the code set passed.
    /// </summary>
    Task<IReadOnlyList<BundlingViolationItem>> ValidateBundlingRulesAsync(
        IReadOnlyList<string> codeValues,
        CancellationToken ct = default);

    /// <summary>
    /// Records a staff decision resolving a payer rule conflict (US_051 edge case).
    /// Throws <see cref="KeyNotFoundException"/> when the violation is not found.
    /// </summary>
    Task<ConflictResolutionRunResult> RecordConflictResolutionAsync(
        ConflictResolutionRecord resolution,
        Guid actingUserId,
        CancellationToken ct = default);
}
