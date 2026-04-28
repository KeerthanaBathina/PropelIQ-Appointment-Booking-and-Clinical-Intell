using System.Net.Http.Headers;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using UPACIP.LoadTests.Infrastructure;
using UPACIP.LoadTests.Reports;

namespace UPACIP.LoadTests.Scenarios;

/// <summary>
/// NBomber scalability ramp scenario that validates linear scalability with &lt;10%
/// P95 degradation from 100 to 1000 concurrent users (US_082 task_002, AC-4).
///
/// <para>
/// <b>Load steps:</b> 100 â†’ 250 â†’ 500 â†’ 750 â†’ 1000 virtual users.
/// Each step runs for <c>RampStepDurationSeconds</c> (default 60 s) followed by a
/// <c>RampCooldownSeconds</c> (default 15 s) cooldown to allow pool recovery.
/// </para>
///
/// <para>
/// <b>Metric:</b> P95 latency for the mixed booking + search workload.
/// </para>
///
/// <para>
/// <b>Assertion:</b> <c>degradation = (stepP95 âˆ’ baselineP95) / baselineP95 Ã— 100 &lt; 10%</c>
/// at every step (AC-4).
/// </para>
/// </summary>
public static class ScalabilityRampScenario
{
    private static readonly int[] LoadSteps = [100, 250, 500, 750, 1000];

    /// <summary>
    /// Executes sequential load steps, records P95 latency at each step, and
    /// returns a <see cref="ScalabilityReport"/> with degradation analysis.
    /// </summary>
    public static async Task<ScalabilityReport> RunAsync(
        LoadTestConfiguration config,
        string                reportFolder,
        CancellationToken     ct = default)
    {
        var report   = new ScalabilityReport();
        double? baselineP95 = null;

        foreach (int users in LoadSteps)
        {
            Console.WriteLine($"\n[ScalabilityRamp] Starting step: {users} concurrent usersâ€¦");

            var stepResult = await RunStepAsync(config, users, reportFolder, ct);

            var p95          = stepResult.ScenarioStats.FirstOrDefault()?.Ok.Latency.Percent95 ?? 0;
            var rps          = stepResult.ScenarioStats.FirstOrDefault()?.Ok.Request.RPS ?? 0;
            var failCount    = stepResult.ScenarioStats.Sum(s => s.Fail.Request.Count);
            var totalOk      = stepResult.ScenarioStats.Sum(s => s.Ok.Request.Count);
            var totalAll     = totalOk + failCount;
            var errorPct     = totalAll > 0 ? (double)failCount / totalAll * 100.0 : 0.0;
            var degradation  = 0.0;

            baselineP95 ??= p95;
            if (baselineP95 > 0)
                degradation = (p95 - baselineP95.Value) / baselineP95.Value * 100.0;

            var slaPassed = degradation < config.MaxDegradationPercent && errorPct < config.MaxErrorRatePercent;

            report.Steps.Add(new ScalabilityStep(
                ConcurrentUsers:   users,
                P95Ms:             p95,
                P50Ms:             stepResult.ScenarioStats.FirstOrDefault()?.Ok.Latency.Percent50 ?? 0,
                P99Ms:             stepResult.ScenarioStats.FirstOrDefault()?.Ok.Latency.Percent99 ?? 0,
                ThroughputRps:     rps,
                ErrorRatePercent:  errorPct,
                DegradationPercent: degradation,
                SlaPassed:         slaPassed));

            Console.WriteLine(
                $"[ScalabilityRamp] Step {users} users â†’ P95={p95:F0}ms  " +
                $"RPS={rps:F1}  Errors={errorPct:F2}%  Degradation={degradation:F1}%  " +
                $"SLA={( slaPassed ? "PASS" : "FAIL")}");

            // Cooldown between steps to allow the connection pool to recover.
            if (users != LoadSteps[^1])
            {
                Console.WriteLine($"[ScalabilityRamp] Cooldown {config.RampCooldownSeconds}sâ€¦");
                await Task.Delay(TimeSpan.FromSeconds(config.RampCooldownSeconds), ct);
            }
        }

        report.OverallPassed = report.Steps.All(s => s.SlaPassed);
        return report;
    }

    // â”€â”€ Private helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static Task<NBomber.Contracts.Stats.NodeStats> RunStepAsync(
        LoadTestConfiguration config,
        int                   users,
        string                reportFolder,
        CancellationToken     ct)
    {
        return Task.Run(() =>
        {
            var httpClient = BuildClient(config);

            var providerIds = Enumerable.Range(1, 50)
                .Select(i => new Guid(i, 0, 0, new byte[8]).ToString())
                .ToArray();

            var rng = new Random();

            var scenario = Scenario.Create($"ramp_{users}_users", async ctx =>
            {
                var providerId = providerIds[rng.Next(providerIds.Length)];
                var date       = DateTime.UtcNow.Date.AddDays(rng.Next(0, 7)).ToString("yyyy-MM-dd");

                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{config.BaseUrl}/api/appointments/slots?providerId={providerId}&date={date}");
                using var resp = await httpClient.SendAsync(req, CancellationToken.None);

                return resp.IsSuccessStatusCode
                    ? Response.Ok()
                    : Response.Fail(message: resp.StatusCode.ToString());
            })
            .WithLoadSimulations(
                Simulation.Inject(
                    rate:     Math.Max(1, users / 10),
                    interval: TimeSpan.FromSeconds(1),
                    during:   TimeSpan.FromSeconds(config.RampStepDurationSeconds)));

            return NBomberRunner
                .RegisterScenarios(scenario)
                .WithReportFormats(ReportFormat.Html)
                .WithReportFolder(reportFolder)
                .WithReportFileName($"scalability_step_{users}_users")
                .Run();
        }, ct);
    }

    private static HttpClient BuildClient(LoadTestConfiguration config)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (!string.IsNullOrEmpty(config.AuthTokenStaff))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config.AuthTokenStaff);
        return client;
    }
}



