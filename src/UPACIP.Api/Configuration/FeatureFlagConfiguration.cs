using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UPACIP.Service.FeatureFlags;

namespace UPACIP.Api.Configuration;

/// <summary>
/// Extension methods for feature flag service registration and configuration source
/// setup (US_101, AC-1, AC-2).
/// </summary>
public static class FeatureFlagConfiguration
{
    /// <summary>
    /// Registers <see cref="IFeatureFlagService"/> and binds <see cref="FeatureFlagOptions"/>
    /// from the <c>FeatureFlags</c> configuration section.
    ///
    /// <see cref="FeatureFlagService"/> is registered as singleton because:
    /// - <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> is itself singleton.
    /// - The service maintains an in-memory fallback cache across its lifetime.
    /// </summary>
    public static IServiceCollection AddFeatureFlagServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FeatureFlagOptions>(
            configuration.GetSection(FeatureFlagOptions.SectionName));

        services.AddSingleton<IFeatureFlagService, FeatureFlagService>();

        return services;
    }

    /// <summary>
    /// Adds <c>config/featureflags.json</c> and the environment-specific override file
    /// (<c>config/featureflags.{Environment}.json</c>) to the configuration pipeline
    /// with <c>reloadOnChange: true</c>.
    ///
    /// Change detection (AC-2):
    ///   ASP.NET Core's <c>JsonConfigurationProvider</c> uses
    ///   <c>PhysicalFileProvider</c> + <c>IChangeToken</c> polling (default 4 s on
    ///   Unix; near-instant on Windows via <c>ReadDirectoryChangesW</c>).
    ///   Combined with <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>,
    ///   flag changes propagate to <see cref="IFeatureFlagService"/> within ~30–60 s
    ///   of the file being saved — satisfying the "within 1 minute" requirement.
    ///
    /// File strategy:
    ///   <list type="bullet">
    ///     <item><c>featureflags.json</c> — required; application fails to start if missing (fail-fast).</item>
    ///     <item><c>featureflags.{Env}.json</c> — optional; merges/overrides the base file.
    ///       In Development all AI flags are enabled for testing.</item>
    ///   </list>
    /// </summary>
    public static ConfigurationManager AddFeatureFlagConfiguration(
        this ConfigurationManager configuration,
        IHostEnvironment environment)
    {
        var configDir = Path.Combine(AppContext.BaseDirectory, "config");

        configuration.AddJsonFile(
            path: Path.Combine(configDir, "featureflags.json"),
            optional: false,
            reloadOnChange: true);

        configuration.AddJsonFile(
            path: Path.Combine(configDir, $"featureflags.{environment.EnvironmentName}.json"),
            optional: true,
            reloadOnChange: true);

        return configuration;
    }
}
