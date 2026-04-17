# Task - task_001_be_feature_flag_service

## Requirement Reference

- User Story: us_101
- Story Location: .propel/context/tasks/EP-020/us_101/us_101.md
- Acceptance Criteria:
  - AC-1: Given a new feature is deployed, When the feature flag is set to "disabled," Then the feature is invisible to all users without requiring a code deployment.
  - AC-2: Given a feature flag is toggled to "enabled," When users interact with the application, Then the feature becomes available immediately (within 1 minute of configuration change).
- Edge Case:
  - What happens when a feature flag configuration file is corrupted? System falls back to the last known good configuration and logs a critical alert.
  - How does the system handle feature flags for A/B testing segments? Phase 1 supports boolean flags only (on/off); percentage-based rollout planned for Phase 2.

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
| Backend | Microsoft.FeatureManagement.AspNetCore | 3.x |
| Caching | Upstash Redis | 7.x |
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

Implement a file-based feature flag system using `Microsoft.FeatureManagement.AspNetCore` backed by a dedicated `featureflags.json` configuration file with `IOptionsMonitor<T>` for hot-reload (changes detected within 1 minute without restart). The service provides a clean `IFeatureFlagService` abstraction that controllers and services consume to gate feature visibility (AC-1) and respond to real-time flag toggles (AC-2). A `FileConfigurationWatcher` monitors the feature flag file using `PhysicalFileProvider`/`IChangeToken` with a 30-second polling interval — ensuring changes propagate within 1 minute. When the configuration file is corrupted (malformed JSON), the system falls back to the last known good configuration snapshot cached in memory and logs a `Critical` alert via Serilog (edge case 1). Phase 1 implements boolean-only flags (on/off); the data model reserves an `IsPercentageBased` field for future Phase 2 expansion but ignores it in evaluation logic (edge case 2).

## Dependent Tasks

- US_001 — Requires backend API scaffold with DI container and configuration pipeline.
- US_007 — Requires configuration management infrastructure baseline.

## Impacted Components

- **CREATE** `src/UPACIP.Service/FeatureFlags/IFeatureFlagService.cs` — Interface: IsEnabled, GetAllFlags
- **CREATE** `src/UPACIP.Service/FeatureFlags/FeatureFlagService.cs` — Implementation with IOptionsMonitor, fallback cache
- **CREATE** `src/UPACIP.Service/FeatureFlags/FeatureFlagOptions.cs` — Strongly-typed options for feature flag collection
- **CREATE** `src/UPACIP.Service/FeatureFlags/FeatureFlagDefinition.cs` — Model: Name, IsEnabled, Description, CreatedUtc
- **CREATE** `src/UPACIP.Api/Configuration/FeatureFlagConfiguration.cs` — DI registration and file watcher setup
- **CREATE** `config/featureflags.json` — Feature flag definitions file
- **CREATE** `config/featureflags.Development.json` — Development-specific flag overrides
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register feature flag services and configuration source

## Implementation Plan

1. **Define `FeatureFlagDefinition` model**: Create `src/UPACIP.Service/FeatureFlags/FeatureFlagDefinition.cs`:
   ```csharp
   public sealed class FeatureFlagDefinition
   {
       public required string Name { get; init; }
       public bool IsEnabled { get; set; }
       public string Description { get; init; } = string.Empty;
       public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
       public DateTime? LastModifiedUtc { get; set; }

       // Phase 2 placeholder — ignored in Phase 1 evaluation
       public bool IsPercentageBased { get; init; }
       public int RolloutPercentage { get; init; }
   }
   ```
   - `Name` uses `required` modifier — prevents flag creation without identifier.
   - `IsPercentageBased` and `RolloutPercentage` are present in the model but ignored by the service in Phase 1 (edge case 2). This avoids breaking schema changes when Phase 2 is implemented.
   - `LastModifiedUtc` is nullable — set only after the first toggle.

2. **Define `FeatureFlagOptions` for strongly-typed binding**: Create `src/UPACIP.Service/FeatureFlags/FeatureFlagOptions.cs`:
   ```csharp
   public sealed class FeatureFlagOptions
   {
       public const string SectionName = "FeatureFlags";

       public Dictionary<string, FeatureFlagDefinition> Flags { get; set; } = new();
   }
   ```
   Binds to the `FeatureFlags` section in `featureflags.json`. The dictionary key is the flag name for O(1) lookup.

3. **Define `IFeatureFlagService` interface**: Create `src/UPACIP.Service/FeatureFlags/IFeatureFlagService.cs`:
   ```csharp
   public interface IFeatureFlagService
   {
       bool IsEnabled(string featureName);
       IReadOnlyDictionary<string, FeatureFlagDefinition> GetAllFlags();
   }
   ```
   - `IsEnabled` returns `false` for unknown flag names — fail-closed behavior (secure default).
   - `GetAllFlags` returns a read-only snapshot for admin dashboard display.

