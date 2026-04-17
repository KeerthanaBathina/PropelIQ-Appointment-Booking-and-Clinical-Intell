# Task - TASK_002

## Requirement Reference

- User Story: US_069
- Story Location: .propel/context/tasks/EP-012/us_069/us_069.md
- Requirement Tags: TR-006, AIR-O05
- Acceptance Criteria:
    - AC-1: **Given** the primary provider (OpenAI) fails or is unavailable, **When** the AI Gateway detects the failure, **Then** requests are automatically routed to Claude 3.5 Sonnet as fallback.
    - AC-3: **Given** the primary provider recovers, **When** the circuit breaker resets, **Then** the AI Gateway resumes routing to the primary provider for new requests.
- Edge Case:
    - In-flight requests during provider switchover: In-flight requests complete on the original provider; only new requests are routed to fallback. Implemented via per-request provider binding at dispatch time.
    - Both providers unavailable: System returns structured error `AIResponse` with `Success = false` and `ErrorMessage = "AI service unavailable"`. Caller falls back to manual workflow per NFR-022.

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
| Monitoring | Serilog + Seq | 8.x / 2024.x |
| Testing | xUnit + Moq | 2.x / 4.x |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O04, AIR-O08, AIR-O05 |
| **AI Pattern** | Hybrid (RAG + Tool Calling + Classification) |
| **Prompt Template Path** | N/A (resilience infrastructure layer) |
| **Guardrails Config** | N/A (guardrails in EP-013 tasks) |
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

Implement the automatic failover routing logic and circuit breaker recovery behavior within the AI Gateway. When the primary provider (OpenAI GPT-4o-mini) fails, the Polly resilience pipeline detects the failure and routes new requests to Claude 3.5 Sonnet. When the primary recovers, the circuit breaker transitions through half-open state, probes the primary with a test request, and resumes full primary routing upon success (AC-3). In-flight requests are bound to their dispatched provider at invocation time and complete on the original provider regardless of circuit breaker state changes during execution. The task also integrates provider state event publishing for observability, enabling real-time monitoring of failover/recovery transitions.

## Dependent Tasks

- US_067 / task_001_be_ai_gateway_scaffold — Requires `AIGatewayService`, `IAIProviderAdapter` interface, DI infrastructure.
- US_067 / task_002_ai_provider_resilience — Requires `AIResiliencePipelineBuilder`, `AIProviderFallbackHandler`, Polly pipeline configuration, `ProviderHealthTracker`.
- US_069 / task_001_ai_claude_fallback_adapter — Requires completed `ClaudeProviderAdapter` with full API integration.

## Impacted Components

- **MODIFY** `Server/Features/AIGateway/Resilience/AIProviderFallbackHandler.cs` — Implement full failover routing with provider binding, recovery detection, and dual-provider unavailability handling
- **MODIFY** `Server/Features/AIGateway/Resilience/AIResiliencePipelineBuilder.cs` — Configure half-open probing strategy, circuit breaker state transition callbacks
- **NEW** `Server/Features/AIGateway/Resilience/ProviderStateManager.cs` — Centralized provider state tracking (Active, Degraded, Unavailable) with thread-safe transitions
- **NEW** `Server/Features/AIGateway/Resilience/CircuitBreakerStateMonitor.cs` — Monitor and publish circuit breaker state transitions (Closed → Open → HalfOpen → Closed)
- **MODIFY** `Server/Features/AIGateway/Services/AIGatewayService.cs` — Integrate provider state manager into request routing, bind provider at dispatch time for in-flight isolation
- **MODIFY** `Server/Features/AIGateway/Services/ProviderHealthTracker.cs` — Add circuit breaker state metrics, failover event counters, recovery time tracking

## Implementation Plan

1. **Implement ProviderStateManager** for centralized provider state tracking:
   - Thread-safe state machine with states: `Active`, `Degraded`, `Unavailable` per provider
   - `Active`: Provider receiving traffic normally (circuit closed)
   - `Degraded`: Provider experiencing intermittent failures (circuit half-open, probing)
   - `Unavailable`: Provider circuit open (all traffic routed to fallback)
   - State transitions triggered by Polly circuit breaker callbacks
   - Expose `GetActiveProvider()` returning current preferred provider based on state

