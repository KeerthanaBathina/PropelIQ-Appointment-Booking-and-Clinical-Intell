# Task - task_002_be_config_feature_flags

## Requirement Reference

- User Story: us_007
- Story Location: .propel/context/tasks/EP-TECH/us_007/us_007.md
- Acceptance Criteria:
  - AC-3: Given centralized configuration is in place, When a setting is changed in appsettings.json, Then the application reads the updated value without restart (for supported hot-reload settings).
  - AC-4: Given feature flags are configured, When a feature flag is set to `false`, Then the corresponding feature endpoint returns 404 Not Found or a feature-disabled message.
- Edge Case:
  - How does the system handle malformed configuration values? Application logs validation error at startup and uses safe defaults.

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
| Library | Microsoft.FeatureManagement.AspNetCore | 3.x |

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

Implement centralized configuration management with `appsettings.json` hot-reload support per TR-022 and feature flag infrastructure using `Microsoft.FeatureManagement.AspNetCore` per TR-021. Configuration changes to supported settings (e.g., feature flags, log levels) are picked up without application restart via `IOptionsMonitor<T>` and `reloadOnChange: true`. Feature flags are stored in the `FeatureManagement` section of `appsettings.json` and evaluated through `IFeatureManager`. Endpoints gated by a disabled feature flag return 404 Not Found using the `[FeatureGate]` attribute. Malformed configuration values are caught by validation at startup with safe fallback defaults logged via Serilog.

## Dependent Tasks

- US_001 task_001_be_solution_scaffold — Backend solution with `Program.cs` and `appsettings.json` must exist.

## Impacted Components

- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Add `Microsoft.FeatureManagement.AspNetCore` NuGet package
- **MODIFY** `src/UPACIP.Api/Program.cs` — Configure `reloadOnChange: true` for appsettings files, register feature management services, add configuration validation
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add `FeatureManagement` section with initial feature flags
- **NEW** `src/UPACIP.Api/Configuration/FeatureFlags.cs` — Static class defining feature flag name constants (avoids magic strings)
- **NEW** `src/UPACIP.Api/Configuration/AppSettings.cs` — Strongly-typed POCO for application settings with `[Required]` data annotations for validation

## Implementation Plan

1. **Ensure `reloadOnChange` is enabled**: In `Program.cs`, verify that `builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)` and `builder.Configuration.AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)` are configured. ASP.NET Core 8 `WebApplication.CreateBuilder` enables `reloadOnChange: true` by default, but explicitly confirm this in the configuration pipeline for clarity.

2. **Create strongly-typed configuration POCO**: Create `AppSettings.cs` with sections matching `appsettings.json` structure (e.g., `ConnectionStrings`, `JwtSettings`, `CacheSettings`). Apply `[Required]` data annotations to mandatory fields. Register with `builder.Services.Configure<AppSettings>(builder.Configuration)` and add `builder.Services.AddOptionsWithValidateOnStart<AppSettings>()` to validate at startup. If validation fails, the application logs the error and refuses to start (fail-fast).

3. **Use `IOptionsMonitor<T>` for hot-reload**: For configuration sections that support hot-reload (feature flags, log levels, non-connection-string settings), inject `IOptionsMonitor<T>` instead of `IOptions<T>` in services. `IOptionsMonitor<T>` automatically picks up changes when the underlying `appsettings.json` file is modified on disk. Document which settings support hot-reload vs. which require restart (connection strings, Kestrel bindings).

4. **Install Microsoft.FeatureManagement.AspNetCore**: Add the `Microsoft.FeatureManagement.AspNetCore` (3.x) NuGet package. This is Microsoft's official feature flag library supporting percentage rollout, time windows, and custom filters — all driven by `appsettings.json` configuration.

5. **Register feature management services**: In `Program.cs`, call `builder.Services.AddFeatureManagement()` which reads the `FeatureManagement` section from configuration. This integrates with the configuration hot-reload pipeline, so toggling a flag in `appsettings.json` takes effect without restart.

6. **Define feature flag constants**: Create `FeatureFlags.cs` with `public static class FeatureFlags` containing string constants for each feature flag name (e.g., `public const string AiDocumentParsing = "AiDocumentParsing"`, `public const string SmsNotifications = "SmsNotifications"`). This avoids magic strings in `[FeatureGate]` attributes and `IFeatureManager.IsEnabledAsync()` calls.

7. **Add `FeatureManagement` section to `appsettings.json`**: Add initial feature flags with sensible Phase 1 defaults. Example structure: `"FeatureManagement": { "AiDocumentParsing": false, "SmsNotifications": false, "ConversationalIntake": false, "WaitlistManagement": true }`. All AI-dependent features default to `false` until AI infrastructure is implemented.

