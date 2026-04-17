# Task - task_002_be_centralized_configuration_management

## Requirement Reference

- User Story: us_101
- Story Location: .propel/context/tasks/EP-020/us_101/us_101.md
- Acceptance Criteria:
  - AC-3: Given centralized configuration is set up, When an admin modifies a setting, Then all running application instances pick up the change without restart.
  - AC-4: Given configuration files are stored, When they are managed, Then they use hierarchical appsettings.json with environment-specific overrides (Development, Staging, Production).
- Edge Case:
  - What happens when a feature flag configuration file is corrupted? System falls back to the last known good configuration and logs a critical alert.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Backend | Microsoft.Extensions.Configuration | 8.x |
| Backend | Microsoft.Extensions.Options | 8.x |
| Monitoring | Serilog | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement centralized configuration management using ASP.NET Core's hierarchical configuration system with `IOptionsMonitor<T>` for runtime hot-reload across all application instances. The configuration hierarchy follows the standard override pattern: `appsettings.json` → `appsettings.{Environment}.json` → environment variables → command-line arguments, where later sources override earlier ones. Strongly-typed configuration sections are defined for each subsystem (database, Redis, AI gateway, email, SMS) using the Options pattern, bound to POCO classes via `IOptions<T>` / `IOptionsMonitor<T>`. A `ConfigurationValidationService` runs at startup to validate all required settings are present and well-formed, failing fast with descriptive error messages if critical configuration is missing. A `ConfigurationChangeLogger` monitors configuration file changes and logs structured events for auditing. When an admin modifies `appsettings.json`, `IOptionsMonitor<T>` detects the change via `reloadOnChange: true` and propagates updated values to all consuming services without restart (AC-3). Environment-specific overrides (Development, Staging, Production) are supported via the hierarchical JSON file pattern (AC-4). Configuration file corruption triggers a fallback to cached values with Critical logging (edge case 1).

## Dependent Tasks

- task_001_be_feature_flag_service — Requires feature flag configuration infrastructure.
- US_001 — Requires backend API scaffold.
- US_007 — Requires configuration management baseline.

## Impacted Components

- **CREATE** `src/UPACIP.Service/Configuration/DatabaseOptions.cs` — Strongly-typed database connection settings
- **CREATE** `src/UPACIP.Service/Configuration/RedisOptions.cs` — Strongly-typed Redis connection settings
- **CREATE** `src/UPACIP.Service/Configuration/AiGatewayOptions.cs` — Strongly-typed AI gateway settings
- **CREATE** `src/UPACIP.Service/Configuration/EmailOptions.cs` — Strongly-typed email/SMTP settings
- **CREATE** `src/UPACIP.Service/Configuration/SmsOptions.cs` — Strongly-typed SMS/Twilio settings
- **CREATE** `src/UPACIP.Api/Configuration/AppConfigurationSetup.cs` — Configuration source registration with hierarchy
- **CREATE** `src/UPACIP.Api/Configuration/ConfigurationValidationService.cs` — IHostedService for startup validation
- **CREATE** `src/UPACIP.Api/Configuration/ConfigurationChangeLogger.cs` — IOptionsMonitor change listener for audit logging
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add structured configuration sections
- **CREATE** `src/UPACIP.Api/appsettings.Staging.json` — Staging environment overrides
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register configuration services and validation

## Implementation Plan

