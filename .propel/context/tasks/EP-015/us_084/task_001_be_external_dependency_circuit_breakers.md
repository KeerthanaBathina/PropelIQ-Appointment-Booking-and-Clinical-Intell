# Task - task_001_be_external_dependency_circuit_breakers

## Requirement Reference

- User Story: us_084
- Story Location: .propel/context/tasks/EP-015/us_084/us_084.md
- Acceptance Criteria:
  - AC-1: Given an external dependency (SMS, email, AI provider) fails repeatedly, When the circuit breaker threshold is reached, Then requests to that dependency are short-circuited and a fallback response is returned immediately.

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
| Backend | Polly | 8.x |
| Backend | Serilog | 8.x |
| Library | Twilio | 2023-05 |
| Library | System.Net.Mail (SMTP) | 8.x |
| Caching | Upstash Redis | 7.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements deterministic Polly circuit breaker policies around external dependencies. No LLM inference — it wraps existing SMS, email, and AI Gateway calls with resilience policies.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement a unified external dependency circuit breaker framework using Polly 8.x that prevents cascading failures when SMS (Twilio), email (SMTP), or AI provider (OpenAI/Anthropic) services become unavailable, per NFR-023. The framework provides three capabilities: (1) **Resilience pipeline registry** — a centralized `IExternalServiceResilienceProvider` that creates and manages named Polly resilience pipelines (retry + circuit breaker + timeout) for each external dependency category, replacing ad-hoc Polly usage across individual services with a consistent, configurable approach; (2) **Per-dependency circuit breaker policies** — SMS circuit breaker (open after 3 failures, 60-second break), email circuit breaker (open after 3 failures, 30-second break), AI provider circuit breaker (open after 5 consecutive failures, 30-second break per AIR-O04) with each policy tracking state independently; (3) **Fallback response routing** — when a circuit is open, each dependency returns a structured fallback immediately: SMS → queue for later delivery, email → queue for later delivery, AI → redirect to manual workflow (integrating with `IDegradationModeManager` from US_083). All circuit state transitions are logged via Serilog for operational visibility.

## Dependent Tasks

- US_001 — Requires backend API scaffold.
- US_004 task_002_be_cache_service_layer — Requires `ICacheService` with existing Redis circuit breaker pattern as reference.
- US_012 task_002_be_registration_verification_api — Requires `IEmailService` / `SmtpEmailService` to wrap with circuit breaker.
- US_083 task_002_be_graceful_degradation_ai_fallback — Requires `IDegradationModeManager` for AI provider degradation signaling.

## Impacted Components

- **NEW** `src/UPACIP.Service/Resilience/IExternalServiceResilienceProvider.cs` — Interface: GetPipeline(string dependencyName), GetCircuitState(string dependencyName)
- **NEW** `src/UPACIP.Service/Resilience/ExternalServiceResilienceProvider.cs` — Polly resilience pipeline registry with named pipelines per dependency
- **NEW** `src/UPACIP.Service/Resilience/Models/CircuitBreakerOptions.cs` — Config per dependency: FailureThreshold, BreakDurationSeconds, TimeoutSeconds, RetryCount
- **NEW** `src/UPACIP.Service/Resilience/Models/ExternalServiceFallback.cs` — Fallback response model: DependencyName, FallbackAction, QueuedForRetry, Message
- **NEW** `src/UPACIP.Service/Notifications/ResilientSmsService.cs` — Wraps Twilio SDK calls with SMS circuit breaker pipeline and queue-for-retry fallback
- **NEW** `src/UPACIP.Service/Notifications/ResilientEmailService.cs` — Wraps `SmtpEmailService` with email circuit breaker pipeline and queue-for-retry fallback
- **MODIFY** `Server/AI/AiGatewayService.cs` — Integrate AI circuit breaker pipeline from resilience provider (replace ad-hoc Polly if present)
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register resilience provider, bind circuit breaker configuration, wire resilient service decorators
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add ExternalServiceResilience configuration section

## Implementation Plan

