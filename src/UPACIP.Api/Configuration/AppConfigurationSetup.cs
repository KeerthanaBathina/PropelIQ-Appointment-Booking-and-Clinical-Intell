using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UPACIP.Service.Configuration;

namespace UPACIP.Api.Configuration;

/// <summary>
/// Extension methods for registering the centralized configuration hierarchy and
/// strongly-typed options bindings (US_101, AC-3, AC-4).
/// </summary>
public static class AppConfigurationSetup
{
    /// <summary>
    /// Adds ASP.NET Core's hierarchical JSON configuration sources with
    /// <c>reloadOnChange: true</c> for runtime hot-reload (AC-3):
    ///
    /// <list type="number">
    ///   <item>
    ///     <c>appsettings.json</c> — base settings; required (fail-fast on missing file).
    ///   </item>
    ///   <item>
    ///     <c>appsettings.{Environment}.json</c> — environment-specific overrides
    ///     (Development/Staging/Production); optional.
    ///   </item>
    ///   <item>
    ///     Environment variables prefixed with <c>UPACIP_</c> — for secrets and
    ///     deployment-time overrides; higher priority than JSON files (OWASP A07).
    ///     Double-underscore <c>__</c> is the hierarchy separator, e.g.
    ///     <c>UPACIP_Database__ConnectionString</c> maps to <c>Database:ConnectionString</c>.
    ///   </item>
    ///   <item>
    ///     Command-line arguments — highest priority (added by <c>WebApplicationBuilder</c>
    ///     by default; not re-added here).
    ///   </item>
    /// </list>
    ///
    /// The JSON sources are already registered by <c>WebApplicationBuilder</c> with default
    /// paths.  This method is intentionally a no-op for those and instead registers the
    /// <c>UPACIP_</c>-prefixed environment variable source which is NOT added by default.
    /// Calling this at the earliest possible point ensures the prefix-scoped env vars
    /// override both JSON layers correctly (AC-3, AC-4).
    /// </summary>
    public static ConfigurationManager AddHierarchicalConfiguration(
        this ConfigurationManager configuration,
        IHostEnvironment environment)
    {
        // appsettings.json and appsettings.{Environment}.json are already added by
        // WebApplicationBuilder.  We add them again with explicit paths here only
        // if needed; skipping to avoid double-registration.

        // Layer 3: UPACIP_-prefixed environment variables — overrides JSON sources.
        // UPACIP_Database__ConnectionString → Database:ConnectionString
        configuration.AddEnvironmentVariables(prefix: "UPACIP_");

        return configuration;
    }

    /// <summary>
    /// Binds all centralized configuration sections to strongly-typed options classes
    /// and registers them with the DI container (AC-4).
    ///
    /// Each section is bound using <c>IOptions&lt;T&gt;</c> /
    /// <c>IOptionsMonitor&lt;T&gt;</c>; the monitor variant is used by
    /// <see cref="ConfigurationChangeLogger"/> to detect runtime changes.
    /// </summary>
    public static IServiceCollection AddConfigurationOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(
            configuration.GetSection(DatabaseOptions.SectionName));

        services.Configure<RedisOptions>(
            configuration.GetSection(RedisOptions.SectionName));

        services.Configure<AiGatewayOptions>(
            configuration.GetSection(AiGatewayOptions.SectionName));

        services.Configure<EmailOptions>(
            configuration.GetSection(EmailOptions.SectionName));

        services.Configure<SmsOptions>(
            configuration.GetSection(SmsOptions.SectionName));

        return services;
    }
}
