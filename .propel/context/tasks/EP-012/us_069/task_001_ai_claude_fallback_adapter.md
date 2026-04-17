# Task - TASK_001

## Requirement Reference

- User Story: US_069
- Story Location: .propel/context/tasks/EP-012/us_069/us_069.md
- Requirement Tags: TR-006, AIR-O05
- Acceptance Criteria:
    - AC-1: **Given** the primary provider (OpenAI) fails or is unavailable, **When** the AI Gateway detects the failure, **Then** requests are automatically routed to Claude 3.5 Sonnet as fallback.
    - AC-2: **Given** the fallback provider is active, **When** requests are processed, **Then** the same token budget limits and response format apply as with the primary provider.
- Edge Case:
    - Different response formats between providers: The AI Gateway normalizes all responses to a unified schema regardless of provider. Claude Messages API responses are mapped to the same `AIResponse` DTO as OpenAI Chat Completion responses.
    - In-flight requests during provider switchover: In-flight requests complete on the original provider; only new requests are routed to fallback.

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
| AI/ML | Anthropic Claude 3.5 Sonnet (Fallback) | claude-3-5-sonnet-20241022 |
| Monitoring | Serilog + Seq | 8.x / 2024.x |
| Testing | xUnit + Moq | 2.x / 4.x |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O01, AIR-O02, AIR-O03, AIR-O05 |
| **AI Pattern** | Hybrid (RAG + Tool Calling + Classification) |
| **Prompt Template Path** | N/A (provider adapter layer) |
| **Guardrails Config** | N/A (guardrails in EP-013 tasks) |
| **Model Provider** | Anthropic Claude 3.5 Sonnet (Fallback) |

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

Complete the Anthropic Claude 3.5 Sonnet provider adapter (`ClaudeProviderAdapter`) scaffolded in US_067/task_002, implementing full Anthropic Messages API integration with response normalization to the unified `AIResponse` schema. This task ensures the fallback provider produces identical response structures and enforces the same per-request token budgets as the primary OpenAI provider (AIR-O01, AIR-O02, AIR-O03). The adapter handles Claude-specific API semantics (system message as top-level parameter, `anthropic-version` header, content block array responses) and maps them transparently to the gateway's unified contract. Structured logging with correlation IDs captures all fallback invocations for audit and cost tracking.

## Dependent Tasks

- US_067 / task_001_be_ai_gateway_scaffold — Requires `IAIProviderAdapter` interface, `AIRequest`/`AIResponse` DTOs, `TokenBudget` configuration, DI infrastructure.
- US_067 / task_002_ai_provider_resilience — Requires `ClaudeProviderAdapter` skeleton, `ClaudeProviderOptions` configuration POCO, `ProviderHealthTracker` service.
- US_068 — Requires primary OpenAI provider integration for fallback context (response format baseline, token budget baselines).

## Impacted Components

- **MODIFY** `Server/Features/AIGateway/Providers/ClaudeProviderAdapter.cs` — Complete Claude Messages API integration with full request/response mapping
- **NEW** `Server/Features/AIGateway/Providers/ClaudeResponseMapper.cs` — Dedicated response mapper: Anthropic content blocks → unified `AIResponse`
- **NEW** `Server/Features/AIGateway/Providers/ClaudeRequestBuilder.cs` — Dedicated request builder: `AIRequest` → Anthropic Messages API payload
- **MODIFY** `Server/Features/AIGateway/Configuration/ClaudeProviderOptions.cs` — Add fallback-specific settings (FallbackPriority, MaxConcurrentFallbackRequests)
- **MODIFY** `Server/Features/AIGateway/Services/ProviderHealthTracker.cs` — Add Claude-specific cost metrics ($3.00/1M input, $15.00/1M output)
- **MODIFY** `Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs` — Register Claude mapper and builder services

## Implementation Plan

1. **Implement Claude request builder** (`ClaudeRequestBuilder`):
   - Map `AIRequest.Prompt` to Anthropic `messages` array with `role: "user"` content block
   - Map `AIRequest.SystemMessage` to top-level `system` parameter (Anthropic-specific: system is NOT in messages array)
   - Set `model` to `claude-3-5-sonnet-20241022` from `ClaudeProviderOptions`
   - Set `max_tokens` from `AIRequest.MaxOutputTokens` (Anthropic requires explicit `max_tokens`)
   - Set `temperature` from `AIRequest.Temperature`
   - Include `anthropic-version: 2023-06-01` header per API contract
   - Include `x-api-key` header with API key from configuration (NOT Bearer token — Anthropic uses custom header)