1. **Create resilience configuration models**: Create `CircuitBreakerConfig` with per-dependency settings: `string DependencyName`, `int FailureThreshold` (consecutive failures before opening), `int BreakDurationSeconds` (how long circuit stays open), `int TimeoutSeconds` (per-call timeout), `int RetryCount` (retries before counting as failure), `int RetryBaseDelayMs` (exponential backoff base). Create `ExternalServiceResilienceOptions` containing `Dictionary<string, CircuitBreakerConfig> Dependencies` with defaults:
   ```json
   "ExternalServiceResilience": {
     "Dependencies": {
       "Sms": { "FailureThreshold": 3, "BreakDurationSeconds": 60, "TimeoutSeconds": 10, "RetryCount": 2, "RetryBaseDelayMs": 500 },
       "Email": { "FailureThreshold": 3, "BreakDurationSeconds": 30, "TimeoutSeconds": 15, "RetryCount": 3, "RetryBaseDelayMs": 1000 },
       "AiPrimary": { "FailureThreshold": 5, "BreakDurationSeconds": 30, "TimeoutSeconds": 30, "RetryCount": 3, "RetryBaseDelayMs": 1000 },
       "AiFallback": { "FailureThreshold": 5, "BreakDurationSeconds": 30, "TimeoutSeconds": 30, "RetryCount": 2, "RetryBaseDelayMs": 500 }
     }
   }
   ```
   The AI thresholds align with AIR-O04 (5 consecutive failures, 30-second retry). SMS has a longer break (60s) because Twilio rate limits recover slowly.

2. **Implement `ExternalServiceResilienceProvider`**: Create `IExternalServiceResilienceProvider` / `ExternalServiceResilienceProvider` registered as a singleton. On construction, build a `ConcurrentDictionary<string, ResiliencePipeline>` of named Polly V8 resilience pipelines from configuration. Each pipeline composes three strategies in order:
   - **Timeout**: `TimeoutStrategyOptions` with configured per-call timeout.
   - **Retry**: `RetryStrategyOptions` with exponential backoff (`delay * 2^attempt`), configured max retries, and jitter via `Backoff.DecorrelatedJitterBackoffV2`.
   - **Circuit Breaker**: `CircuitBreakerStrategyOptions` with `FailureRatio = 1.0` (consecutive mode), `SamplingDuration = TimeSpan.FromSeconds(breakDuration)`, `MinimumThroughput = failureThreshold`. On state transition, log via Serilog: `Log.Warning("CIRCUIT_BREAKER: {Dependency} transitioned {FromState} → {ToState}")`.
   Method `GetPipeline(string dependencyName)` returns the named pipeline. Method `GetCircuitState(string dependencyName)` returns the current state (Closed/Open/HalfOpen) for monitoring. The provider avoids creating duplicate pipelines for the same dependency name.

3. **Implement `ResilientSmsService` (AC-1 — SMS)**: Create a decorator service that wraps the existing SMS sending logic (Twilio SDK `MessageResource.CreateAsync`) with the "Sms" circuit breaker pipeline. Method `SendSmsAsync(string phoneNumber, string message)`: (a) execute the Twilio API call through `resilienceProvider.GetPipeline("Sms").ExecuteAsync()`; (b) on success, return `ExternalServiceFallback { Success = true }`; (c) on `BrokenCircuitException` (circuit is open), log a warning and enqueue the SMS for later delivery via Redis list `notification:sms:retry_queue` with payload `{ PhoneNumber, Message, QueuedAt }`, then return `ExternalServiceFallback { FallbackAction = "queued_for_retry", QueuedForRetry = true, Message = "SMS delivery temporarily unavailable — queued for retry" }`; (d) on other exceptions after retry exhaustion, also queue for retry. A separate `NotificationRetryService` (BackgroundService) periodically checks the retry queue and re-attempts delivery when the circuit closes — this retry processor is a lightweight poller (every 60 seconds) that checks circuit state before attempting.

4. **Implement `ResilientEmailService` (AC-1 — Email)**: Create a decorator that wraps `IEmailService` (SmtpEmailService from US_012) with the "Email" circuit breaker pipeline. Same pattern as SMS: execute through resilience pipeline, on `BrokenCircuitException` queue to `notification:email:retry_queue`, return fallback response. This replaces the existing ad-hoc Polly retry in `SmtpEmailService` (US_012) with the centralized resilience pipeline. Register `ResilientEmailService` as the `IEmailService` implementation in DI (decorator pattern — inject `SmtpEmailService` as inner service).

