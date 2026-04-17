# Task - TASK_002

## Requirement Reference

- User Story: US_067
- Story Location: .propel/context/tasks/EP-012/us_067/us_067.md
- Acceptance Criteria:
    - AC-1: **Given** the AI Gateway service is scaffolded, **When** a developer inspects the architecture, **Then** it follows the vertical slice pattern with provider abstraction, request routing, and Polly resilience policies.
    - AC-2: **Given** the AI Gateway receives a request, **When** the primary provider fails, **Then** Polly automatically routes to the fallback provider without caller awareness.
- Edge Case:
    - Both primary and fallback providers unavailable: System returns a structured error response with "AI service unavailable" and the caller falls back to manual workflow.
    - Provider API version changes: Provider adapters are versioned independently; version mismatch triggers configuration alert without blocking requests.

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

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O01, AIR-O02, AIR-O03, AIR-O04, AIR-O05, AIR-O08, AIR-O09 |
| **AI Pattern** | Hybrid (RAG + Tool Calling + Classification) |
| **Prompt Template Path** | N/A (provider infrastructure layer) |
| **Guardrails Config** | N/A (guardrails in EP-013 tasks) |
| **Model Provider** | OpenAI GPT-4o-mini (Primary) / Anthropic Claude 3.5 Sonnet (Fallback) |

### **CRITICAL: AI Implementation Requirement (AI Tasks Only)**

**IF AI Impact = Yes:**

- **MUST** implement guardrails for input sanitization and output validation
- **MUST** enforce token budget limits per AIR-O01, AIR-O02, AIR-O03 requirements
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

Implement the OpenAI GPT-4o-mini and Anthropic Claude 3.5 Sonnet provider adapters against the `IAIProviderAdapter` interface, and configure the complete Polly V8 resilience pipeline (circuit breaker, retry with exponential backoff, fallback routing). This task delivers the core resilience behavior: when the primary provider (OpenAI) fails, Polly automatically routes to the fallback provider (Claude) without caller awareness. When both providers are unavailable, the system returns a structured error response enabling caller fallback to manual workflow.

The task also implements token budget enforcement per request type (AIR-O01/O02/O03), provider configuration via `appsettings.json`, model version management with rollback support (AIR-O05), and cost-per-request tracking for daily monitoring (AIR-O09).

## Dependent Tasks

- task_001_be_ai_gateway_scaffold — Requires IAIProviderAdapter interface, AIRequest/AIResponse DTOs, AIGatewayOptions configuration, and DI infrastructure.

## Impacted Components

- **NEW** `Features/AIGateway/Providers/OpenAIProviderAdapter.cs` — GPT-4o-mini provider implementation
- **NEW** `Features/AIGateway/Providers/ClaudeProviderAdapter.cs` — Claude 3.5 Sonnet fallback provider implementation
- **NEW** `Features/AIGateway/Resilience/AIResiliencePipelineBuilder.cs` — Polly V8 resilience pipeline configuration
- **NEW** `Features/AIGateway/Resilience/AIProviderFallbackHandler.cs` — Fallback routing logic between providers
- **NEW** `Features/AIGateway/Configuration/OpenAIProviderOptions.cs` — OpenAI-specific configuration
- **NEW** `Features/AIGateway/Configuration/ClaudeProviderOptions.cs` — Claude-specific configuration
- **NEW** `Features/AIGateway/Configuration/ResilienceOptions.cs` — Polly resilience policy configuration
- **NEW** `Features/AIGateway/Services/ProviderHealthTracker.cs` — Provider health and cost tracking service
- **MODIFY** `Features/AIGateway/Services/AIGatewayService.cs` — Wire resilience pipeline into request routing
- **MODIFY** `Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs` — Register providers and resilience policies
- **MODIFY** `Server/appsettings.json` — Add provider-specific configuration sections

## Implementation Plan

