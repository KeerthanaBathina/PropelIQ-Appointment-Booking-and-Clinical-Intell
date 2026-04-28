namespace UPACIP.Service.Monitoring;

/// <summary>
/// Contract for sliding-window HTTP error rate monitoring against the 0.1% threshold (US_083 task_001, AC-4, NFR-031).
/// </summary>
public interface IErrorRateMonitor
{
    /// <summary>
    /// Records the outcome of a completed HTTP request into the sliding window.
    /// Thread-safe; non-blocking.
    /// </summary>
    /// <param name="isError">
    /// <see langword="true"/> when the response status code is in the 4xx–5xx range.
    /// </param>
    /// <param name="category">
    /// Workflow category: <c>booking</c>, <c>intake</c>, <c>coding</c>, or <c>other</c>.
    /// </param>
    void RecordRequestOutcome(bool isError, string category);

    /// <summary>
    /// Returns the current error rate as a fraction in the range [0.0, 1.0].
    /// Returns 0.0 when no requests have been recorded in the window.
    /// </summary>
    double GetCurrentErrorRate();

    /// <summary>
    /// Returns <see langword="true"/> when the current error rate exceeds the configured
    /// threshold (<see cref="Models.MonitoringOptions.ErrorRateThresholdPercent"/> / 100).
    /// </summary>
    bool IsThresholdExceeded();

    /// <summary>
    /// Returns per-category error and total request counts within the current sliding window
    /// for diagnostic triage (AC-4).
    /// </summary>
    IReadOnlyDictionary<string, (int Errors, int Total)> GetErrorRateByCategory();
}
