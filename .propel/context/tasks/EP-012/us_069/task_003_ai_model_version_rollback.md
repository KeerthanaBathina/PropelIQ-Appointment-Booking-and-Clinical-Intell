# Task - TASK_003

## Requirement Reference

- User Story: US_069
- Story Location: .propel/context/tasks/EP-012/us_069/us_069.md
- Requirement Tags: TR-006, AIR-O05
- Acceptance Criteria:
    - AC-4: **Given** a model version rollback is needed, **When** an admin triggers rollback, **Then** the system reverts to the previous model version within 1 hour.
- Edge Case:
    - Different response formats between providers: The AI Gateway normalizes all responses to a unified schema regardless of provider. Model version changes do not alter the response normalization layer.

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
| AI Gateway | Custom .NET Service with Polly | N/A / 8.x |
| AI/ML | OpenAI GPT-4o-mini (Primary) | 2024-07-18 |
| AI/ML | Anthropic Claude 3.5 Sonnet (Fallback) | claude-3-5-sonnet-20241022 |
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |
| Testing | xUnit + Moq | 2.x / 4.x |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O05 |
| **AI Pattern** | N/A (operational capability) |
| **Prompt Template Path** | N/A (version management layer) |
| **Guardrails Config** | N/A |
| **Model Provider** | OpenAI GPT-4o-mini (Primary) / Anthropic Claude 3.5 Sonnet (Fallback) |

### **CRITICAL: AI Implementation Requirement (AI Tasks Only)**

**IF AI Impact = Yes:**

- **MUST** implement guardrails for input sanitization and output validation
- **MUST** enforce token budget limits per AIR-O01 requirements
- **MUST** implement fallback logic for low-confidence responses
- **MUST** log all prompts/responses for audit (redact PII)
- **MUST** handle model failures gracefully (timeout, rate limit, 5xx errors)

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the model version rollback mechanism for the AI Gateway, enabling administrators to revert to a previous model version within 1 hour when accuracy degradation is detected (AIR-O05). The system maintains a version registry tracking active and previous model versions for each provider. An admin API endpoint triggers rollback by swapping the active model version to the previous one. The provider adapters hot-reload the model configuration via `IOptionsMonitor<T>` without requiring a service restart. All rollback events are captured in the audit log with the admin user, reason, timestamp, and before/after model versions.

## Dependent Tasks

- US_067 / task_001_be_ai_gateway_scaffold — Requires `AIGatewayOptions` configuration, DI infrastructure, `IAIProviderAdapter` interface.
- US_067 / task_002_ai_provider_resilience — Requires `OpenAIProviderOptions`, `ClaudeProviderOptions`, `ProviderHealthTracker`.
- US_069 / task_001_ai_claude_fallback_adapter — Requires completed `ClaudeProviderAdapter` for fallback version rollback.
- US_069 / task_002_ai_failover_routing_circuit_breaker — Requires `ProviderStateManager` for coordinating rollback with circuit breaker state.

## Impacted Components

- **NEW** `Server/Features/AIGateway/Versioning/ModelVersionRegistry.cs` — In-memory version registry tracking active/previous model versions per provider
- **NEW** `Server/Features/AIGateway/Versioning/ModelVersionEntry.cs` — Version entry model (Provider, ModelId, Version, ActivatedAt, DeactivatedAt, ActivatedBy)
- **NEW** `Server/Features/AIGateway/Versioning/IModelVersionRegistry.cs` — Version registry interface
- **NEW** `Server/Features/AIGateway/Endpoints/ModelVersionEndpoints.cs` — Admin API endpoints for version management (GET current, POST rollback)
- **NEW** `Server/Features/AIGateway/Versioning/RollbackRequest.cs` — Rollback request DTO (Provider, Reason, AdminUserId)
- **NEW** `Server/Features/AIGateway/Versioning/RollbackResponse.cs` — Rollback response DTO (Success, PreviousVersion, RolledBackVersion, Timestamp)
- **MODIFY** `Server/Features/AIGateway/Providers/OpenAIProviderAdapter.cs` — Use `IOptionsMonitor<OpenAIProviderOptions>` for hot-reload on version change
- **MODIFY** `Server/Features/AIGateway/Providers/ClaudeProviderAdapter.cs` — Use `IOptionsMonitor<ClaudeProviderOptions>` for hot-reload on version change
- **MODIFY** `Server/Features/AIGateway/Services/ProviderHealthTracker.cs` — Log rollback events with before/after metrics
- **MODIFY** `Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs` — Register version registry, endpoints, and IOptionsMonitor bindings

## Implementation Plan