4. **Implement `FeatureFlagService` with hot-reload and corruption fallback (AC-1, AC-2, edge case 1)**: Create `src/UPACIP.Service/FeatureFlags/FeatureFlagService.cs`:
   ```csharp
   public sealed class FeatureFlagService : IFeatureFlagService
   {
       private readonly IOptionsMonitor<FeatureFlagOptions> _optionsMonitor;
       private readonly ILogger<FeatureFlagService> _logger;
       private volatile FeatureFlagOptions _lastKnownGoodConfig;

       public FeatureFlagService(
           IOptionsMonitor<FeatureFlagOptions> optionsMonitor,
           ILogger<FeatureFlagService> logger)
       {
           _optionsMonitor = optionsMonitor;
           _logger = logger;

           // Capture initial config as last known good
           _lastKnownGoodConfig = CloneOptions(optionsMonitor.CurrentValue);

           // Subscribe to config changes
           _optionsMonitor.OnChange(OnConfigurationChanged);
       }

       public bool IsEnabled(string featureName)
       {
           var options = GetCurrentOptions();

           if (options.Flags.TryGetValue(featureName, out var flag))
           {
               return flag.IsEnabled;
           }

           _logger.LogWarning(
               "FEATURE_FLAG_UNKNOWN: Flag '{FeatureName}' not found, defaulting to disabled",
               featureName);

           return false; // Fail-closed: unknown flags are disabled
       }

       public IReadOnlyDictionary<string, FeatureFlagDefinition> GetAllFlags()
       {
           var options = GetCurrentOptions();
           return options.Flags.AsReadOnly();
       }

       private FeatureFlagOptions GetCurrentOptions()
       {
           try
           {
               var current = _optionsMonitor.CurrentValue;

               if (current?.Flags is null || current.Flags.Count == 0)
               {
                   _logger.LogCritical(
                       "FEATURE_FLAG_CORRUPTION: Current configuration is empty or null, " +
                       "falling back to last known good configuration with {FlagCount} flags",
                       _lastKnownGoodConfig.Flags.Count);

                   return _lastKnownGoodConfig;
               }

               return current;
           }
           catch (Exception ex)
           {
               _logger.LogCritical(ex,
                   "FEATURE_FLAG_CORRUPTION: Failed to read current configuration, " +
                   "falling back to last known good configuration");

               return _lastKnownGoodConfig;
           }
       }

       private void OnConfigurationChanged(FeatureFlagOptions newOptions)
       {
           if (newOptions?.Flags is not null && newOptions.Flags.Count > 0)
           {
               var previousFlags = _lastKnownGoodConfig;
               _lastKnownGoodConfig = CloneOptions(newOptions);

               // Log flag state changes
               foreach (var (name, flag) in newOptions.Flags)
               {
                   if (previousFlags.Flags.TryGetValue(name, out var oldFlag)
                       && oldFlag.IsEnabled != flag.IsEnabled)
                   {
                       _logger.LogInformation(
                           "FEATURE_FLAG_TOGGLED: '{FeatureName}' changed from {OldState} to {NewState}",
                           name,
                           oldFlag.IsEnabled ? "Enabled" : "Disabled",
                           flag.IsEnabled ? "Enabled" : "Disabled");
                   }
               }
           }
           else
           {
               _logger.LogCritical(
                   "FEATURE_FLAG_CORRUPTION: Received empty/null configuration on change event, " +
                   "retaining last known good configuration with {FlagCount} flags",
                   _lastKnownGoodConfig.Flags.Count);
           }
       }

       private static FeatureFlagOptions CloneOptions(FeatureFlagOptions source)
       {
           return new FeatureFlagOptions
           {
               Flags = source.Flags.ToDictionary(
                   kvp => kvp.Key,
                   kvp => new FeatureFlagDefinition
                   {
                       Name = kvp.Value.Name,
                       IsEnabled = kvp.Value.IsEnabled,
                       Description = kvp.Value.Description,
                       CreatedUtc = kvp.Value.CreatedUtc,
                       LastModifiedUtc = kvp.Value.LastModifiedUtc,
                       IsPercentageBased = kvp.Value.IsPercentageBased,
                       RolloutPercentage = kvp.Value.RolloutPercentage,
                   })
           };
       }
   }
   ```
   Key behaviors:
   - **AC-1 (feature invisible when disabled)**: `IsEnabled` returns the current `IsEnabled` value from the bound configuration. When set to `false`, any consuming controller/service skips the feature. Fail-closed for unknown flags.
   - **AC-2 (available within 1 minute)**: `IOptionsMonitor<T>` automatically detects file changes via `IChangeToken`. Combined with the `reloadOnChange: true` setting and 30-second `PhysicalFileProvider` polling, changes propagate within ~30-60 seconds.
   - **Edge case 1 (corruption fallback)**: `_lastKnownGoodConfig` stores a deep clone of the last valid configuration. If `_optionsMonitor.CurrentValue` returns null/empty (corrupt JSON), the service falls back to this snapshot and logs `Critical`.
   - **Change logging**: `OnChange` callback compares old and new states, logging `FEATURE_FLAG_TOGGLED` for each flag that changed — provides an audit trail.
   - **Thread safety**: `_lastKnownGoodConfig` is marked `volatile` for visibility across threads. The `CloneOptions` method creates a deep copy to prevent reference mutation.