5. **Integrate AI circuit breaker with `AiGatewayService` (AC-1 — AI)**: Modify `AiGatewayService` to use the resilience provider's "AiPrimary" and "AiFallback" pipelines instead of any existing ad-hoc Polly configuration. The AI Gateway already implements primary/fallback provider routing (OpenAI → Claude per AIR-O04). Integrate as follows: (a) wrap the primary provider call (OpenAI) with `GetPipeline("AiPrimary").ExecuteAsync()`; (b) if the primary circuit opens, route to the fallback provider (Claude) wrapped in `GetPipeline("AiFallback").ExecuteAsync()`; (c) if both circuits are open, signal `IDegradationModeManager.ActivateDegradation(DependencyCategory.AiProviders)` and return the manual workflow fallback response (from US_083 task_002). When either circuit closes (HalfOpen → Closed transition), call `IDegradationModeManager.DeactivateDegradation(DependencyCategory.AiProviders)` to restore normal operations. This consolidates AI resilience into the shared framework while preserving the existing multi-provider failover.

6. **Implement `NotificationRetryService`**: Create a `BackgroundService` that periodically (every 60 seconds) checks the SMS and email retry queues. For each queue: (a) check if the corresponding circuit is Closed or HalfOpen via `GetCircuitState()`; (b) if circuit is still Open, skip — do not attempt retry; (c) if circuit is Closed/HalfOpen, dequeue up to 10 items per cycle and re-attempt delivery through the resilience pipeline; (d) on success, remove from queue; (e) on failure, re-enqueue with incremented retry count; (f) discard items that have been retried more than 5 times (configurable) and log an error. This ensures queued notifications are eventually delivered when external services recover without overwhelming them with a burst of retries.

7. **Expose circuit breaker status for monitoring**: Add a method `GetAllCircuitStates()` to the resilience provider that returns a `Dictionary<string, CircuitState>` for all registered dependencies. This is consumed by the `SystemStatusController` (from US_083 task_002) to include circuit breaker states in the system status response. Staff and admins can see which external dependencies have open circuits. Emit circuit state metrics via `IPerformanceTracker` on each state transition: `RecordLatency("circuit.{dependency}.state_change", 1)` for tracking circuit open frequency.