1. **Implement ModelVersionRegistry** with in-memory version tracking:
   - Store `Dictionary<string, ModelVersionEntry>` keyed by provider name ("OpenAI", "Claude")
   - Each entry tracks: `ActiveVersion` (current model ID), `PreviousVersion` (rollback target), `ActivatedAt`, `ActivatedBy`
   - On initialization, populate from `IOptionsMonitor<AIGatewayOptions>` reading current model versions from `appsettings.json`
   - `GetActiveVersion(providerName)` returns current active model version string
   - `GetPreviousVersion(providerName)` returns rollback target version
   - Thread-safe operations using `ReaderWriterLockSlim` for concurrent read access

2. **Implement rollback operation** in `ModelVersionRegistry.ExecuteRollback`:
   - Validate previous version exists (reject if no previous version recorded)
   - Swap: current active → previous, previous → current active
   - Record rollback metadata: `AdminUserId`, `Reason`, `Timestamp`
   - Update provider configuration via `IConfiguration` reload mechanism
   - Return `RollbackResponse` with before/after versions and timestamp
   - Total operation time target: under 5 minutes (well within 1-hour SLA per AIR-O05)

3. **Implement admin API endpoints** via ASP.NET Core Minimal API:
   - `GET /api/admin/ai-gateway/versions` — Return current active and previous versions for all providers (requires Admin role)
   - `POST /api/admin/ai-gateway/versions/rollback` — Trigger rollback for specified provider (requires Admin role)
   - Request validation: `Provider` must be "OpenAI" or "Claude", `Reason` required (non-empty), `AdminUserId` from JWT claims
   - Response: `RollbackResponse` with HTTP 200 on success, 400 for invalid request, 409 if no previous version available
   - Authorize with `[Authorize(Roles = "Admin")]` policy

4. **Implement hot-reload in provider adapters**:
   - Replace `IOptions<T>` with `IOptionsMonitor<T>` in both `OpenAIProviderAdapter` and `ClaudeProviderAdapter`
   - Register `OnChange` callback to detect configuration changes at runtime
   - On model version change: update internal `model` parameter used in API requests
   - No service restart required — adapter reads `CurrentValue` from monitor on each request
   - Validate new model version against known supported versions before applying

5. **Implement rollback audit logging**:
   - Log structured Serilog event: `{Action: "ModelVersionRollback", Provider, PreviousVersion, NewVersion, AdminUserId, Reason, Timestamp, DurationMs}`
   - Write audit record to PostgreSQL `AuditLog` table via existing audit infrastructure
   - Include rollback in daily operations summary report

6. **Coordinate rollback with circuit breaker state**:
   - If provider is in `Unavailable` state (circuit open), rollback triggers circuit breaker reset to `HalfOpen` for immediate probe with new version
   - If provider is `Active`, rollback applies immediately to next request
   - Log coordination event: `{Action: "RollbackCircuitReset", Provider, CircuitStateBeforeRollback, CircuitStateAfterRollback}`

## Current Project State

