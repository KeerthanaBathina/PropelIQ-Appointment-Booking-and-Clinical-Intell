using System.Text;
using System.Text.Json;

namespace UPACIP.LoadTests.Reports;

/// <summary>
/// A single step in the scalability ramp test (US_082 task_002, AC-4).
/// </summary>
public sealed record ScalabilityStep(
    int    ConcurrentUsers,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double ThroughputRps,
    double ErrorRatePercent,
    double DegradationPercent,
    bool   SlaPassed);

/// <summary>
/// Collects scalability step results and exposes a pass/fail verdict (US_082 task_002, AC-4).
/// </summary>
public sealed class ScalabilityReport
{
    public List<ScalabilityStep> Steps         { get; } = new();
    public bool                  OverallPassed { get; set; }
    public DateTime              GeneratedAt   { get; } = DateTime.UtcNow;
}

/// <summary>
/// Generates a human-readable summary table and JSON report from
/// a <see cref="ScalabilityReport"/> (US_082 task_002, AC-4).
///
/// <para>
/// <b>Output (console):</b>
/// <code>
/// ┌──────────────────────────────────────────────────────────────────────────────┐
/// │ Scalability Report — UPACIP Load Test Suite                                  │
/// ├────────────┬──────────┬──────────┬──────────┬──────────┬─────────┬──────────┤
/// │ Users      │ P50 (ms) │ P95 (ms) │ P99 (ms) │ RPS      │ Errors% │ Degrad%  │
/// ├────────────┼──────────┼──────────┼──────────┼──────────┼─────────┼──────────┤
/// │ 100        │ 45       │ 120      │ 210      │ 980      │ 0.00    │ 0.00     │
/// │ …          │ …        │ …        │ …        │ …        │ …       │ …        │
/// └────────────┴──────────┴──────────┴──────────┴──────────┴─────────┴──────────┘
/// Overall: PASS
/// </code>
/// </para>
/// </summary>
public static class ScalabilityReportGenerator
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the summary table to the console and saves a JSON report file.
    /// </summary>
    /// <param name="report">Populated scalability report.</param>
    /// <param name="reportFolder">Directory to save the JSON file.</param>
    public static void GenerateAndPrint(ScalabilityReport report, string reportFolder)
    {
        PrintTable(report);
        SaveJson(report, reportFolder);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void PrintTable(ScalabilityReport report)
    {
        Console.WriteLine();
        Console.WriteLine("┌─────────────────────────────────────────────────────────────────────────────────┐");
        Console.WriteLine("│  Scalability Report — UPACIP Load Test Suite                                    │");
        Console.WriteLine("├────────────┬──────────┬──────────┬──────────┬──────────┬─────────┬─────────────┤");
        Console.WriteLine("│  Users     │ P50 (ms) │ P95 (ms) │ P99 (ms) │ RPS      │ Errors% │ Degradation │");
        Console.WriteLine("├────────────┼──────────┼──────────┼──────────┼──────────┼─────────┼─────────────┤");

        foreach (var step in report.Steps)
        {
            var verdict     = step.SlaPassed ? "PASS" : "FAIL";
            var degradStr   = step.DegradationPercent >= 0
                ? $"+{step.DegradationPercent:F1}%"
                : $"{step.DegradationPercent:F1}%";

            Console.WriteLine(
                $"│  {step.ConcurrentUsers,-9} │ {step.P50Ms,-8:F0} │ {step.P95Ms,-8:F0} │ " +
                $"{step.P99Ms,-8:F0} │ {step.ThroughputRps,-8:F1} │ " +
                $"{step.ErrorRatePercent,-7:F2} │ {degradStr,-8}  {verdict,-4} │");
        }

        Console.WriteLine("└────────────┴──────────┴──────────┴──────────┴──────────┴─────────┴─────────────┘");
        Console.WriteLine();

        var overall   = report.OverallPassed ? "PASS ✓" : "FAIL ✗";
        var failSteps = report.Steps.Where(s => !s.SlaPassed).ToList();

        Console.WriteLine($"Overall result: {overall}");

        if (failSteps.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Failing steps:");
            foreach (var step in failSteps)
            {
                var hints = BuildBottleneckHints(step);
                Console.WriteLine($"  • {step.ConcurrentUsers} users — {hints}");
            }
        }

        Console.WriteLine($"Generated at: {report.GeneratedAt:O}");
        Console.WriteLine();
    }

    private static string BuildBottleneckHints(ScalabilityStep step)
    {
        var hints = new List<string>();

        if (step.DegradationPercent >= 10.0)
            hints.Add($"P95 degraded {step.DegradationPercent:F1}% (threshold 10%)");

        if (step.ErrorRatePercent >= 1.0)
            hints.Add($"Error rate {step.ErrorRatePercent:F2}% exceeds 1% SLA");

        if (step.P95Ms >= 3000)
            hints.Add("P95 > 3000ms — possible DB pool or connection pressure");

        if (step.ThroughputRps < 100)
            hints.Add("Low RPS — check circuit-breaker state and AI queue depth");

        return hints.Count > 0 ? string.Join("; ", hints) : "Unknown bottleneck — check metrics";
    }

    private static void SaveJson(ScalabilityReport report, string reportFolder)
    {
        try
        {
            Directory.CreateDirectory(reportFolder);
            var path    = Path.Combine(reportFolder, $"scalability_report_{report.GeneratedAt:yyyyMMdd_HHmmss}.json");
            var payload = new
            {
                GeneratedAt    = report.GeneratedAt,
                OverallPassed  = report.OverallPassed,
                Steps          = report.Steps.Select(s => new
                {
                    s.ConcurrentUsers,
                    s.P50Ms,
                    s.P95Ms,
                    s.P99Ms,
                    s.ThroughputRps,
                    s.ErrorRatePercent,
                    s.DegradationPercent,
                    s.SlaPassed,
                }).ToArray(),
            };

            File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOpts));
            Console.WriteLine($"Report saved: {path}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: could not save JSON report — {ex.Message}");
        }
    }
}
