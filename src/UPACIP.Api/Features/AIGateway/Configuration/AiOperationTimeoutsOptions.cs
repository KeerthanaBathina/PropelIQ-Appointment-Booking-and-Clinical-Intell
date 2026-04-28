namespace UPACIP.Api.Features.AIGateway.Configuration;

/// <summary>
/// Per-operation AI request timeout thresholds (US_081 task_002, AC-2, AC-3).
///
/// <para>
/// Bind from the <c>"AiOperationTimeouts"</c> configuration section:
/// <code>
/// "AiOperationTimeouts": {
///   "DocumentParsing":      25000,
///   "MedicalCoding":         4000,
///   "ConversationalIntake":  3000,
///   "Default":              10000
/// }
/// </code>
/// </para>
///
/// <para>
/// Timeouts are enforced by creating a linked <see cref="System.Threading.CancellationTokenSource"/>
/// with the per-operation value. The primary provider must respond within the timeout; if it does not,
/// the Polly fallback pipeline has the remaining budget to try the secondary provider.
/// </para>
/// </summary>
public sealed class AiOperationTimeoutsOptions
{
    public const string SectionName = "AiOperationTimeouts";

    /// <summary>
    /// Hard timeout (ms) for document parsing AI requests.
    /// Must be ≤ 25,000ms to provide a 5s buffer inside the 30s SLA (AC-2).
    /// Default: 25,000ms.
    /// </summary>
    public int DocumentParsing { get; set; } = 25_000;

    /// <summary>
    /// Hard timeout (ms) for medical coding AI requests.
    /// Must be ≤ 4,000ms to meet the 5s SLA with fallback budget (AC-3).
    /// Default: 4,000ms.
    /// </summary>
    public int MedicalCoding { get; set; } = 4_000;

    /// <summary>
    /// Hard timeout (ms) for conversational intake AI requests.
    /// Default: 3,000ms.
    /// </summary>
    public int ConversationalIntake { get; set; } = 3_000;

    /// <summary>
    /// Fallback timeout (ms) used when no per-operation value is configured.
    /// Default: 10,000ms.
    /// </summary>
    public int Default { get; set; } = 10_000;
}