1. **Define strongly-typed options classes for each subsystem (AC-4)**: Create POCO classes in `src/UPACIP.Service/Configuration/`:

   **DatabaseOptions.cs:**
   ```csharp
   public sealed class DatabaseOptions
   {
       public const string SectionName = "Database";

       public required string ConnectionString { get; init; }
       public int MaxPoolSize { get; init; } = 100;
       public int CommandTimeoutSeconds { get; init; } = 30;
       public bool EnableSensitiveDataLogging { get; init; }
   }
   ```

   **RedisOptions.cs:**
   ```csharp
   public sealed class RedisOptions
   {
       public const string SectionName = "Redis";

       public required string ConnectionString { get; init; }
       public int DefaultTtlMinutes { get; init; } = 5;
       public int ConnectTimeoutMs { get; init; } = 5000;
       public int SyncTimeoutMs { get; init; } = 1000;
       public string InstanceName { get; init; } = "UPACIP:";
   }
   ```

   **AiGatewayOptions.cs:**
   ```csharp
   public sealed class AiGatewayOptions
   {
       public const string SectionName = "AiGateway";

       public required string PrimaryProviderBaseUrl { get; init; }
       public required string FallbackProviderBaseUrl { get; init; }
       public string PrimaryModel { get; init; } = "gpt-4o-mini";
       public string FallbackModel { get; init; } = "claude-3-5-sonnet-20241022";
       public int MaxTokensPerRequest { get; init; } = 4096;
       public int TimeoutSeconds { get; init; } = 30;
       public double ConfidenceThreshold { get; init; } = 0.8;
   }
   ```

   **EmailOptions.cs:**
   ```csharp
   public sealed class EmailOptions
   {
       public const string SectionName = "Email";

       public required string SmtpHost { get; init; }
       public int SmtpPort { get; init; } = 587;
       public required string FromAddress { get; init; }
       public string FromName { get; init; } = "UPACIP Platform";
       public bool EnableSsl { get; init; } = true;
   }
   ```

   **SmsOptions.cs:**
   ```csharp
   public sealed class SmsOptions
   {
       public const string SectionName = "Sms";

       public required string AccountSid { get; init; }
       public required string AuthToken { get; init; }
       public required string FromNumber { get; init; }
       public int MaxRetries { get; init; } = 3;
   }
   ```

   Key patterns:
   - Each class is `sealed` — prevents inheritance misuse.
   - `required` modifier on connection strings/credentials — compile-time enforcement.
   - Sensible defaults for timeouts, TTLs, pool sizes — reduces configuration burden.
   - `const string SectionName` — single source of truth for section binding.
   - **No secrets in code** — credentials come from environment variables or secure config (OWASP A07).

2. **Create `AppConfigurationSetup` for hierarchical configuration (AC-4)**: Create `src/UPACIP.Api/Configuration/AppConfigurationSetup.cs`:
   ```csharp
   public static class AppConfigurationSetup
   {
       public static ConfigurationManager AddHierarchicalConfiguration(
           this ConfigurationManager configuration,
           IHostEnvironment environment)
       {
           // Layer 1: Base configuration (lowest priority)
           configuration.AddJsonFile(
               "appsettings.json",
               optional: false,
               reloadOnChange: true);

           // Layer 2: Environment-specific overrides
           configuration.AddJsonFile(
               $"appsettings.{environment.EnvironmentName}.json",
               optional: true,
               reloadOnChange: true);

           // Layer 3: Environment variables (higher priority, for secrets)
           configuration.AddEnvironmentVariables(prefix: "UPACIP_");

           // Layer 4: Command-line arguments (highest priority)
           // Already added by default via WebApplicationBuilder

           return configuration;
       }

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
   ```
   Configuration hierarchy (AC-4):
   - **`appsettings.json`** — base settings shared across all environments. `optional: false` ensures startup failure if missing.
   - **`appsettings.{Environment}.json`** — environment-specific overrides. Development enables verbose logging, Staging uses test credentials, Production uses production credentials. `optional: true` because not every environment needs overrides.
   - **Environment variables** (`UPACIP_` prefix) — for secrets (API keys, connection strings). Overrides JSON files. Prefix scopes variables to this application only.
   - **Command-line arguments** — highest priority, used for ad-hoc overrides during debugging or deployment scripts.
   - **`reloadOnChange: true`** on both JSON sources — enables runtime hot-reload (AC-3).