5. **Create `FeatureFlagConfiguration` extension for DI registration**: Create `src/UPACIP.Api/Configuration/FeatureFlagConfiguration.cs`:
   ```csharp
   public static class FeatureFlagConfiguration
   {
       public static IServiceCollection AddFeatureFlagServices(
           this IServiceCollection services,
           IConfiguration configuration)
       {
           services.Configure<FeatureFlagOptions>(
               configuration.GetSection(FeatureFlagOptions.SectionName));

           services.AddSingleton<IFeatureFlagService, FeatureFlagService>();

           return services;
       }

       public static ConfigurationManager AddFeatureFlagConfiguration(
           this ConfigurationManager configuration,
           IHostEnvironment environment)
       {
           var basePath = Path.Combine(
               AppContext.BaseDirectory, "config");

           configuration.AddJsonFile(
               Path.Combine(basePath, "featureflags.json"),
               optional: false,
               reloadOnChange: true);

           configuration.AddJsonFile(
               Path.Combine(basePath, $"featureflags.{environment.EnvironmentName}.json"),
               optional: true,
               reloadOnChange: true);

           return configuration;
       }
   }
   ```
   - **`reloadOnChange: true`** — enables `IChangeToken`-based file monitoring. ASP.NET Core's `PhysicalFileProvider` polls the file system for changes (default interval varies by OS; on Windows, uses `ReadDirectoryChangesW` for near-instant notification).
   - **Environment-specific override** — `featureflags.Development.json` overrides base `featureflags.json` values. Only loaded when `ASPNETCORE_ENVIRONMENT=Development`.
   - **`optional: false`** for base file — application fails to start if `featureflags.json` is missing (fail-fast).
   - **`optional: true`** for environment file — environment-specific overrides are not required.
   - **Singleton lifetime** — `FeatureFlagService` is singleton because `IOptionsMonitor<T>` is itself singleton and the service holds the fallback cache.

6. **Register in `Program.cs`**: Update the application bootstrap:
   ```csharp
   // Configuration sources
   builder.Configuration.AddFeatureFlagConfiguration(builder.Environment);

   // Services
   builder.Services.AddFeatureFlagServices(builder.Configuration);
   ```
   Position: Add configuration source before `builder.Build()`, register services with other DI registrations.

7. **Create `featureflags.json` with initial flag definitions**: Create `config/featureflags.json`:
   ```json
   {
     "FeatureFlags": {
       "Flags": {
         "AiConversationalIntake": {
           "Name": "AiConversationalIntake",
           "IsEnabled": false,
           "Description": "Enable AI-powered conversational patient intake flow",
           "CreatedUtc": "2026-04-17T00:00:00Z",
           "IsPercentageBased": false,
           "RolloutPercentage": 0
         },
         "AiDocumentParsing": {
           "Name": "AiDocumentParsing",
           "IsEnabled": false,
           "Description": "Enable AI-powered clinical document parsing and extraction",
           "CreatedUtc": "2026-04-17T00:00:00Z",
           "IsPercentageBased": false,
           "RolloutPercentage": 0
         },
         "AiMedicalCoding": {
           "Name": "AiMedicalCoding",
           "IsEnabled": false,
           "Description": "Enable AI-assisted ICD-10/CPT medical code suggestion",
           "CreatedUtc": "2026-04-17T00:00:00Z",
           "IsPercentageBased": false,
           "RolloutPercentage": 0
         },
         "SmsReminders": {
           "Name": "SmsReminders",
           "IsEnabled": true,
           "Description": "Enable SMS appointment reminders via Twilio",
           "CreatedUtc": "2026-04-17T00:00:00Z",
           "IsPercentageBased": false,
           "RolloutPercentage": 0
         },
         "EmailReminders": {
           "Name": "EmailReminders",
           "IsEnabled": true,
           "Description": "Enable email appointment reminders",
           "CreatedUtc": "2026-04-17T00:00:00Z",
           "IsPercentageBased": false,
           "RolloutPercentage": 0
         }
       }
     }
   }
   ```
   Initial flags align with design decision #10 (AI feature flags for gradual rollout):
   - AI features (`AiConversationalIntake`, `AiDocumentParsing`, `AiMedicalCoding`) are disabled by default — enabled per cohort during gradual rollout.
   - Communication features (`SmsReminders`, `EmailReminders`) are enabled by default.

