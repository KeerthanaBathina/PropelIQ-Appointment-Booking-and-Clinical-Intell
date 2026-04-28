namespace UPACIP.Service.Validation.Models;

/// <summary>
/// Structured outcome of an ICD-10 or CPT code validation request (US_085 AC-4, DR-015).
///
/// Validation flow:
/// <list type="number">
///   <item>Exact-match lookup against <c>icd10_code_library</c> / <c>cpt_code_library</c>.</item>
///   <item>If found and active: <see cref="IsValid"/> = <c>true</c>, no suggestions.</item>
///   <item>If found but deprecated: <see cref="IsDeprecated"/> = <c>true</c>;
///         <see cref="SuggestedAlternatives"/> contains up to 5 semantically similar,
///         non-deprecated alternatives from pgvector cosine-similarity search.</item>
///   <item>If not found: both flags are <c>false</c>;
///         <see cref="SuggestedAlternatives"/> contains similarity-based candidates.</item>
/// </list>
/// </summary>
public sealed record CodeValidationResult
{
    /// <summary>
    /// <c>true</c> when the submitted code exists in the current library version and is not deprecated.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// <c>true</c> when the code exists but is deprecated / inactive in the library.
    /// Implies <see cref="IsValid"/> is <c>false</c>.
    /// </summary>
    public bool IsDeprecated { get; init; }

    /// <summary>Human-readable validation outcome suitable for display in UI error messages.</summary>
    public string ValidationMessage { get; init; } = string.Empty;

    /// <summary>The code value that was submitted and validated (normalised to uppercase).</summary>
    public string SubmittedCode { get; init; } = string.Empty;

    /// <summary>The code system that was validated — <c>"ICD-10"</c> or <c>"CPT"</c>.</summary>
    public string SubmittedCodeSystem { get; init; } = string.Empty;

    /// <summary>
    /// Up to 5 suggested alternative codes ordered by semantic similarity descending.
    /// Empty when <see cref="IsValid"/> is <c>true</c> (no suggestions needed).
    /// For deprecated ICD-10 codes, the direct <c>replacement_code</c> (if set) is
    /// prepended to the similarity results.
    /// </summary>
    public IReadOnlyList<CodeSuggestion> SuggestedAlternatives { get; init; } =
        Array.Empty<CodeSuggestion>();
}
