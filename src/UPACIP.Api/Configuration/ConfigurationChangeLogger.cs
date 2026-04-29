using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Configuration;

namespace UPACIP.Api.Configuration;

/// <summary>
/// Hosted service that subscribes to <see cref="IOptionsMonitor{T}"/> change events
/// for all centralized configuration sections and logs a structured audit event
/// whenever a setting is modified at runtime (US_101, AC-3).
///
/// AC-3 propagation:
///   When an admin saves a modified <c>appsettings.json</c>, ASP.NET Core's
///   <c>PhysicalFileProvider</c> detects the file change via <c>IChangeToken</c>
///   (near-instant on Windows via <c>ReadDirectoryChangesW</c>).
///   <see cref="IOptionsMonitor{T}.OnChange"/> fires on each affected options class,
///   and this service logs a <c>CONFIGURATION_CHANGED</c> event with the section name
///   and an ISO-8601 timestamp to the Serilog pipeline.
///
/// Security:
///   Only the configuration <em>section name</em> is logged — never the values
///   themselves — to prevent credentials or PII from appearing in log aggregation
///   tools (OWASP A09).
///
/// Memory management:
///   <see cref="IOptionsMonitor{T}.OnChange"/> returns an <see cref="IDisposable"/>
///   registration token.  All tokens are collected and disposed on
///   <see cref="IHostedService.StopAsync"/> to prevent leaks.
/// </summary>
public sealed class ConfigurationChangeLogger : IHostedService, IDisposable
{
    private readonly ILogger<ConfigurationChangeLogger> _logger;
    private readonly List<IDisposable> _changeListeners = new();

    public ConfigurationChangeLogger(
        IOptionsMonitor<DatabaseOptions> databaseOptions,
        IOptionsMonitor<RedisOptions> redisOptions,
        IOptionsMonitor<AiGatewayOptions> aiGatewayOptions,
        IOptionsMonitor<EmailOptions> emailOptions,
        IOptionsMonitor<SmsOptions> smsOptions,
        ILogger<ConfigurationChangeLogger> logger)
    {
        _logger = logger;

        AddListener(databaseOptions.OnChange((_, name) =>
            LogConfigChange(DatabaseOptions.SectionName, name)));

        AddListener(redisOptions.OnChange((_, name) =>
            LogConfigChange(RedisOptions.SectionName, name)));

        AddListener(aiGatewayOptions.OnChange((_, name) =>
            LogConfigChange(AiGatewayOptions.SectionName, name)));

        AddListener(emailOptions.OnChange((_, name) =>
            LogConfigChange(EmailOptions.SectionName, name)));

        AddListener(smsOptions.OnChange((_, name) =>
            LogConfigChange(SmsOptions.SectionName, name)));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "CONFIGURATION_MONITOR_STARTED: Monitoring {SectionCount} configuration sections for runtime changes",
            _changeListeners.Count);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        foreach (var listener in _changeListeners)
        {
            listener.Dispose();
        }

        _changeListeners.Clear();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void AddListener(IDisposable? token)
    {
        // OnChange returns null when the options source does not support change
        // notifications (e.g. in-memory providers used in unit tests). Skip silently.
        if (token is not null)
        {
            _changeListeners.Add(token);
        }
    }

    private void LogConfigChange(string sectionName, string? namedOption)
    {
        _logger.LogWarning(
            "CONFIGURATION_CHANGED: Section '{SectionName}' (named option: '{NamedOption}') " +
            "was modified at {Timestamp}. IOptionsMonitor consumers will receive updated values.",
            sectionName,
            namedOption ?? "(default)",
            DateTime.UtcNow.ToString("o"));
    }
}
