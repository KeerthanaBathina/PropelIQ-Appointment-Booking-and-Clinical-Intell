# Task - task_002_ai_circuit_breaker

## Requirement Reference

- User Story: US_070
- Story Location: .propel/context/tasks/EP-012/us_070/us_070.md
- Acceptance Criteria:
    - **AC-2**: Given the circuit breaker monitors provider health, When 5 consecutive failures occur, Then the circuit breaker opens and all new requests are immediately routed to the fallback provider.
    - **AC-3**: Given the circuit breaker is open, When 30 seconds elapse, Then the circuit enters half-open state and allows one test request to determine if the provider has recovered.
    - **AC-4**: Given the half-open test succeeds, When the response is received, Then the circuit breaker closes and normal routing resumes.
- Edge Case:
    - **EC-1**: Both providers trigger circuit breakers simultaneously — System enters degraded mode, returns "AI unavailable" to all callers, and notifies admins via alert.
    - **EC-2**: Circuit breaker state across multiple server instances — Circuit breaker state is shared via Redis to ensure consistent behavior across all instances.

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
| AI Gateway | Custom .NET Service with Polly | 8.x |
| AI/ML | OpenAI GPT-4o-mini (Primary) / Claude 3.5 Sonnet (Fallback) | 2024-07-18 / claude-3-5-sonnet-20241022 |
| Caching | Upstash Redis | 7.x |
| Library | Polly (Resilience) | 8.x |
| Testing | xUnit + Moq | 2.x / 4.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O04 |
| **AI Pattern** | Gateway / Circuit Breaker |
| **Prompt Template Path** | N/A (infrastructure task, no prompt templates) |
| **Guardrails Config** | Circuit breaker configuration in appsettings.json |
| **Model Provider** | OpenAI (Primary) / Anthropic (Fallback) |

### **CRITICAL: AI Implementation Requirement (AI Tasks Only)**

**IF AI Impact = Yes:**

- **MUST** implement circuit breaker that opens after 5 consecutive failures per AIR-O04
- **MUST** implement 30-second break duration before half-open test
- **MUST** route to fallback provider (Claude 3.5 Sonnet) when primary circuit opens
- **MUST** handle dual circuit breaker failure (degraded mode) gracefully
- **MUST** share circuit breaker state via Redis across instances
- **MUST** log all circuit state transitions for audit (redact PII)
- **MUST** handle model failures gracefully (timeout, rate limit, 5xx errors)

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement a circuit breaker pattern for the AI Gateway using Polly v8 resilience library with Redis-backed state sharing. The circuit breaker monitors AI provider health and automatically routes traffic to the fallback provider (Claude 3.5 Sonnet) when the primary provider (OpenAI GPT-4o-mini) experiences consecutive failures.

**Circuit breaker behavior:**

- **Closed** (normal): Requests route to primary provider (GPT-4o-mini)
- **Open** (after 5 consecutive failures): All new requests immediately route to fallback provider (Claude 3.5 Sonnet)
- **Half-Open** (after 30 seconds): One test request sent to primary provider to check recovery
- **Degraded Mode**: If both providers fail, return "AI unavailable" error and notify admins

Circuit breaker state is shared via Redis to ensure consistent behavior across multiple server instances in a distributed deployment.

## Dependent Tasks

- US_068 tasks (OpenAI GPT-4o-mini Primary Provider Integration) - Requires primary provider integration
- US_069 tasks (Claude 3.5 Sonnet Fallback Provider Integration) - Requires fallback provider integration
- US_067 tasks (AI Gateway Foundation) - Requires base AI Gateway service scaffold
- task_001_ai_token_budget_enforcement (this US) - Token budget validation occurs before circuit breaker in the pipeline

## Impacted Components