3. **Implement `ConfigurationValidationService` for startup validation (AC-4, edge case 1)**: Create `src/UPACIP.Api/Configuration/ConfigurationValidationService.cs`:
   ```csharp
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

           ValidateSection<DatabaseOptions>(DatabaseOptions.SectionName, errors,
               opts =>
               {
                   if (string.IsNullOrWhiteSpace(opts.ConnectionString))
                       errors.Add($"{DatabaseOptions.SectionName}:ConnectionString is required");
                   if (opts.MaxPoolSize <= 0)
                       errors.Add($"{DatabaseOptions.SectionName}:MaxPoolSize must be > 0");
                   if (opts.CommandTimeoutSeconds <= 0)
                       errors.Add($"{DatabaseOptions.SectionName}:CommandTimeoutSeconds must be > 0");
               });

           ValidateSection<RedisOptions>(RedisOptions.SectionName, errors,
               opts =>
               {
                   if (string.IsNullOrWhiteSpace(opts.ConnectionString))
                       errors.Add($"{RedisOptions.SectionName}:ConnectionString is required");
                   if (opts.DefaultTtlMinutes <= 0)
                       errors.Add($"{RedisOptions.SectionName}:DefaultTtlMinutes must be > 0");
               });

           ValidateSection<AiGatewayOptions>(AiGatewayOptions.SectionName, errors,
               opts =>
               {
                   if (string.IsNullOrWhiteSpace(opts.PrimaryProviderBaseUrl))
                       errors.Add($"{AiGatewayOptions.SectionName}:PrimaryProviderBaseUrl is required");
                   if (string.IsNullOrWhiteSpace(opts.FallbackProviderBaseUrl))
                       errors.Add($"{AiGatewayOptions.SectionName}:FallbackProviderBaseUrl is required");
                   if (opts.MaxTokensPerRequest <= 0)
                       errors.Add($"{AiGatewayOptions.SectionName}:MaxTokensPerRequest must be > 0");
               });

           ValidateSection<EmailOptions>(EmailOptions.SectionName, errors,
               opts =>
               {
                   if (string.IsNullOrWhiteSpace(opts.SmtpHost))
                       errors.Add($"{EmailOptions.SectionName}:SmtpHost is required");
                   if (string.IsNullOrWhiteSpace(opts.FromAddress))
                       errors.Add($"{EmailOptions.SectionName}:FromAddress is required");
               });

           ValidateSection<SmsOptions>(SmsOptions.SectionName, errors,
               opts =>
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
                   _logger.LogCritical("CONFIGURATION_VALIDATION_FAILURE: {Error}", error);
               }

               throw new InvalidOperationException(
                   $"Configuration validation failed with {errors.Count} error(s): " +
                   string.Join("; ", errors));
           }

           _logger.LogInformation(
               "CONFIGURATION_VALIDATED: All configuration sections validated successfully");

           return Task.CompletedTask;
       }

       public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

       private void ValidateSection<T>(
           string sectionName,
           List<string> errors,
           Action<T> validate) where T : class, new()
       {
           try
           {
               var options = _serviceProvider
                   .GetRequiredService<IOptions<T>>().Value;
               validate(options);
           }
           catch (OptionsValidationException ex)
           {
               errors.Add($"{sectionName}: {ex.Message}");
           }
           catch (InvalidOperationException)
           {
               errors.Add($"{sectionName}: Section missing or cannot be bound");
           }
       }
   }
   ```
   Key behaviors:
   - **Runs at startup** — `IHostedService.StartAsync` executes before the application starts accepting requests.
   - **Fail-fast** — throws `InvalidOperationException` if any required configuration is missing, preventing the application from running with invalid settings.
   - **Descriptive errors** — each validation failure includes the section name and specific field, enabling fast diagnosis.
   - **No secrets in logs** — validates presence (not emptiness) of sensitive fields without logging their values (OWASP A07).
   - **Aggregated errors** — collects all failures before throwing, so admins see all issues at once rather than fixing one at a time.

4. **Implement `ConfigurationChangeLogger` for audit trail (AC-3)**: Create `src/UPACIP.Api/Configuration/ConfigurationChangeLogger.cs`:
   ```csharp
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

           _changeListeners.Add(
               databaseOptions.OnChange((opts, name) =>
                   LogConfigChange(DatabaseOptions.SectionName, name)));

           _changeListeners.Add(
               redisOptions.OnChange((opts, name) =>
                   LogConfigChange(RedisOptions.SectionName, name)));

           _changeListeners.Add(
               aiGatewayOptions.OnChange((opts, name) =>
                   LogConfigChange(AiGatewayOptions.SectionName, name)));

           _changeListeners.Add(
               emailOptions.OnChange((opts, name) =>
                   LogConfigChange(EmailOptions.SectionName, name)));

           _changeListeners.Add(
               smsOptions.OnChange((opts, name) =>
                   LogConfigChange(SmsOptions.SectionName, name)));
       }

       public Task StartAsync(CancellationToken cancellationToken)
       {
           _logger.LogInformation(
               "CONFIGURATION_MONITOR_STARTED: Monitoring {SectionCount} configuration sections for changes",
               _changeListeners.Count);

           return Task.CompletedTask;
       }

       public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

       private void LogConfigChange(string sectionName, string? namedOption)
       {
           _logger.LogWarning(
               "CONFIGURATION_CHANGED: Section '{SectionName}' was modified at {Timestamp}. " +
               "All application instances will pick up the change via IOptionsMonitor.",
               sectionName,
               DateTime.UtcNow.ToString("o"));
       }

       public void Dispose()
       {
           foreach (var listener in _changeListeners)
           {
               listener.Dispose();
           }

           _changeListeners.Clear();
       }
   }
   ```
   Key behaviors (AC-3):
   - **Subscribes to all options monitors** — detects changes across all configuration sections.
   - **`LogWarning` severity** — configuration changes are operationally significant and should be visible in monitoring dashboards.
   - **ISO 8601 timestamp** — consistent with other structured log events in the system.
   - **No secret logging** — logs the section name only, never the configuration values themselves.
   - **Disposable** — unsubscribes from change listeners on shutdown to prevent memory leaks.
   - **Audit trail** — combined with Serilog → Seq pipeline, provides searchable history of all configuration changes.