2. **Configure circuit breaker state transition callbacks** in `AIResiliencePipelineBuilder`:
   - `OnOpened` (Closed → Open): Publish "Primary Unavailable" event, update `ProviderStateManager` to `Unavailable`, log failure count and last error
   - `OnHalfOpened` (Open → HalfOpen after 30s): Publish "Primary Probing" event, update state to `Degraded`, allow single probe request to primary
   - `OnClosed` (HalfOpen → Closed after successful probe): Publish "Primary Recovered" event, update state to `Active`, log recovery duration
   - All callbacks include correlation IDs and timestamps for audit trail

3. **Implement in-flight request isolation** in `AIGatewayService.SendCompletionAsync`:
   - At request dispatch time, capture current `activeProvider` reference from `ProviderStateManager`
   - Bind request to captured provider instance — request completes on this provider regardless of state changes during execution
   - Use `CancellationToken` for cooperative cancellation; do NOT abort in-flight requests on failover
   - New requests arriving after failover use updated `activeProvider` from `ProviderStateManager`

4. **Implement CircuitBreakerStateMonitor** for observability:
   - Subscribe to `ProviderStateManager` state change events
   - Emit structured Serilog events for each transition: `{Provider, FromState, ToState, Timestamp, FailureCount, RecoveryDurationMs}`
   - Track metrics: total failover count, average recovery time, current provider state
   - Expose `/health/ai-providers` endpoint returning current state of all providers (for operations dashboard)

5. **Implement dual-provider unavailability handling** in `AIProviderFallbackHandler`:
   - If primary circuit open AND fallback `SendCompletionAsync` throws exception → return structured error `AIResponse`
   - `AIResponse.Success = false`, `ErrorMessage = "AI service unavailable — both providers failed. Fallback to manual workflow."`
   - Log critical alert with both provider failure details
   - Caller (document parser, coding service, intake service) checks `AIResponse.Success` and routes to manual workflow per NFR-022

6. **Update ProviderHealthTracker** with circuit breaker metrics:
   - Track per-provider: circuit state (Closed/Open/HalfOpen), time in current state, state transition count
   - Track failover events: count, average duration before recovery, longest outage
   - Log daily summary: primary uptime percentage, fallback usage percentage, total failover events

## Current Project State