2. **Implement Claude response mapper** (`ClaudeResponseMapper`):
   - Parse Anthropic response `content` array (array of content blocks, each with `type` and `text`)
   - Concatenate text content blocks into single `AIResponse.Content` string
   - Map `usage.input_tokens` → `AIResponse.InputTokensUsed`
   - Map `usage.output_tokens` → `AIResponse.OutputTokensUsed`
   - Map `stop_reason` (`end_turn`, `max_tokens`, `stop_sequence`) to determine completion status
   - Set `AIResponse.ProviderName = "Anthropic"` and `AIResponse.ModelVersion = "claude-3-5-sonnet-20241022"`
   - Handle `stop_reason: "max_tokens"` as truncation warning in response metadata
   - Preserve `AIResponse.RequestId` and `AIResponse.CorrelationId` from original request

3. **Complete ClaudeProviderAdapter** with full API integration:
   - Wire `ClaudeRequestBuilder` and `ClaudeResponseMapper` into `SendCompletionAsync`
   - Configure `HttpClient` with `BaseAddress`, timeout from `ClaudeProviderOptions.TimeoutSeconds`
   - Implement `IsHealthyAsync` via lightweight Anthropic API status check
   - Handle Anthropic-specific HTTP errors: 429 (rate limited, read `retry-after` header), 529 (overloaded), 500 (internal), 401 (invalid API key)
   - Enforce token budget per request type before sending: reject over-budget requests with descriptive error

4. **Enforce identical token budgets on fallback**:
   - Reuse `TokenBudget` configuration from `AIGatewayOptions` (same budgets as primary)
   - DocumentParsing: 4K input / 1K output (AIR-O01)
   - ConversationalIntake: 500 input / 200 output (AIR-O02)
   - MedicalCoding: 2K input / 500 output (AIR-O03)
   - Validate token budget BEFORE sending request to Anthropic API (fail-fast)
   - Log budget enforcement decisions with request type and budget limits

5. **Add structured logging for fallback invocations**:
   - Log fallback activation event with correlation ID, request type, original provider failure reason
   - Log Claude request/response with token usage (redact PII per AIR-S01)
   - Log cost-per-request using Claude pricing ($3.00/1M input, $15.00/1M output)
   - Log response latency for performance tracking (AIR-Q03 through AIR-Q05)

6. **Update DI registration** in `AIGatewayServiceCollectionExtensions`:
   - Register `ClaudeRequestBuilder` as singleton
   - Register `ClaudeResponseMapper` as singleton
   - Bind `ClaudeProviderOptions` from `appsettings.json` configuration section

## Current Project State