5. **Update `appsettings.json` with structured sections (AC-4)**: Add configuration sections to `src/UPACIP.Api/appsettings.json`:
   ```json
   {
     "Database": {
       "ConnectionString": "",
       "MaxPoolSize": 100,
       "CommandTimeoutSeconds": 30,
       "EnableSensitiveDataLogging": false
     },
     "Redis": {
       "ConnectionString": "",
       "DefaultTtlMinutes": 5,
       "ConnectTimeoutMs": 5000,
       "SyncTimeoutMs": 1000,
       "InstanceName": "UPACIP:"
     },
     "AiGateway": {
       "PrimaryProviderBaseUrl": "https://api.openai.com",
       "FallbackProviderBaseUrl": "https://api.anthropic.com",
       "PrimaryModel": "gpt-4o-mini",
       "FallbackModel": "claude-3-5-sonnet-20241022",
       "MaxTokensPerRequest": 4096,
       "TimeoutSeconds": 30,
       "ConfidenceThreshold": 0.8
     },
     "Email": {
       "SmtpHost": "",
       "SmtpPort": 587,
       "FromAddress": "",
       "FromName": "UPACIP Platform",
       "EnableSsl": true
     },
     "Sms": {
       "AccountSid": "",
       "AuthToken": "",
       "FromNumber": "",
       "MaxRetries": 3
     }
   }
   ```
   - **Connection strings and credentials are empty** — populated via environment variables (`UPACIP_Database__ConnectionString`) or environment-specific JSON files.
   - **Non-sensitive defaults** (pool sizes, timeouts, model names) are set in the base file.
   - Sections map 1:1 to the options classes defined in step 1.

6. **Create `appsettings.Staging.json` for staging environment (AC-4)**: Create `src/UPACIP.Api/appsettings.Staging.json`:
   ```json
   {
     "Database": {
       "MaxPoolSize": 50,
       "CommandTimeoutSeconds": 15,
       "EnableSensitiveDataLogging": false
     },
     "Redis": {
       "DefaultTtlMinutes": 2,
       "InstanceName": "UPACIP-STG:"
     },
     "AiGateway": {
       "MaxTokensPerRequest": 2048,
       "TimeoutSeconds": 15
     },
     "Serilog": {
       "MinimumLevel": {
         "Default": "Information"
       }
     }
   }
   ```
   Staging overrides:
   - **Reduced pool/timeout values** — staging has fewer resources than production.
   - **Shorter Redis TTL** — more frequent cache invalidation for testing.
   - **Unique Redis instance name** — prevents key collisions if staging and production share a Redis instance.
   - **Lower AI token budget** — reduces cost during staging testing.
   - Connection strings provided via environment variables.

7. **Register all configuration services in `Program.cs` (AC-3, AC-4)**: Update the application bootstrap:
   ```csharp
   // Configuration hierarchy (Layer 1-4)
   builder.Configuration.AddHierarchicalConfiguration(builder.Environment);

   // Strongly-typed options binding
   builder.Services.AddConfigurationOptions(builder.Configuration);

   // Startup validation
   builder.Services.AddHostedService<ConfigurationValidationService>();

   // Change monitoring
   builder.Services.AddSingleton<ConfigurationChangeLogger>();
   builder.Services.AddHostedService(
       sp => sp.GetRequiredService<ConfigurationChangeLogger>());
   ```
   Registration order:
   - Configuration sources first — must be registered before `builder.Build()`.
   - Options binding second — binds sections to strongly-typed classes.
   - Validation service third — `IHostedService` runs on startup, validates all sections.
   - Change logger as singleton + hosted service — survives the full application lifetime, starts monitoring after startup.

8. **Document multi-instance configuration propagation (AC-3)**: For Phase 1 (Windows Server + IIS deployment), configuration changes propagate to all instances via:
   - **Shared file system**: All IIS application pool instances read from the same `appsettings.json` file. `reloadOnChange: true` causes `PhysicalFileProvider` to detect the modification and trigger `IOptionsMonitor<T>.OnChange`.
   - **Propagation timeline**: File system notification → `IChangeToken` callback → `IOptionsMonitor<T>` refresh → consuming services see updated values. Total: <5 seconds on Windows.
   - **Multiple instances on the same server**: IIS worker processes share the file system. All processes detect the same change event.
   - **Multiple servers**: For multi-server deployments, a shared network drive or deployment script must copy the updated configuration file to each server. `reloadOnChange: true` detects the change on each server independently.

   For future Phase 2 (centralized config store like Azure App Configuration or Consul):
   - Replace `AddJsonFile` with the provider's configuration source.
   - `IOptionsMonitor<T>` continues to work unchanged — the Options pattern is provider-agnostic.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── config/