8. **Register services and configure DI**: In `Program.cs`: bind `ExternalServiceResilienceOptions` from configuration, register `services.AddSingleton<IExternalServiceResilienceProvider, ExternalServiceResilienceProvider>()`, register `ResilientSmsService` as the SMS sender implementation, register `ResilientEmailService` as the `IEmailService` decorator (wrapping `SmtpEmailService`), register `services.AddHostedService<NotificationRetryService>()`. Add the `ExternalServiceResilience` section to `appsettings.json`. No middleware changes needed — circuit breakers are applied at the service layer, not the HTTP pipeline.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── Admin/
│   │   │       └── SystemStatusController.cs        ← from US_083
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Caching/
│   │   │   ├── ICacheService.cs                     ← from US_004
│   │   │   └── RedisCacheService.cs                 ← from US_004
│   │   ├── Monitoring/
│   │   │   ├── IDegradationModeManager.cs           ← from US_083
│   │   │   ├── DegradationModeManager.cs            ← from US_083
│   │   │   └── UptimeMonitoringService.cs           ← from US_083
│   │   ├── Performance/
│   │   │   └── IPerformanceTracker.cs               ← from US_081
│   │   └── Notifications/
│   └── UPACIP.DataAccess/
│       └── ApplicationDbContext.cs
├── Server/
│   ├── Services/
│   │   ├── IEmailService.cs                         ← from US_012
│   │   ├── SmtpEmailService.cs                      ← from US_012
│   │   ├── IAppointmentBookingService.cs
│   │   ├── AppointmentBookingService.cs
│   │   └── AppointmentSlotCacheService.cs           ← from US_017
│   └── AI/
│       ├── AiGatewayService.cs                      ← from US_067
│       └── DocumentParsing/
├── app/
├── config/
└── scripts/
```

> Assumes US_004 (Redis + ICacheService), US_012 (IEmailService), US_067 (AI Gateway), US_081 (performance tracker), and US_083 (degradation manager) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Resilience/IExternalServiceResilienceProvider.cs | Interface: GetPipeline, GetCircuitState, GetAllCircuitStates |
| CREATE | src/UPACIP.Service/Resilience/ExternalServiceResilienceProvider.cs | Named Polly V8 resilience pipeline registry with retry + circuit breaker + timeout |
| CREATE | src/UPACIP.Service/Resilience/Models/CircuitBreakerOptions.cs | Config per dependency: FailureThreshold, BreakDurationSeconds, TimeoutSeconds, RetryCount |
| CREATE | src/UPACIP.Service/Resilience/Models/ExternalServiceFallback.cs | Fallback response: DependencyName, FallbackAction, QueuedForRetry, Message |
| CREATE | src/UPACIP.Service/Notifications/ResilientSmsService.cs | SMS sending with circuit breaker and queue-for-retry fallback |
| CREATE | src/UPACIP.Service/Notifications/ResilientEmailService.cs | IEmailService decorator with circuit breaker and queue-for-retry fallback |
| CREATE | src/UPACIP.Service/Notifications/NotificationRetryService.cs | BackgroundService: retry queued SMS/email when circuits close |
| MODIFY | Server/AI/AiGatewayService.cs | Integrate resilience provider pipelines for AI primary/fallback routing |
| MODIFY | src/UPACIP.Api/Program.cs | Register resilience provider, resilient services, notification retry service |
| MODIFY | src/UPACIP.Api/appsettings.json | Add ExternalServiceResilience section with per-dependency config |

## External References

- [Polly V8 — Resilience Pipelines](https://www.pollydocs.org/strategies/)
- [Polly V8 — Circuit Breaker Strategy](https://www.pollydocs.org/strategies/circuit-breaker.html)
- [Polly V8 — Retry Strategy with Jitter](https://www.pollydocs.org/strategies/retry.html)
- [Polly V8 — Timeout Strategy](https://www.pollydocs.org/strategies/timeout.html)
- [Decorator Pattern — Service Wrapping](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines#decorator-pattern)
- [Twilio SDK — .NET](https://www.twilio.com/docs/libraries/csharp-dotnet)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] SMS circuit breaker opens after 3 consecutive Twilio failures
- [ ] Email circuit breaker opens after 3 consecutive SMTP failures
- [ ] AI primary circuit breaker opens after 5 consecutive OpenAI failures and routes to Claude
- [ ] When both AI circuits are open, degradation mode is activated via `IDegradationModeManager`
- [ ] When a circuit is open, requests return fallback immediately without attempting the external call
- [ ] Failed SMS notifications are queued to `notification:sms:retry_queue` in Redis
- [ ] Failed email notifications are queued to `notification:email:retry_queue` in Redis
- [ ] `NotificationRetryService` re-attempts queued items only when circuit state is Closed/HalfOpen
- [ ] Circuit state transitions are logged via Serilog with dependency name and state change
- [ ] `GetAllCircuitStates()` returns current state for all dependencies (for monitoring integration)

## Implementation Checklist

- [ ] Create `CircuitBreakerConfig` and `ExternalServiceResilienceOptions` models in `src/UPACIP.Service/Resilience/Models/`
- [ ] Implement `IExternalServiceResilienceProvider` / `ExternalServiceResilienceProvider` with named Polly V8 pipelines
- [ ] Implement `ResilientSmsService` with SMS circuit breaker and queue-for-retry fallback
- [ ] Implement `ResilientEmailService` as `IEmailService` decorator with circuit breaker and queue-for-retry
- [ ] Integrate AI circuit breaker pipelines into `AiGatewayService` with degradation mode signaling
- [ ] Implement `NotificationRetryService` BackgroundService for queued SMS/email re-delivery
- [ ] Expose `GetAllCircuitStates()` for monitoring integration with `SystemStatusController`
- [ ] Register all services in DI and add ExternalServiceResilience configuration to appsettings.json