8. **Apply `[FeatureGate]` to controller actions**: Decorate relevant controller actions or entire controllers with `[FeatureGate(FeatureFlags.XxxFeature)]`. When the flag is `false`, ASP.NET Core automatically returns 404 Not Found. For custom responses (e.g., `{ "error": "Feature is currently disabled" }`), implement `IDisabledFeaturesHandler` and register it in DI.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── HealthChecks/
│   │   │   └── HealthCheckResponseWriter.cs
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlerMiddleware.cs
│   │   │   └── CorrelationIdMiddleware.cs
│   │   └── Controllers/
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
│       └── ApplicationDbContext.cs
├── app/
└── scripts/
```

> Assumes US_001 scaffold and task_001_be_health_check_endpoints are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add `Microsoft.FeatureManagement.AspNetCore` 3.x NuGet package |
| MODIFY | src/UPACIP.Api/Program.cs | Confirm `reloadOnChange: true`, register `AddFeatureManagement()`, add `AddOptionsWithValidateOnStart<AppSettings>()` for startup validation |
| MODIFY | src/UPACIP.Api/appsettings.json | Add `FeatureManagement` section with initial feature flags (AiDocumentParsing: false, SmsNotifications: false, ConversationalIntake: false, WaitlistManagement: true) |
| CREATE | src/UPACIP.Api/Configuration/FeatureFlags.cs | Static class with string constants for feature flag names |
| CREATE | src/UPACIP.Api/Configuration/AppSettings.cs | Strongly-typed POCO with `[Required]` annotations for configuration validation, registered via `IOptionsMonitor<T>` |

## External References

- [Configuration in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-8.0)
- [Options pattern in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0)
- [Microsoft.FeatureManagement documentation](https://learn.microsoft.com/en-us/azure/azure-app-configuration/use-feature-flags-dotnet-core)
- [Microsoft.FeatureManagement.AspNetCore NuGet](https://www.nuget.org/packages/Microsoft.FeatureManagement.AspNetCore)
- [FeatureGate attribute reference](https://learn.microsoft.com/en-us/dotnet/api/microsoft.featuremanagement.mvc.featuregateattribute)
- [Options validation in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0#options-validation)

## Build Commands

```powershell
# Restore packages
dotnet restore src/UPACIP.Api/UPACIP.Api.csproj

# Build
dotnet build src/UPACIP.Api/UPACIP.Api.csproj --no-restore

# Run application
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj

# Test feature flag disabled (should return 404)
curl -s -o NUL -w "%{http_code}" http://localhost:5000/api/ai/document-parse
# Expected: 404

# Modify appsettings.json to toggle feature flag, then re-test without restart
# Edit FeatureManagement.AiDocumentParsing = true in appsettings.json
curl -s -o NUL -w "%{http_code}" http://localhost:5000/api/ai/document-parse
# Expected: 200 (or 401 if auth required)

# Test malformed config (set required field to empty string, restart)
# Expected: Application fails to start with validation error logged
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors after adding FeatureManagement package
- [ ] Changing a value in `appsettings.json` (e.g., log level) is reflected in the running application without restart
- [ ] Feature flag set to `true` allows endpoint access (returns expected response)
- [ ] Feature flag set to `false` returns 404 Not Found (or custom feature-disabled message)
- [ ] Toggling a feature flag in `appsettings.json` while app is running takes effect without restart
- [ ] Application logs a validation error and fails to start when a `[Required]` configuration value is missing
- [ ] Malformed configuration values (e.g., non-numeric port) are caught at startup with descriptive error log
- [ ] `FeatureFlags.cs` constants match the keys in `appsettings.json` `FeatureManagement` section

## Implementation Checklist

- [ ] Add `Microsoft.FeatureManagement.AspNetCore` 3.x to `UPACIP.Api.csproj`
- [ ] Confirm `reloadOnChange: true` is set for `appsettings.json` and environment-specific override files in `Program.cs`
- [ ] Create `Configuration/AppSettings.cs` with strongly-typed sections and `[Required]` annotations, register with `AddOptionsWithValidateOnStart<AppSettings>()`
- [ ] Register feature management services via `builder.Services.AddFeatureManagement()` in `Program.cs`
- [ ] Create `Configuration/FeatureFlags.cs` with `const string` entries for each feature flag name
- [ ] Add `FeatureManagement` section to `appsettings.json` with initial flags (AiDocumentParsing: false, SmsNotifications: false, ConversationalIntake: false, WaitlistManagement: true)
- [ ] Apply `[FeatureGate]` attribute example on a sample controller action and verify 404 behavior when flag is disabled
- [ ] Implement `IDisabledFeaturesHandler` to return a JSON `{ "error": "Feature is currently disabled" }` response instead of bare 404