```text
[Placeholder — update after US_067 and US_068 tasks are complete]
Server/
├── Program.cs
├── appsettings.json
├── Features/
│   └── AIGateway/
│       ├── Contracts/
│       │   ├── IAIProviderAdapter.cs          ← US_067/task_001
│       │   ├── AIRequest.cs                   ← US_067/task_001
│       │   ├── AIResponse.cs                  ← US_067/task_001
│       │   ├── AIRequestType.cs               ← US_067/task_001
│       │   └── TokenBudget.cs                 ← US_067/task_001
│       ├── Middleware/                         ← US_067/task_001
│       ├── Services/
│       │   ├── IAIGatewayService.cs           ← US_067/task_001
│       │   ├── AIGatewayService.cs            ← US_067/task_001
│       │   └── ProviderHealthTracker.cs       ← US_067/task_002
│       ├── Configuration/
│       │   ├── AIGatewayOptions.cs            ← US_067/task_001
│       │   ├── OpenAIProviderOptions.cs       ← US_067/task_002
│       │   ├── ClaudeProviderOptions.cs       ← US_067/task_002
│       │   └── ResilienceOptions.cs           ← US_067/task_002
│       ├── Extensions/
│       │   └── AIGatewayServiceCollectionExtensions.cs ← US_067/task_001
│       ├── Providers/
│       │   ├── OpenAIProviderAdapter.cs       ← US_067/task_002
│       │   └── ClaudeProviderAdapter.cs       ← US_067/task_002 (skeleton)
│       ├── Resilience/
│       │   ├── AIResiliencePipelineBuilder.cs ← US_067/task_002
│       │   └── AIProviderFallbackHandler.cs   ← US_067/task_002
│       └── Queue/                             ← US_067/task_003
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Features/AIGateway/Providers/ClaudeProviderAdapter.cs | Complete Anthropic Messages API integration with full request/response mapping, error handling, token budget enforcement |
| CREATE | Server/Features/AIGateway/Providers/ClaudeResponseMapper.cs | Map Anthropic content block array responses to unified AIResponse schema |
| CREATE | Server/Features/AIGateway/Providers/ClaudeRequestBuilder.cs | Map AIRequest to Anthropic Messages API payload (system message, content blocks, headers) |
| MODIFY | Server/Features/AIGateway/Configuration/ClaudeProviderOptions.cs | Add FallbackPriority and MaxConcurrentFallbackRequests settings |
| MODIFY | Server/Features/AIGateway/Services/ProviderHealthTracker.cs | Add Claude cost metrics ($3.00/1M input, $15.00/1M output) |
| MODIFY | Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs | Register ClaudeRequestBuilder and ClaudeResponseMapper services |

## External References

- [Anthropic Messages API — Official Docs](https://docs.anthropic.com/en/api/messages)
- [Anthropic API Authentication — x-api-key Header](https://docs.anthropic.com/en/api/getting-started#authentication)
- [Anthropic API Versioning — anthropic-version Header](https://docs.anthropic.com/en/api/versioning)
- [Anthropic Error Handling — Status Codes](https://docs.anthropic.com/en/api/errors)
- [Anthropic Rate Limits — 429 and retry-after](https://docs.anthropic.com/en/api/rate-limits)
- [Polly V8 Fallback Strategy — GitHub Docs](https://github.com/App-vNext/Polly/blob/main/docs/strategies/fallback.md)

### Anthropic Messages API Reference — Request/Response Shapes

```csharp
// Anthropic Messages API Request Shape
// POST https://api.anthropic.com/v1/messages
// Headers: x-api-key: {key}, anthropic-version: 2023-06-01, content-type: application/json
{
    "model": "claude-3-5-sonnet-20241022",
    "max_tokens": 1024,
    "system": "You are a medical document parser.",  // NOT in messages array
    "messages": [
        { "role": "user", "content": "Parse the following clinical note..." }
    ],
    "temperature": 0.2
}

// Anthropic Messages API Response Shape
{
    "id": "msg_...",
    "type": "message",
    "role": "assistant",
    "content": [
        { "type": "text", "text": "Extracted medications: ..." }
    ],
    "model": "claude-3-5-sonnet-20241022",
    "stop_reason": "end_turn",        // end_turn | max_tokens | stop_sequence
    "usage": {
        "input_tokens": 342,
        "output_tokens": 156
    }
}
```

## Build Commands

- `dotnet build` — Compile with completed Claude provider adapter
- `dotnet run` — Start backend with Claude fallback provider ready

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)

## Implementation Checklist

- [ ] Implement `ClaudeRequestBuilder` with Anthropic-specific request mapping (system as top-level param, content blocks, `anthropic-version` header, `x-api-key` auth)
- [ ] Implement `ClaudeResponseMapper` with content block concatenation, token usage mapping, stop_reason handling, and unified `AIResponse` output
- [ ] Complete `ClaudeProviderAdapter.SendCompletionAsync` wiring request builder, response mapper, HttpClient, and error handling (429/529/500/401)
- [ ] Implement `ClaudeProviderAdapter.IsHealthyAsync` with Anthropic API availability check
- [ ] Enforce identical token budgets per request type (DocumentParsing 4K/1K, Intake 500/200, Coding 2K/500) with fail-fast validation before API call
- [ ] Add structured Serilog logging for fallback activation events, request/response audit (PII-redacted), cost tracking, and latency metrics
- [ ] Register `ClaudeRequestBuilder` and `ClaudeResponseMapper` in DI container and bind updated `ClaudeProviderOptions`
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-O01, AIR-O02, AIR-O03, AIR-O05 requirements are met
