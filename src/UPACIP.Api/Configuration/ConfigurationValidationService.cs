using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Configuration;

namespace UPACIP.Api.Configuration;

/// <summary>
/// Hosted service that validates all required configuration sections at startup
/// and fails fast if critical settings are missing or invalid (US_101, AC-4).
///
/// Fail-fast behavior:
///   Throws <see cref="InvalidOperationException"/> before the HTTP pipeline starts
///   accepting requests, so operators see a clear error message with all validation
///   failures in a single exception rather than cascading NullReferenceExceptions later.
///
/// Security:
///   Validates <em>presence</em> of credential fields (non-empty check) but never
///   logs their values, preventing secrets from appearing in log aggregation tools
///   (OWASP A09).
/// </summary>
public sealed class ConfigurationValidationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConfigurationValidationService> _logger;

    public ConfigurationValidationService(
        IServiceProvider serviceProvider,
        ILogger<ConfigurationValidationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        ValidateSection<DatabaseOptions>(errors, opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.ConnectionString))
                errors.Add($"{DatabaseOptions.SectionName}:ConnectionString is required");
            if (opts.MaxPoolSize <= 0)
                errors.Add($"{DatabaseOptions.SectionName}:MaxPoolSize must be > 0");
            if (opts.CommandTimeoutSeconds <= 0)
                errors.Add($"{DatabaseOptions.SectionName}:CommandTimeoutSeconds must be > 0");
        });

        ValidateSection<RedisOptions>(errors, opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.ConnectionString))
                errors.Add($"{RedisOptions.SectionName}:ConnectionString is required");
            if (opts.DefaultTtlMinutes <= 0)
                errors.Add($"{RedisOptions.SectionName}:DefaultTtlMinutes must be > 0");
        });

        ValidateSection<AiGatewayOptions>(errors, opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.PrimaryProviderBaseUrl))
                errors.Add($"{AiGatewayOptions.SectionName}:PrimaryProviderBaseUrl is required");
            if (string.IsNullOrWhiteSpace(opts.FallbackProviderBaseUrl))
                errors.Add($"{AiGatewayOptions.SectionName}:FallbackProviderBaseUrl is required");
            if (opts.MaxTokensPerRequest <= 0)
                errors.Add($"{AiGatewayOptions.SectionName}:MaxTokensPerRequest must be > 0");
        });

        ValidateSection<EmailOptions>(errors, opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.SmtpHost))
                errors.Add($"{EmailOptions.SectionName}:SmtpHost is required");
            if (string.IsNullOrWhiteSpace(opts.FromAddress))
                errors.Add($"{EmailOptions.SectionName}:FromAddress is required");
        });

        ValidateSection<SmsOptions>(errors, opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.AccountSid))
                errors.Add($"{SmsOptions.SectionName}:AccountSid is required");
            if (string.IsNullOrWhiteSpace(opts.FromNumber))
                errors.Add($"{SmsOptions.SectionName}:FromNumber is required");
        });

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                _logger.LogCritical(
                    "CONFIGURATION_VALIDATION_FAILURE: {ValidationError}", error);
            }

            throw new InvalidOperationException(
                $"Configuration validation failed with {errors.Count} error(s): " +
                string.Join("; ", errors));
        }

        _logger.LogInformation(
            "CONFIGURATION_VALIDATED: All {SectionCount} configuration sections passed startup validation",
            5);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ValidateSection<T>(List<string> errors, Action<T> validate)
        where T : class, new()
    {
        try
        {
            var options = _serviceProvider.GetRequiredService<IOptions<T>>().Value;
            validate(options);
        }
        catch (OptionsValidationException ex)
        {
            errors.Add($"{typeof(T).Name}: {ex.Message}");
        }
        catch (InvalidOperationException)
        {
            errors.Add($"{typeof(T).Name}: Section missing or cannot be bound");
        }
    }
}
