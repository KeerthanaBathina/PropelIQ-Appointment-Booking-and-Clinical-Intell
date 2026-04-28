using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using UPACIP.LoadTests.Infrastructure;
using UPACIP.LoadTests.Reports;
using UPACIP.LoadTests.Scenarios;

// â”€â”€ Entry point â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
//
// Usage:
//   dotnet run --project tests/UPACIP.LoadTests -- --scenario all
//   dotnet run --project tests/UPACIP.LoadTests -- --scenario booking
//   dotnet run --project tests/UPACIP.LoadTests -- --scenario search
//   dotnet run --project tests/UPACIP.LoadTests -- --scenario dashboard
//   dotnet run --project tests/UPACIP.LoadTests -- --scenario mixed
//   dotnet run --project tests/UPACIP.LoadTests -- --scenario scalability
//   dotnet run --project tests/UPACIP.LoadTests -- --seed
//   dotnet run --project tests/UPACIP.LoadTests -- --cleanup
//
// Exit codes: 0 = all assertions passed, 1 = one or more assertions failed.

var scenario  = ParseArg(args, "--scenario") ?? "all";
var reportDir = ParseArg(args, "--report-dir")
    ?? Path.Combine(AppContext.BaseDirectory, "Reports");

// â”€â”€ Load configuration â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
var config = LoadTestConfiguration.Load();

Console.WriteLine($"[UPACIP LoadTests] scenario={scenario} reportDir={reportDir}");
Console.WriteLine($"[UPACIP LoadTests] BaseUrl={config.BaseUrl}");

// â”€â”€ Seed / cleanup shorthand â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
if (args.Contains("--seed"))
{
    await using var seeder = new TestDataSeeder(config.ConnectionString, verbose: true);
    await seeder.SeedAsync();
    return 0;
}

if (args.Contains("--cleanup"))
{
    await using var seeder = new TestDataSeeder(config.ConnectionString, verbose: true);
    await seeder.CleanupAsync();
    return 0;
}

// â”€â”€ Run scenarios â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
bool allPassed = true;

if (scenario is "booking" or "all")
{
    Console.WriteLine("\n[UPACIP LoadTests] Running: Booking load scenario (1000 users, 120 s)â€¦");
    var bookingScenario = BookingLoadScenario.Create(config);

    var stats = NBomberRunner
        .RegisterScenarios(bookingScenario)
        .WithReportFormats(ReportFormat.Html, ReportFormat.Txt)
        .WithReportFolder(reportDir)
        .WithReportFileName("booking_load_test")
        .Run();

    var p95 = stats.ScenarioStats.FirstOrDefault()?.Ok.Latency.Percent95 ?? double.MaxValue;
    var passed = p95 < config.BookingP95Ms;
    if (!passed)
    {
        Console.Error.WriteLine($"FAIL: Booking P95={p95:F0}ms >= SLA {config.BookingP95Ms}ms");
        allPassed = false;
    }
    else
    {
        Console.WriteLine($"PASS: Booking P95={p95:F0}ms < {config.BookingP95Ms}ms SLA");
    }
}

if (scenario is "search" or "all")
{
    Console.WriteLine("\n[UPACIP LoadTests] Running: Search load scenario (1000 users, 120 s)â€¦");
    var searchScenario = SearchLoadScenario.Create(config);

    var stats = NBomberRunner
        .RegisterScenarios(searchScenario)
        .WithReportFormats(ReportFormat.Html, ReportFormat.Txt)
        .WithReportFolder(reportDir)
        .WithReportFileName("search_load_test")
        .Run();

    var p95 = stats.ScenarioStats.FirstOrDefault()?.Ok.Latency.Percent95 ?? double.MaxValue;
    var passed = p95 < config.SearchP95Ms;
    if (!passed)
    {
        Console.Error.WriteLine($"FAIL: Search P95={p95:F0}ms >= SLA {config.SearchP95Ms}ms");
        allPassed = false;
    }
    else
    {
        Console.WriteLine($"PASS: Search P95={p95:F0}ms < {config.SearchP95Ms}ms SLA");
    }
}

if (scenario is "dashboard" or "all")
{
    Console.WriteLine("\n[UPACIP LoadTests] Running: Dashboard load scenario (1000 users, 120 s)â€¦");
    var dashScenario = DashboardLoadScenario.Create(config);

    var stats = NBomberRunner
        .RegisterScenarios(dashScenario)
        .WithReportFormats(ReportFormat.Html, ReportFormat.Txt)
        .WithReportFolder(reportDir)
        .WithReportFileName("dashboard_load_test")
        .Run();

    var p95 = stats.ScenarioStats.FirstOrDefault()?.Ok.Latency.Percent95 ?? double.MaxValue;
    var passed = p95 < config.DashboardP95Ms;
    if (!passed)
    {
        Console.Error.WriteLine($"FAIL: Dashboard P95={p95:F0}ms >= SLA {config.DashboardP95Ms}ms");
        allPassed = false;
    }
    else
    {
        Console.WriteLine($"PASS: Dashboard P95={p95:F0}ms < {config.DashboardP95Ms}ms SLA");
    }
}

if (scenario is "mixed" or "all")
{
    Console.WriteLine("\n[UPACIP LoadTests] Running: Mixed workload scenario (1000 users, 180 s)â€¦");
    var mixedScenarios = MixedWorkloadScenario.Create(config);

    var stats = NBomberRunner
        .RegisterScenarios(mixedScenarios)
        .WithReportFormats(ReportFormat.Html, ReportFormat.Txt)
        .WithReportFolder(reportDir)
        .WithReportFileName("mixed_workload_test")
        .Run();

    foreach (var s in stats.ScenarioStats)
    {
        var totalReq  = s.Ok.Request.Count + s.Fail.Request.Count;
        var errorPct  = totalReq > 0 ? (double)s.Fail.Request.Count / totalReq * 100.0 : 0.0;
        var passed    = errorPct < config.MaxErrorRatePercent;

        if (!passed)
        {
            Console.Error.WriteLine(
                $"FAIL: Mixed/{s.ScenarioName} error rate {errorPct:F2}% >= {config.MaxErrorRatePercent}%");
            allPassed = false;
        }
        else
        {
            Console.WriteLine(
                $"PASS: Mixed/{s.ScenarioName} error rate {errorPct:F2}% < {config.MaxErrorRatePercent}%");
        }
    }
}

if (scenario is "scalability" or "all")
{
    Console.WriteLine("\n[UPACIP LoadTests] Running: Scalability ramp scenario (100â†’1000 users)â€¦");

    var report = await ScalabilityRampScenario.RunAsync(config, reportDir);
    ScalabilityReportGenerator.GenerateAndPrint(report, reportDir);

    if (!report.OverallPassed)
    {
        Console.Error.WriteLine("FAIL: Scalability ramp did not meet <10% degradation SLA.");
        allPassed = false;
    }
    else
    {
        Console.WriteLine("PASS: Scalability ramp â€” <10% degradation at all load steps.");
    }
}

// â”€â”€ Final verdict â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
Console.WriteLine();
Console.WriteLine(allPassed
    ? "â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•"
    : "â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");
Console.WriteLine(allPassed
    ? "  OVERALL RESULT: PASS âœ“"
    : "  OVERALL RESULT: FAIL âœ—");
Console.WriteLine("â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");

return allPassed ? 0 : 1;

// â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
static string? ParseArg(string[] args, string flag)
{
    var idx = Array.IndexOf(args, flag);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