```text
[Placeholder — update after US_067 tasks and task_001 of this story are complete]
Server/
├── Features/
│   └── AIGateway/
│       ├── Contracts/                         ← US_067/task_001
│       ├── Middleware/                         ← US_067/task_001
│       ├── Services/
│       │   ├── AIGatewayService.cs            ← US_067/task_001 + task_002
│       │   └── ProviderHealthTracker.cs       ← US_067/task_002
│       ├── Configuration/                     ← US_067/task_001 + task_002
│       ├── Extensions/                        ← US_067/task_001
│       ├── Providers/
│       │   ├── OpenAIProviderAdapter.cs       ← US_067/task_002 + US_068
│       │   ├── ClaudeProviderAdapter.cs       ← US_069/task_001
│       │   ├── ClaudeResponseMapper.cs        ← US_069/task_001
│       │   └── ClaudeRequestBuilder.cs        ← US_069/task_001
│       ├── Resilience/
│       │   ├── AIResiliencePipelineBuilder.cs ← US_067/task_002
│       │   └── AIProviderFallbackHandler.cs   ← US_067/task_002
│       └── Queue/                             ← US_067/task_003
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Features/AIGateway/Resilience/AIProviderFallbackHandler.cs | Add dual-provider unavailability handling, structured error response when both providers fail |
| MODIFY | Server/Features/AIGateway/Resilience/AIResiliencePipelineBuilder.cs | Configure OnOpened/OnHalfOpened/OnClosed callbacks for circuit breaker state transitions |
| CREATE | Server/Features/AIGateway/Resilience/ProviderStateManager.cs | Thread-safe provider state machine (Active/Degraded/Unavailable) with event publishing |
| CREATE | Server/Features/AIGateway/Resilience/CircuitBreakerStateMonitor.cs | Monitor and log circuit breaker transitions, expose /health/ai-providers endpoint |
| MODIFY | Server/Features/AIGateway/Services/AIGatewayService.cs | Integrate ProviderStateManager for request routing, bind provider at dispatch time for in-flight isolation |
| MODIFY | Server/Features/AIGateway/Services/ProviderHealthTracker.cs | Add circuit breaker state metrics, failover counters, recovery time tracking |

## External References

- [Polly V8 Circuit Breaker Strategy — GitHub Docs](https://github.com/App-vNext/Polly/blob/main/docs/strategies/circuit-breaker.md)
- [Polly V8 Circuit Breaker Events — OnOpened, OnHalfOpened, OnClosed](https://github.com/App-vNext/Polly/blob/main/docs/strategies/circuit-breaker.md#defaults)
- [Microsoft Resilience Patterns — Circuit Breaker](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/implement-circuit-breaker-pattern)
- [Polly V8 Fallback Strategy — GitHub Docs](https://github.com/App-vNext/Polly/blob/main/docs/strategies/fallback.md)

### Polly V8 Reference — Circuit Breaker State Callbacks

```csharp
// Circuit breaker with state transition callbacks for provider state management
.AddCircuitBreaker(new CircuitBreakerStrategyOptions<AIResponse>
{
    FailureRatio = 1.0,
    MinimumThroughput = 5,                              // AIR-O04: 5 consecutive failures
    SamplingDuration = TimeSpan.FromSeconds(30),
    BreakDuration = TimeSpan.FromSeconds(30),            // AIR-O04: retry after 30s
    OnOpened = args =>
    {
        // Circuit opened: primary unavailable, route all new requests to fallback
        providerStateManager.TransitionTo("OpenAI", ProviderState.Unavailable);
        logger.LogWarning("Circuit OPENED for OpenAI. Routing to Claude fallback.");
        return ValueTask.CompletedTask;
    },
    OnHalfOpened = args =>
    {
        // Circuit half-open: probe primary with single request
        providerStateManager.TransitionTo("OpenAI", ProviderState.Degraded);
        logger.LogInformation("Circuit HALF-OPEN for OpenAI. Probing primary...");
        return ValueTask.CompletedTask;
    },
    OnClosed = args =>
    {
        // Circuit closed: primary recovered, resume normal routing
        providerStateManager.TransitionTo("OpenAI", ProviderState.Active);
        logger.LogInformation("Circuit CLOSED for OpenAI. Primary recovered.");
        return ValueTask.CompletedTask;
    }
})
```

## Build Commands

- `dotnet build` — Compile with failover routing and circuit breaker recovery
- `dotnet run` — Start backend with full provider failover lifecycle

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Circuit breaker state transitions verified: Closed → Open → HalfOpen → Closed
- [ ] In-flight request isolation confirmed: requests complete on dispatched provider
- [ ] Dual-provider unavailability returns structured error response

## Implementation Checklist

- [ ] Implement `ProviderStateManager` with thread-safe state machine (Active/Degraded/Unavailable) and `GetActiveProvider()` method
- [ ] Configure Polly circuit breaker `OnOpened`/`OnHalfOpened`/`OnClosed` callbacks in `AIResiliencePipelineBuilder` to drive `ProviderStateManager` transitions
- [ ] Implement in-flight request isolation in `AIGatewayService.SendCompletionAsync` — capture provider reference at dispatch time, bind request to captured provider
- [ ] Implement `CircuitBreakerStateMonitor` with structured Serilog events for state transitions and `/health/ai-providers` health endpoint
- [ ] Implement dual-provider unavailability handling in `AIProviderFallbackHandler` — return structured error `AIResponse` with "AI service unavailable" when both fail
- [ ] Update `ProviderHealthTracker` with circuit breaker state metrics, failover counters, and recovery time tracking
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-O04, AIR-O08, AIR-O05 requirements are met