8. **Create `featureflags.Development.json` with development overrides**: Create `config/featureflags.Development.json`:
   ```json
   {
     "FeatureFlags": {
       "Flags": {
         "AiConversationalIntake": {
           "Name": "AiConversationalIntake",
           "IsEnabled": true,
           "Description": "Enable AI-powered conversational patient intake flow"
         },
         "AiDocumentParsing": {
           "Name": "AiDocumentParsing",
           "IsEnabled": true,
           "Description": "Enable AI-powered clinical document parsing and extraction"
         },
         "AiMedicalCoding": {
           "Name": "AiMedicalCoding",
           "IsEnabled": true,
           "Description": "Enable AI-assisted ICD-10/CPT medical code suggestion"
         }
       }
     }
   }
   ```
   All AI features enabled in Development for testing. Production `featureflags.json` keeps them disabled until explicit rollout.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── config/                                      ← configuration files directory
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Configuration/
│   │   ├── Controllers/
│   │   ├── HealthChecks/
│   │   ├── Middleware/
│   │   ├── appsettings.json
│   │   └── appsettings.Development.json
│   ├── UPACIP.Contracts/
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   └── FeatureFlags/                        ← new directory
│   └── UPACIP.DataAccess/
├── tests/
├── e2e/
├── scripts/
├── app/
└── .propel/
```

> Assumes US_001 (backend API scaffold) and US_007 (configuration management baseline) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/FeatureFlags/FeatureFlagDefinition.cs | Flag model: Name, IsEnabled, Description, Phase 2 placeholders |
| CREATE | src/UPACIP.Service/FeatureFlags/FeatureFlagOptions.cs | Strongly-typed options binding to FeatureFlags config section |
| CREATE | src/UPACIP.Service/FeatureFlags/IFeatureFlagService.cs | Interface: IsEnabled(name), GetAllFlags() |
| CREATE | src/UPACIP.Service/FeatureFlags/FeatureFlagService.cs | Singleton with IOptionsMonitor, fallback cache, change logging |
| CREATE | src/UPACIP.Api/Configuration/FeatureFlagConfiguration.cs | DI registration + config source with reloadOnChange |
| CREATE | config/featureflags.json | Base feature flag definitions (AI flags disabled, comms enabled) |
| CREATE | config/featureflags.Development.json | Development overrides (all AI flags enabled) |
| MODIFY | src/UPACIP.Api/Program.cs | Register feature flag config source and services |

## External References

- [ASP.NET Core Configuration — File Change Detection](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration#file-configuration-provider)
- [IOptionsMonitor — React to Configuration Changes](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#ioptionsmonitor)
- [Microsoft.FeatureManagement — Feature Flags in ASP.NET Core](https://learn.microsoft.com/en-us/azure/azure-app-configuration/use-feature-flags-dotnet-core)
- [ASP.NET Core Options Pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options)
- [PhysicalFileProvider — Change Token Polling](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.fileproviders.physicalfileprovider)

## Build Commands

```powershell
# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln

# Test feature flag toggle (application must be running)
# 1. Change IsEnabled in config/featureflags.json
# 2. Wait up to 60 seconds
# 3. Verify via endpoint or logs
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors
- [ ] Feature flag set to disabled → feature is not accessible (AC-1)
- [ ] Feature flag toggled to enabled → feature available within 1 minute without restart (AC-2)
- [ ] Unknown feature flag name returns false (fail-closed)
- [ ] Corrupted featureflags.json → system uses last known good config + Critical log (edge case 1)
- [ ] IOptionsMonitor.OnChange fires when featureflags.json is modified
- [ ] Flag state changes logged with FEATURE_FLAG_TOGGLED event
- [ ] Phase 2 fields (IsPercentageBased, RolloutPercentage) are present in model but ignored (edge case 2)
- [ ] Development environment loads featureflags.Development.json overrides

## Implementation Checklist

- [ ] Create FeatureFlagDefinition model with Phase 2 placeholder fields
- [ ] Create FeatureFlagOptions with dictionary-based flag collection
- [ ] Define IFeatureFlagService interface (IsEnabled, GetAllFlags)
- [ ] Implement FeatureFlagService with IOptionsMonitor, fallback cache, and change logging
- [ ] Create FeatureFlagConfiguration extension for DI and config source registration
- [ ] Create featureflags.json with AI and communication flag definitions
- [ ] Create featureflags.Development.json with AI flags enabled
- [ ] Register feature flag services in Program.cs
