namespace UPACIP.Service.Documents;

/// <summary>
/// Configuration settings for the document parsing queue dispatcher (US_039 EC-2).
///
/// Bind from the <c>DocumentParsing</c> configuration section (appsettings.json).
/// </summary>
public sealed class DocumentParsingDispatcherSettings
{
    /// <summary>Configuration section key in appsettings.json.</summary>
    public const string SectionName = "DocumentParsing";

    /// <summary>
    /// Maximum number of parsing jobs allowed to run concurrently.
    /// Default 5 — balances AI throughput against API rate limits (US_039 EC-2).
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 5;

    /// <summary>
    /// How often (seconds) the dispatcher polls Redis for new queue entries.
    /// Default 5 s — short enough for near-real-time queue drain without busy-polling.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum in-process Polly retry attempts per job before the document is
    /// marked <c>Failed</c> (US_039 AC-4, AC-5). Default 3.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;
}