```text
[Placeholder — update after preceding tasks are complete]
Server/
├── Features/
│   └── AIGateway/
│       ├── Contracts/                         ← US_067/task_001
│       ├── Middleware/                         ← US_067/task_001
│       ├── Services/
│       │   ├── AIGatewayService.cs            ← US_069/task_002
│       │   └── ProviderHealthTracker.cs       ← US_069/task_002
│       ├── Configuration/                     ← US_067/task_001 + task_002
│       ├── Extensions/                        ← US_067/task_001
│       ├── Providers/
│       │   ├── OpenAIProviderAdapter.cs       ← US_068
│       │   ├── ClaudeProviderAdapter.cs       ← US_069/task_001
│       │   ├── ClaudeResponseMapper.cs        ← US_069/task_001
│       │   └── ClaudeRequestBuilder.cs        ← US_069/task_001
│       ├── Resilience/
│       │   ├── AIResiliencePipelineBuilder.cs ← US_069/task_002
│       │   ├── AIProviderFallbackHandler.cs   ← US_069/task_002
│       │   ├── ProviderStateManager.cs        ← US_069/task_002
│       │   └── CircuitBreakerStateMonitor.cs  ← US_069/task_002
│       ├── Queue/                             ← US_067/task_003
│       └── Versioning/                        ← NEW (this task)
│           ├── IModelVersionRegistry.cs
│           ├── ModelVersionRegistry.cs
│           ├── ModelVersionEntry.cs
│           ├── RollbackRequest.cs
│           └── RollbackResponse.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Features/AIGateway/Versioning/IModelVersionRegistry.cs | Version registry interface with GetActiveVersion, GetPreviousVersion, ExecuteRollback methods |
| CREATE | Server/Features/AIGateway/Versioning/ModelVersionRegistry.cs | Thread-safe in-memory version registry with rollback logic and configuration reload |
| CREATE | Server/Features/AIGateway/Versioning/ModelVersionEntry.cs | Version entry model (Provider, ModelId, Version, ActivatedAt, DeactivatedAt, ActivatedBy) |
| CREATE | Server/Features/AIGateway/Versioning/RollbackRequest.cs | Rollback request DTO (Provider, Reason, AdminUserId) |
| CREATE | Server/Features/AIGateway/Versioning/RollbackResponse.cs | Rollback response DTO (Success, PreviousVersion, RolledBackVersion, Timestamp) |
| CREATE | Server/Features/AIGateway/Endpoints/ModelVersionEndpoints.cs | Admin API: GET /api/admin/ai-gateway/versions, POST /api/admin/ai-gateway/versions/rollback |
| MODIFY | Server/Features/AIGateway/Providers/OpenAIProviderAdapter.cs | Replace IOptions with IOptionsMonitor for hot-reload, add OnChange callback |
| MODIFY | Server/Features/AIGateway/Providers/ClaudeProviderAdapter.cs | Replace IOptions with IOptionsMonitor for hot-reload, add OnChange callback |
| MODIFY | Server/Features/AIGateway/Services/ProviderHealthTracker.cs | Add rollback event logging with before/after metrics |
| MODIFY | Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs | Register IModelVersionRegistry, ModelVersionEndpoints, IOptionsMonitor bindings |

## External References

- [IOptionsMonitor — Microsoft Docs](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#ioptionsmonitor)
- [ASP.NET Core Minimal APIs — Microsoft Docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- [ASP.NET Core Authorization — Role-Based](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- [Configuration Reload — Microsoft Docs](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration#configuration-reload)
- [OpenAI Model Versioning — Platform Docs](https://platform.openai.com/docs/models)
- [Anthropic Model Versioning — API Docs](https://docs.anthropic.com/en/docs/about-claude/models)

### IOptionsMonitor Hot-Reload Pattern Reference

```csharp
// Hot-reload pattern for provider adapters
public class ClaudeProviderAdapter : IAIProviderAdapter
{
    private readonly IOptionsMonitor<ClaudeProviderOptions> _optionsMonitor;
    private readonly ILogger<ClaudeProviderAdapter> _logger;

    public ClaudeProviderAdapter(
        IOptionsMonitor<ClaudeProviderOptions> optionsMonitor,
        ILogger<ClaudeProviderAdapter> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;

        // Register hot-reload callback
        _optionsMonitor.OnChange(options =>
        {
            _logger.LogInformation(
                "Claude model version updated: {Model}",
                options.Model);
        });
    }

    public string ModelVersion => _optionsMonitor.CurrentValue.Model;

    public async Task<AIResponse> SendCompletionAsync(
        AIRequest request, CancellationToken ct)
    {
        // Always reads CurrentValue — reflects latest configuration
        var options = _optionsMonitor.CurrentValue;
        // ... use options.Model for API request
    }
}
```

## Build Commands

- `dotnet build` — Compile with model version management
- `dotnet run` — Start backend with rollback capability
- `dotnet ef migrations add AddModelVersionAuditLog` — If audit log schema update needed

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Rollback endpoint returns correct before/after versions
- [ ] Hot-reload confirmed: model version change applies without restart
- [ ] Rollback audit log entry persisted to database
- [ ] Rollback operation completes within 5 minutes (well within 1-hour SLA)

## Implementation Checklist

- [ ] Implement `IModelVersionRegistry` interface and `ModelVersionRegistry` with thread-safe version tracking (active/previous per provider) using `ReaderWriterLockSlim`
- [ ] Implement `ExecuteRollback` method with version swap, validation, configuration reload, and `RollbackResponse` generation
- [ ] Create admin API endpoints (`GET /api/admin/ai-gateway/versions`, `POST /api/admin/ai-gateway/versions/rollback`) with Admin role authorization and request validation
- [ ] Refactor `OpenAIProviderAdapter` and `ClaudeProviderAdapter` to use `IOptionsMonitor<T>` with `OnChange` callback for hot-reload model version updates
- [ ] Implement rollback audit logging via structured Serilog events and PostgreSQL `AuditLog` table write
- [ ] Coordinate rollback with `ProviderStateManager` — reset circuit breaker to `HalfOpen` when rolling back an unavailable provider
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-O05 requirement is met (rollback within 1 hour)