- **NEW** `AiGateway/Configuration/CircuitBreakerOptions.cs` - Configuration POCO for circuit breaker thresholds
- **NEW** `AiGateway/Resilience/AiProviderCircuitBreaker.cs` - Polly circuit breaker wrapper with dual-provider support
- **NEW** `AiGateway/Resilience/ICircuitBreakerStateStore.cs` - Interface for distributed circuit breaker state
- **NEW** `AiGateway/Resilience/RedisCircuitBreakerStateStore.cs` - Redis-backed implementation for shared state across instances
- **NEW** `AiGateway/Models/CircuitBreakerState.cs` - State model (Closed, Open, HalfOpen, Degraded)
- **NEW** `AiGateway/Services/DegradedModeHandler.cs` - Handles dual-failure scenario with admin alerting
- **MODIFY** `AiGateway/Services/AiGatewayService.cs` - Integrate circuit breaker into provider routing pipeline
- **MODIFY** `appsettings.json` - Add CircuitBreaker configuration section

## Implementation Plan

1. **Define circuit breaker configuration**: Create `CircuitBreakerOptions` class with configurable properties: `ConsecutiveFailureThreshold` (default: 5), `BreakDurationSeconds` (default: 30), `SamplingDurationSeconds`, and Redis connection details. Use `IOptions<CircuitBreakerOptions>` for injection.

2. **Implement Polly circuit breaker for primary provider**: Configure `ResiliencePipelineBuilder` with `AddCircuitBreaker` using `CircuitBreakerStrategyOptions`:
   ```
   FailureRatio = 1.0 (consecutive failures model)
   MinimumThroughput = 5
   SamplingDuration = configurable window
   BreakDuration = 30 seconds
   ShouldHandle = HttpRequestException, TimeoutException, 5xx responses
   ```
   Use `CircuitBreakerStateProvider` to monitor state transitions.

3. **Implement Polly circuit breaker for fallback provider**: Configure a separate circuit breaker instance for Claude 3.5 Sonnet with the same threshold parameters. This enables detection of dual-provider failure.

4. **Implement fallback routing logic**: In `AiGatewayService`, wrap provider calls with circuit breaker pipelines:
   - If primary circuit is **Closed** → route to GPT-4o-mini
   - If primary circuit is **Open/HalfOpen** → route to Claude 3.5 Sonnet
   - If both circuits are **Open** → enter degraded mode

5. **Implement Redis-backed state sharing**: Create `RedisCircuitBreakerStateStore` that synchronizes circuit breaker state to Redis using atomic operations. Use Redis keys with TTL matching break duration. On each state transition, publish state to Redis; on each request, read state from Redis to ensure cross-instance consistency.

6. **Implement degraded mode handler**: When both provider circuits open:
   - Return structured 503 Service Unavailable response with "AI service temporarily unavailable" message
   - Trigger admin notification (log critical alert via Serilog for external monitoring pickup)
   - Continue checking both circuits for recovery on subsequent requests

7. **Add structured logging and metrics**: Log all circuit breaker state transitions (Closed→Open, Open→HalfOpen, HalfOpen→Closed, Degraded) with timestamps, failure counts, and provider identifiers using Serilog structured logging with correlation IDs.

8. **Configure via appsettings.json**: Add `CircuitBreaker` section with primary and fallback provider thresholds, Redis connection, and alert configuration.

## Current Project State

- [Placeholder - to be updated based on completion of dependent tasks from US_067, US_068, US_069, and task_001]

