namespace UPACIP.Service.AiSafety;

/// <summary>
/// Per-request context produced by <see cref="IPiiRedactionService.RedactPii"/> that holds
/// the token-to-PII mapping table required for re-association of AI results with the
/// original patient context (US_074 task_001, AC-4).
///
/// <para>
/// Token format: <c>[CATEGORY_N]</c> where CATEGORY is one of NAME, DOB, SSN, PHONE,
/// EMAIL, ADDRESS and N is a sequential counter per category per request
/// (e.g. <c>[NAME_1]</c>, <c>[EMAIL_1]</c>, <c>[ADDRESS_2]</c>).
/// </para>
///
/// <para>
/// This object is scoped to the lifetime of a single AI Gateway request.
/// It is NEVER persisted to the database or written to any log.
/// The <see cref="Mappings"/> dictionary must never leave the current request boundary.
/// </para>
/// </summary>
public sealed class PiiRedactionContext
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises an empty context for the given patient.
    /// </summary>
    /// <param name="patientId">
    /// Internal patient identifier used to re-associate AI results with the correct
    /// patient record via <c>PatientId</c> only — never via PII fields (AC-4).
    /// </param>
    public PiiRedactionContext(Guid patientId)
    {
        PatientId = patientId;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Internal patient identifier for re-association of AI results (AC-4).
    /// This is the ONLY patient reference transmitted to downstream services.
    /// </summary>
    public Guid PatientId { get; }

    /// <summary>
    /// Mapping of placeholder tokens to their original PII values.
    /// Keys follow the <c>[CATEGORY_N]</c> format.
    /// This dictionary must NEVER be logged, persisted, or transmitted externally.
    /// </summary>
    public Dictionary<string, string> Mappings { get; } = new();

    /// <summary>
    /// Counts of redacted tokens by PII category (e.g. <c>{"SSN": 1, "EMAIL": 1}</c>).
    /// Safe to log — contains only counts, not actual PII values.
    /// </summary>
    public Dictionary<string, int> RedactedCounts { get; } = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when at least one PII token was redacted.
    /// </summary>
    public bool HasRedactions => Mappings.Count > 0;

    /// <summary>
    /// Creates the next sequential placeholder token for the given category and
    /// records the original value in <see cref="Mappings"/>.
    /// </summary>
    /// <param name="category">PII category label (NAME, DOB, SSN, PHONE, EMAIL, ADDRESS).</param>
    /// <param name="originalValue">The actual PII value being replaced.</param>
    /// <returns>Placeholder token such as <c>[NAME_1]</c>.</returns>
    public string AddMapping(string category, string originalValue)
    {
        RedactedCounts.TryGetValue(category, out int current);
        int    next  = current + 1;
        string token = $"[{category}_{next}]";

        Mappings[token]          = originalValue;
        RedactedCounts[category] = next;

        return token;
    }
}