1. **Implement OpenAI GPT-4o-mini provider adapter** (`OpenAIProviderAdapter : IAIProviderAdapter`):
   - Configure `HttpClient` with base URL `https://api.openai.com/v1/chat/completions`
   - Map `AIRequest` to OpenAI Chat Completion API request format (model: `gpt-4o-mini`, messages array, max_tokens, temperature)
   - Parse OpenAI response to extract content, token usage (prompt_tokens, completion_tokens), and map to `AIResponse`
   - Implement `IsHealthyAsync` via lightweight models endpoint check
   - Handle OpenAI-specific errors: 429 (rate limit), 500/503 (server errors), timeout

2. **Implement Claude 3.5 Sonnet fallback provider adapter** (`ClaudeProviderAdapter : IAIProviderAdapter`):
   - Configure `HttpClient` with base URL `https://api.anthropic.com/v1/messages`
   - Map `AIRequest` to Anthropic Messages API format (model: `claude-3-5-sonnet-20241022`, messages, max_tokens, system message)
   - Parse Anthropic response to extract content, token usage (input_tokens, output_tokens), and map to `AIResponse`
   - Implement `IsHealthyAsync` via API availability check
   - Handle Anthropic-specific errors: 429 (rate limit), 529 (overloaded), 500 (server errors)

3. **Configure Polly V8 circuit breaker policy** using `ResiliencePipelineBuilder<AIResponse>`:
   - `FailureRatio`: 1.0 (all failures count — maps to "5 consecutive failures" by setting `MinimumThroughput: 5`)
   - `SamplingDuration`: 30 seconds
   - `MinimumThroughput`: 5 (AIR-O04: open after 5 consecutive failures)
   - `BreakDuration`: 30 seconds (AIR-O04: retry after 30s)
   - `ShouldHandle`: `HttpRequestException`, `TaskCanceledException` (timeout), non-success AIResponse
   - `OnOpened` callback: Log circuit opened with provider name and failure count

4. **Configure Polly V8 retry policy** with exponential backoff:
   - `MaxRetryAttempts`: 3 (AIR-O08)
   - `BackoffType`: `DelayBackoffType.Exponential`
   - `Delay`: 1 second base
   - `UseJitter`: true (prevent thundering herd)
   - `ShouldHandle`: `HttpRequestException`, 429/500/503 status codes
   - `OnRetry` callback: Log retry attempt with delay and error reason

5. **Implement Polly V8 fallback policy** chain:
   - Outer fallback wraps entire primary provider pipeline
   - On primary (OpenAI) circuit break or exhausted retries → route to fallback (Claude) provider
   - On fallback (Claude) failure → return structured error `AIResponse` with `Success = false`, `ErrorMessage = "AI service unavailable — fallback to manual workflow"`
   - Caller receives `AIResponse` transparently — no awareness of provider switching (AC-2)

6. **Implement token budget enforcement** at provider adapter level:
   - Before sending request, validate token counts against budget: DocumentParsing (4K in / 1K out), ConversationalIntake (500 in / 200 out), MedicalCoding (2K in / 500 out)
   - Reject over-budget requests with `AIResponse.Success = false` and descriptive error
   - Log token budget violations for monitoring

7. **Add provider configuration via `appsettings.json`**:
   ```json
   {
     "AIGateway": {
       "Providers": {
         "OpenAI": { "ApiKey": "", "BaseUrl": "https://api.openai.com/v1", "Model": "gpt-4o-mini", "ModelVersion": "2024-07-18", "TimeoutSeconds": 30 },
         "Claude": { "ApiKey": "", "BaseUrl": "https://api.anthropic.com/v1", "Model": "claude-3-5-sonnet-20241022", "AnthropicVersion": "2023-06-01", "TimeoutSeconds": 30 }
       },
       "Resilience": { "CircuitBreakerFailureThreshold": 5, "CircuitBreakerBreakDurationSeconds": 30, "RetryMaxAttempts": 3, "RetryBaseDelaySeconds": 1 }
     }
   }
   ```