```
Server/
├── AiGateway/
│   ├── Configuration/
│   │   └── TokenBudgetOptions.cs
│   ├── Models/
│   │   ├── AiRequestType.cs
│   │   └── TokenBudgetResult.cs
│   ├── Resilience/
│   ├── Services/
│   │   ├── ITokenBudgetValidator.cs
│   │   ├── TokenBudgetValidator.cs
│   │   └── AiGatewayService.cs
│   └── ...
├── appsettings.json
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/AiGateway/Configuration/CircuitBreakerOptions.cs | Configuration POCO: ConsecutiveFailureThreshold (5), BreakDurationSeconds (30), Redis connection |
| CREATE | Server/AiGateway/Models/CircuitBreakerState.cs | Enum: Closed, Open, HalfOpen, Degraded |
| CREATE | Server/AiGateway/Resilience/AiProviderCircuitBreaker.cs | Polly circuit breaker wrapper for primary and fallback providers with state monitoring |
| CREATE | Server/AiGateway/Resilience/ICircuitBreakerStateStore.cs | Interface for distributed state store (GetStateAsync, SetStateAsync) |
| CREATE | Server/AiGateway/Resilience/RedisCircuitBreakerStateStore.cs | Redis-backed implementation using atomic GET/SET with TTL for cross-instance state |
| CREATE | Server/AiGateway/Services/DegradedModeHandler.cs | Dual-failure handler: returns 503 + triggers admin alert via structured logging |
| MODIFY | Server/AiGateway/Services/AiGatewayService.cs | Integrate circuit breaker into provider routing: primary → fallback → degraded |
| MODIFY | Server/appsettings.json | Add CircuitBreaker section with thresholds and Redis configuration |

## External References

- [Polly v8 Circuit Breaker Strategy](https://www.pollydocs.org/strategies/circuit-breaker)
- [Polly CircuitBreakerStrategyOptions API](https://github.com/App-vtte/Polly/blob/main/docs/strategies/circuit-breaker.md)
- [Polly CircuitBreakerStateProvider for state monitoring](https://www.pollydocs.org/strategies/circuit-breaker#monitoring-circuit-state)
- [StackExchange.Redis - .NET Redis client](https://stackexchange.github.io/StackExchange.Redis/)
- [Microsoft Resilience Patterns - Circuit Breaker](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/implement-circuit-breaker-pattern)
- [Serilog Structured Logging](https://serilog.net/)

## Build Commands

- `dotnet build` - Build the solution
- `dotnet test` - Run unit tests
- `dotnet run --project Server` - Run the backend server

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Circuit breaker opens after exactly 5 consecutive primary provider failures
- [ ] All new requests route to fallback (Claude) when primary circuit is open
- [ ] Circuit enters half-open state after 30 seconds and allows one test request
- [ ] Successful half-open test closes circuit and resumes normal routing to primary
- [ ] Failed half-open test re-opens circuit for another 30-second break
- [ ] Dual circuit breaker failure returns 503 "AI unavailable" response
- [ ] Admin notification triggers when entering degraded mode
- [ ] Circuit breaker state is consistent across multiple server instances via Redis
- [ ] Structured logging captures all state transitions with correlation IDs

## Implementation Checklist

- [ ] Create `CircuitBreakerOptions` configuration class with failure threshold (5), break duration (30s), sampling window, and Redis connection settings; register with `IOptions<T>`
- [ ] Create `CircuitBreakerState` enum (Closed, Open, HalfOpen, Degraded)
- [ ] Implement `AiProviderCircuitBreaker` using Polly v8 `ResiliencePipelineBuilder.AddCircuitBreaker()` with `CircuitBreakerStrategyOptions` (FailureRatio=1.0, MinimumThroughput=5, BreakDuration=30s) and `CircuitBreakerStateProvider` for monitoring
- [ ] Implement `ICircuitBreakerStateStore` and `RedisCircuitBreakerStateStore` for distributed state sharing via Redis atomic operations with TTL
- [ ] Implement fallback routing in `AiGatewayService`: primary (Closed) → fallback (Open) → degraded (both Open), catching `BrokenCircuitException` for routing decisions
- [ ] Implement `DegradedModeHandler` returning 503 Service Unavailable with structured error body and triggering admin alert via Serilog critical log
- [ ] Add `CircuitBreaker` section to `appsettings.json` with configurable thresholds for primary and fallback providers
- [ ] Add structured Serilog logging for all state transitions (Closed→Open, Open→HalfOpen, HalfOpen→Closed, Degraded) with timestamps, failure counts, and provider identifiers
- **[AI Tasks - MANDATORY]** Verify AIR-O04 requirement is met (circuit breaker opens after 5 failures, 30s retry)
