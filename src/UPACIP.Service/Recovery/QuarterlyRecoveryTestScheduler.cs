using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Recovery.Models;

namespace UPACIP.Service.Recovery;

/// <summary>
/// BackgroundService that tracks quarterly disaster recovery test obligations and emits
/// structured alerts when tests are due or overdue (US_095, AC-3, DR-026).
///
/// <para>
/// Runs once per day at midnight UTC. On each cycle:
/// <list type="number">
///   <item>Computes the current calendar quarter (Q1–Q4).</item>
///   <item>Queries <c>RecoveryTestRecords</c> for a passing test in the current quarter.</item>
///   <item>Determines <c>DueSoon</c> or <c>Overdue</c> status and emits the appropriate log event.</item>
///   <item>If an <c>IBackupRestorationTestService</c> result was recorded externally (US_089 task_003),
///        compares <see cref="RecoveryTestRecord.ActualRecoveryTime"/> against the RTO target
///        and logs success or a warning.</item>
/// </list>
/// </para>
/// </summary>
public sealed class QuarterlyRecoveryTestScheduler : BackgroundService
{
    private readonly IServiceScopeFactory                       _scopeFactory;
    private readonly IOptionsMonitor<RecoveryTargetOptions>     _optionsMonitor;
    private readonly ILogger<QuarterlyRecoveryTestScheduler>    _logger;

    public QuarterlyRecoveryTestScheduler(
        IServiceScopeFactory                        scopeFactory,
        IOptionsMonitor<RecoveryTargetOptions>      optionsMonitor,
        ILogger<QuarterlyRecoveryTestScheduler>     logger)
    {
        _scopeFactory   = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _logger         = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "QUARTERLY_TEST_SCHEDULER_STARTED: AlertDaysBefore={Days}, RTO={Rto}min.",
            _optionsMonitor.CurrentValue.QuarterlyTestAlertDaysBefore,
            _optionsMonitor.CurrentValue.RtoMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDailyCheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QUARTERLY_TEST_SCHEDULER_ERROR: Unhandled exception during daily check.");
            }

            await WaitUntilNextMidnightAsync(stoppingToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunDailyCheckAsync(CancellationToken ct)
    {
        var opts    = _optionsMonitor.CurrentValue;
        var now     = DateTime.UtcNow;
        var quarter = RecoveryTargetMonitoringService.GetCurrentQuarter(now);
        var deadline = RecoveryTargetMonitoringService.GetQuarterEndUtc(now);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // (a) Check current quarter test status.
        var latestTest = await db.RecoveryTestRecords
            .Where(t => t.Quarter == quarter)
            .OrderByDescending(t => t.ExecutedAtUtc)
            .FirstOrDefaultAsync(ct);

        var status = RecoveryTargetMonitoringService.DetermineQuarterlyStatus(
            latestTest?.Passed == true ? latestTest.ExecutedAtUtc : null,
            deadline,
            opts,
            now);

        var daysRemaining = (deadline - now).TotalDays;
        var daysOverdue   = (now - deadline).TotalDays;

        switch (status)
        {
            case "DueSoon":
                _logger.LogWarning(
                    "RECOVERY_TEST_DUE: Quarter={Quarter}, DaysRemaining={Days:F0}, Deadline={Deadline:O}",
                    quarter, daysRemaining, deadline);
                break;

            case "Overdue":
                _logger.LogCritical(
                    "RECOVERY_TEST_OVERDUE: Quarter={Quarter}, DaysOverdue={Days:F0}, Deadline={Deadline:O}",
                    quarter, daysOverdue, deadline);
                break;

            default:
                _logger.LogDebug(
                    "QUARTERLY_TEST_STATUS: Quarter={Quarter}, Status={Status}, DaysRemaining={Days:F0}",
                    quarter, status, daysRemaining);
                break;
        }

        // (b) For any passing tests recorded since the last check, compare actual
        //     recovery time against the RTO target.
        var since = now.Date; // Tests recorded today
        var recentTests = await db.RecoveryTestRecords
            .Where(t => t.CreatedAtUtc >= since)
            .ToListAsync(ct);

        foreach (var test in recentTests)
        {
            if (!test.Passed)
            {
                _logger.LogWarning(
                    "RECOVERY_TEST_FAILED: TestId={TestId}, Quarter={Quarter}, Type={Type}, " +
                    "Reason={Reason}, ExecutedBy={ExecutedBy}",
                    test.Id, test.Quarter, test.TestType,
                    test.FailureReason ?? "unspecified", test.ExecutedBy);
                continue;
            }

            var rtoTarget = TimeSpan.FromMinutes(opts.RtoMinutes);

            if (test.ActualRecoveryTime <= rtoTarget)
            {
                _logger.LogInformation(
                    "RECOVERY_TEST_PASSED: TestId={TestId}, Quarter={Quarter}, " +
                    "RecoveryTime={Minutes:F0}min within RTO={Target}min, " +
                    "RowsVerified={Rows}, ExecutedBy={ExecutedBy}",
                    test.Id, test.Quarter,
                    test.ActualRecoveryTime.TotalMinutes, opts.RtoMinutes,
                    test.RowsVerified, test.ExecutedBy);
            }
            else
            {
                _logger.LogWarning(
                    "RECOVERY_TEST_SLOW: TestId={TestId}, Quarter={Quarter}, " +
                    "RecoveryTime={Minutes:F0}min exceeds RTO={Target}min, " +
                    "RowsVerified={Rows}, ExecutedBy={ExecutedBy}",
                    test.Id, test.Quarter,
                    test.ActualRecoveryTime.TotalMinutes, opts.RtoMinutes,
                    test.RowsVerified, test.ExecutedBy);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static async Task WaitUntilNextMidnightAsync(CancellationToken ct)
    {
        var now       = DateTime.UtcNow;
        var nextCheck = now.Date.AddDays(1); // midnight UTC tomorrow
        var delay     = nextCheck - now;
        if (delay <= TimeSpan.Zero) delay = TimeSpan.FromHours(1);

        await Task.Delay(delay, ct);
    }
}