│   ├── featureflags.json                        ← from task_001
│   └── featureflags.Development.json            ← from task_001
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Configuration/
│   │   │   └── FeatureFlagConfiguration.cs      ← from task_001
│   │   ├── Controllers/
│   │   ├── HealthChecks/
│   │   ├── Middleware/
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   ├── UPACIP.Contracts/
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── FeatureFlags/                        ← from task_001
│   │   │   ├── IFeatureFlagService.cs
│   │   │   ├── FeatureFlagService.cs
│   │   │   ├── FeatureFlagOptions.cs
│   │   │   └── FeatureFlagDefinition.cs
│   │   └── Configuration/                       ← new directory
│   └── UPACIP.DataAccess/
├── tests/
├── e2e/
├── scripts/
├── app/
└── .propel/
```

> Assumes US_001 (backend API scaffold), US_007 (configuration baseline), and task_001 (feature flag service) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Configuration/DatabaseOptions.cs | Strongly-typed database connection settings |
| CREATE | src/UPACIP.Service/Configuration/RedisOptions.cs | Strongly-typed Redis connection settings |
| CREATE | src/UPACIP.Service/Configuration/AiGatewayOptions.cs | Strongly-typed AI gateway settings |
| CREATE | src/UPACIP.Service/Configuration/EmailOptions.cs | Strongly-typed email/SMTP settings |
| CREATE | src/UPACIP.Service/Configuration/SmsOptions.cs | Strongly-typed SMS/Twilio settings |
| CREATE | src/UPACIP.Api/Configuration/AppConfigurationSetup.cs | Hierarchical config sources with reloadOnChange |
| CREATE | src/UPACIP.Api/Configuration/ConfigurationValidationService.cs | IHostedService for startup validation with fail-fast |
| CREATE | src/UPACIP.Api/Configuration/ConfigurationChangeLogger.cs | IOptionsMonitor change listener for audit logging |
| MODIFY | src/UPACIP.Api/appsettings.json | Add Database, Redis, AiGateway, Email, Sms sections |
| CREATE | src/UPACIP.Api/appsettings.Staging.json | Staging environment overrides |
| MODIFY | src/UPACIP.Api/Program.cs | Register config hierarchy, options, validation, change logger |

## External References

- [ASP.NET Core Configuration — Hierarchical Sources](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration#configuration-providers)
- [ASP.NET Core Options Pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options)
- [IOptionsMonitor — Detecting Configuration Changes](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#ioptionsmonitor)
- [ASP.NET Core — Environment-based Configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/environments)
- [ASP.NET Core — Options Validation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options#options-validation)
- [OWASP A07 — Security Misconfiguration](https://owasp.org/Top10/A07_2021-Security_Misconfiguration/)

## Build Commands

```powershell
# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln

# Test configuration validation (should fail if required settings missing)
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj

# Test with specific environment
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj --environment Staging

# Test hot-reload (application must be running):
# 1. Modify appsettings.json while app is running
# 2. Check logs for CONFIGURATION_CHANGED event
# 3. Verify IOptionsMonitor consumers have updated values
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors
- [ ] Admin modifies appsettings.json → all instances pick up change without restart (AC-3)
- [ ] appsettings.json → appsettings.Development.json → env vars override hierarchy works (AC-4)
- [ ] appsettings.Staging.json overrides base settings when ASPNETCORE_ENVIRONMENT=Staging (AC-4)
- [ ] ConfigurationValidationService fails startup if required connection strings are missing
- [ ] ConfigurationChangeLogger emits CONFIGURATION_CHANGED log on file modification
- [ ] No secrets (passwords, API keys) appear in log output
- [ ] Corrupted JSON file → application uses cached options + Critical log (edge case 1)
- [ ] All options classes bind correctly to their configuration sections

## Implementation Checklist

- [ ] Create DatabaseOptions, RedisOptions, AiGatewayOptions, EmailOptions, SmsOptions classes
- [ ] Create AppConfigurationSetup with hierarchical config sources and reloadOnChange
- [ ] Register all options classes via Configure<T> in AddConfigurationOptions
- [ ] Implement ConfigurationValidationService with fail-fast startup validation
- [ ] Implement ConfigurationChangeLogger with IOptionsMonitor change subscriptions
- [ ] Update appsettings.json with structured sections (empty credentials)
- [ ] Create appsettings.Staging.json with staging-specific overrides
- [ ] Register configuration services in Program.cs
