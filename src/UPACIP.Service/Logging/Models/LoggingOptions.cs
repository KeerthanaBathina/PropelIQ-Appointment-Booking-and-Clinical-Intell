namespace UPACIP.Service.Logging.Models;

/// <summary>
/// Strongly-typed binding for the "AppLogging" section in appsettings.json.
///
/// Configures the structured logging pipeline: Seq primary sink, rolling file backup,
/// console fallback, and correlation ID header name (US_095, AC-1, AC-4, TR-028, TR-031,
/// NFR-035).
///
/// Required appsettings.json section:
/// <code>
/// "AppLogging": {
///   "SeqServerUrl":        "http://localhost:5341",
///   "SeqApiKey":           null,
///   "FileLogPath":         "D:\\Logs\\UPACIP\\app-.log",
///   "FileRetainedDays":    30,
///   "FileSizeLimitBytes":  104857600,
///   "EnableConsoleSink":   true,
///   "FallbackQueueCapacity": 10000,
///   "CorrelationIdHeader": "X-Correlation-ID"
/// }
/// </code>
/// </summary>
public sealed class LoggingOptions
{
    public const string SectionName = "AppLogging";

    /// <summary>Seq Community Edition endpoint for structured log ingestion (primary sink).</summary>
    public string SeqServerUrl { get; init; } = "http://localhost:5341";

    /// <summary>Optional API key for Seq authentication. Null disables authentication.</summary>
    public string? SeqApiKey { get; init; }

    /// <summary>Rolling file path pattern for the backup log sink (daily roll via {Date} token).</summary>
    public string FileLogPath { get; init; } = @"D:\Logs\UPACIP\app-.log";

    /// <summary>Number of daily log files to retain before auto-deletion (default: 30 days).</summary>
    public int FileRetainedDays { get; init; } = 30;

    /// <summary>Maximum single log file size in bytes before rolling (default: 100 MB).</summary>
    public long FileSizeLimitBytes { get; init; } = 104_857_600L; // 100 MB

    /// <summary>
    /// Whether to write to the console sink.
    /// Always-on in Development; used as immediate fallback when Seq is unavailable (edge case 1).
    /// </summary>
    public bool EnableConsoleSink { get; init; } = true;

    /// <summary>
    /// Maximum number of log entries held in the in-memory fallback buffer when Seq is
    /// unreachable. Oldest entries are dropped if capacity is exceeded (edge case 1).
    /// </summary>
    public int FallbackQueueCapacity { get; init; } = 10_000;

    /// <summary>HTTP header name used to read and propagate the correlation ID (AC-1).</summary>
    public string CorrelationIdHeader { get; init; } = "X-Correlation-ID";
}
