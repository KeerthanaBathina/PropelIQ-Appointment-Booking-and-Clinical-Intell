using Microsoft.Extensions.Configuration;

namespace UPACIP.LoadTests.Infrastructure;

/// <summary>
/// Strongly-typed configuration for all load test scenarios (US_082 task_002).
///
/// <para>Values are read from <c>appsettings.LoadTest.json</c> and can be overridden via
/// environment variables using the standard <c>LoadTest__BaseUrl</c> convention.</para>
/// </summary>
public sealed class LoadTestConfiguration
{
    // ── Configuration section key ─────────────────────────────────────────────
    public const string SectionName = "LoadTest";

    // ── Connection ────────────────────────────────────────────────────────────

    /// <summary>Base URL of the UPACIP API under test. Default: http://localhost:5000</summary>
    public string BaseUrl { get; set; } = "http://localhost:5000";

    /// <summary>Pre-generated JWT for a user with the Patient role.</summary>
    public string AuthTokenPatient { get; set; } = string.Empty;

    /// <summary>Pre-generated JWT for a user with the Staff role.</summary>
    public string AuthTokenStaff { get; set; } = string.Empty;

    // ── Duration / ramp ───────────────────────────────────────────────────────

    /// <summary>Default scenario duration in seconds. Default: 120.</summary>
    public int DefaultDurationSeconds { get; set; } = 120;

    /// <summary>Warm-up duration (lower load) before full concurrency is applied. Default: 10.</summary>
    public int WarmUpSeconds { get; set; } = 10;

    /// <summary>Ramp-up duration to reach target concurrency. Default: 30.</summary>
    public int RampUpSeconds { get; set; } = 30;

    /// <summary>Duration of each step in the scalability ramp scenario. Default: 60.</summary>
    public int RampStepDurationSeconds { get; set; } = 60;

    /// <summary>Cooldown period between scalability ramp steps. Default: 15.</summary>
    public int RampCooldownSeconds { get; set; } = 15;

    // ── SLA thresholds ────────────────────────────────────────────────────────

    /// <summary>Booking P95 latency SLA threshold (ms). Default: 2000.</summary>
    public int BookingP95Ms { get; set; } = 2000;

    /// <summary>Search P95 latency SLA threshold (ms). Default: 1000.</summary>
    public int SearchP95Ms { get; set; } = 1000;

    /// <summary>Dashboard P95 latency SLA threshold (ms). Default: 3000.</summary>
    public int DashboardP95Ms { get; set; } = 3000;

    /// <summary>Maximum allowed error rate across all scenarios (%). Default: 1.0.</summary>
    public double MaxErrorRatePercent { get; set; } = 1.0;

    /// <summary>Maximum allowed P95 degradation between scalability ramp steps (%). Default: 10.0.</summary>
    public double MaxDegradationPercent { get; set; } = 10.0;

    // ── Database (seeder) ─────────────────────────────────────────────────────

    /// <summary>PostgreSQL connection string used by <see cref="TestDataSeeder"/>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads configuration from <c>appsettings.LoadTest.json</c> (in the executable directory)
    /// and environment variable overrides, then binds to <see cref="LoadTestConfiguration"/>.
    /// </summary>
    public static LoadTestConfiguration Load()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.LoadTest.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var section = config.GetSection(SectionName);
        var result  = new LoadTestConfiguration();
        section.Bind(result);

        // Also bind SlaThresholds section for convenience.
        config.GetSection("SlaThresholds").Bind(result);

        result.ConnectionString =
            config.GetConnectionString("DefaultConnection") ?? string.Empty;

        return result;
    }
}