8. **Implement provider health tracking and cost logging**:
   - Track per-provider: request count, success/failure rate, average latency, total tokens consumed
   - Calculate estimated cost per request: OpenAI ($0.15/1M input, $0.60/1M output), Claude ($3.00/1M input, $15.00/1M output)
   - Log daily cost summary via Serilog structured properties
   - Support model version rollback: configuration change triggers adapter re-initialization without restart (AIR-O05)

## Current Project State

```
[Placeholder — update after task_001 scaffold is complete]
Server/
├── Program.cs
├── appsettings.json
├── Features/
│   └── AIGateway/
│       ├── Contracts/
│       │   ├── IAIProviderAdapter.cs      ← from task_001
│       │   ├── AIRequest.cs               ← from task_001
│       │   ├── AIResponse.cs              ← from task_001
│       │   ├── AIRequestType.cs           ← from task_001
│       │   └── TokenBudget.cs             ← from task_001
│       ├── Middleware/                     ← from task_001
│       ├── Services/
│       │   ├── IAIGatewayService.cs       ← from task_001
│       │   └── AIGatewayService.cs        ← from task_001
│       ├── Configuration/
│       │   └── AIGatewayOptions.cs        ← from task_001
│       ├── Extensions/                    ← from task_001
│       ├── Providers/                     ← NEW (this task)
│       │   ├── OpenAIProviderAdapter.cs
│       │   └── ClaudeProviderAdapter.cs
│       └── Resilience/                    ← NEW (this task)
│           ├── AIResiliencePipelineBuilder.cs
│           └── AIProviderFallbackHandler.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Features/AIGateway/Providers/OpenAIProviderAdapter.cs | GPT-4o-mini IAIProviderAdapter implementation with HTTP client, request/response mapping |
| CREATE | Server/Features/AIGateway/Providers/ClaudeProviderAdapter.cs | Claude 3.5 Sonnet IAIProviderAdapter implementation with HTTP client, request/response mapping |
| CREATE | Server/Features/AIGateway/Resilience/AIResiliencePipelineBuilder.cs | Polly V8 ResiliencePipeline configuration: circuit breaker + retry + fallback chain |
| CREATE | Server/Features/AIGateway/Resilience/AIProviderFallbackHandler.cs | Fallback routing logic: primary → fallback → structured error response |
| CREATE | Server/Features/AIGateway/Configuration/OpenAIProviderOptions.cs | OpenAI config POCO (ApiKey, BaseUrl, Model, ModelVersion, Timeout) |
| CREATE | Server/Features/AIGateway/Configuration/ClaudeProviderOptions.cs | Claude config POCO (ApiKey, BaseUrl, Model, AnthropicVersion, Timeout) |
| CREATE | Server/Features/AIGateway/Configuration/ResilienceOptions.cs | Polly config POCO (CircuitBreaker thresholds, Retry settings) |
| CREATE | Server/Features/AIGateway/Services/ProviderHealthTracker.cs | Per-provider health metrics and cost-per-request tracking |
| MODIFY | Server/Features/AIGateway/Services/AIGatewayService.cs | Wire ResiliencePipeline into SendCompletionAsync request flow |
| MODIFY | Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs | Register providers, resilience pipeline, health tracker |
| MODIFY | Server/appsettings.json | Add Providers and Resilience configuration sections |

## External References

- [Polly V8 Resilience Pipelines — GitHub](https://github.com/App-vNext/Polly/blob/main/docs/getting-started.md)
- [Polly Circuit Breaker Strategy — Docs](https://github.com/App-vNext/Polly/blob/main/docs/strategies/circuit-breaker.md)
- [Polly Retry Strategy — Docs](https://github.com/App-vNext/Polly/blob/main/docs/strategies/retry.md)
- [Polly Fallback Strategy — Docs](https://github.com/App-vNext/Polly/blob/main/docs/strategies/fallback.md)
- [OpenAI Chat Completions API — Docs](https://platform.openai.com/docs/api-reference/chat/create)
- [Anthropic Messages API — Docs](https://docs.anthropic.com/en/api/messages)
- [Microsoft.Extensions.Http.Resilience — NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Http.Resilience)

### Polly V8 Reference — Resilience Pipeline Pattern

```csharp
// Polly V8 comprehensive pipeline: Fallback → CircuitBreaker → Retry → Timeout
var pipeline = new ResiliencePipelineBuilder<AIResponse>()
    .AddFallback(new FallbackStrategyOptions<AIResponse>
    {
        ShouldHandle = new PredicateBuilder<AIResponse>()
            .Handle<BrokenCircuitException>()
            .Handle<HttpRequestException>(),
        FallbackAction = args => /* route to Claude fallback */
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions<AIResponse>
    {
        FailureRatio = 1.0,
        MinimumThroughput = 5,          // AIR-O04: 5 consecutive failures
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration = TimeSpan.FromSeconds(30)  // AIR-O04: retry after 30s
    })
    .AddRetry(new RetryStrategyOptions<AIResponse>
    {
        MaxRetryAttempts = 3,            // AIR-O08: max 3 retries
        BackoffType = DelayBackoffType.Exponential,
        Delay = TimeSpan.FromSeconds(1),
        UseJitter = true
    })
    .Build();
```

## Build Commands

- `dotnet add package Microsoft.Extensions.Http.Polly` — Add Polly HTTP integration
- `dotnet add package Polly` — Add Polly V8 resilience library
- `dotnet build` — Compile with provider adapters and resilience pipeline
- `dotnet run` — Start backend with full AI Gateway resilience

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified (AIR-O01: 4K/1K, AIR-O02: 500/200, AIR-O03: 2K/500)
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Circuit breaker opens after 5 consecutive failures (AIR-O04)
- [ ] Circuit breaker transitions to half-open after 30 seconds (AIR-O04)
- [ ] Retry policy executes max 3 attempts with exponential backoff (AIR-O08)
- [ ] Fallback routes from OpenAI to Claude transparently when primary fails (AC-2)
- [ ] Both providers unavailable returns structured "AI service unavailable" error
- [ ] Provider health metrics and cost tracking log correctly via Serilog

## Implementation Checklist

- [ ] Implement OpenAI GPT-4o-mini provider adapter (IAIProviderAdapter) with HttpClient configuration, request/response mapping, and error handling (429, 500, 503, timeout)
- [ ] Implement Anthropic Claude 3.5 Sonnet fallback provider adapter (IAIProviderAdapter) with HttpClient configuration, request/response mapping, and error handling (429, 529, 500)
- [ ] Configure Polly V8 circuit breaker policy (open after 5 consecutive failures within 30s sampling, half-open retry after 30s break duration per AIR-O04)
- [ ] Configure Polly V8 retry policy with exponential backoff (max 3 retries, 1s base delay, jitter enabled per AIR-O08)
- [ ] Implement Polly V8 fallback policy chain routing primary (OpenAI) → fallback (Claude) → structured error response transparently (AC-2)
- [ ] Implement token budget enforcement per request type at provider adapter level (AIR-O01: 4K/1K parsing, AIR-O02: 500/200 intake, AIR-O03: 2K/500 coding)
- [ ] Add provider configuration via appsettings.json (API keys, base URLs, model versions, timeout, resilience thresholds) with IOptions<T> binding
- [ ] Implement provider health tracking and cost-per-request logging (request count, success rate, latency, token usage, estimated cost per AIR-O09)
- **[AI Tasks - MANDATORY]** Verify AIR-O04 circuit breaker and AIR-O08 retry policies function correctly under failure conditions
- **[AI Tasks - MANDATORY]** Implement and test guardrails: token budget rejection, structured error on both providers unavailable
- **[AI Tasks - MANDATORY]** Verify AIR-O01, AIR-O02, AIR-O03 token budget limits enforced at provider level
