# Task - TASK_004_AI_GATEWAY_COST_QUEUE_HOOKS

## Requirement Reference

- User Story: us_071
- Story Location: .propel/context/tasks/EP-012/us_071/us_071.md
- Acceptance Criteria:
    - AC-1: **Given** AI requests are processed, **When** the daily aggregation runs, **Then** the system calculates total token usage and estimated cost by provider and request type.
    - AC-3: **Given** document parsing requests arrive, **When** the queue concurrency limit is reached, **Then** new requests are queued in Redis FIFO and processed as capacity becomes available.
- Edge Case:
    - What happens when cost data is unavailable from a provider's API? System estimates cost using token count × configured rate card; estimates are flagged as "approximate."

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
| Backend | .NET (ASP.NET Core Web API) | 8.x |
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Caching/Queue | Upstash Redis | 7.x |
| AI Gateway | Custom .NET Service with Polly | 8.x |
| AI/ML | OpenAI GPT-4o-mini (Primary), Anthropic Claude 3.5 Sonnet (Fallback) | 2024-07-18 / claude-3-5-sonnet-20241022 |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O07, AIR-O08, AIR-O09 |
| **AI Pattern** | Tool Calling (document parsing pipeline) |
| **Prompt Template Path** | N/A (infrastructure-level — no prompt templates) |
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

Integrate cost tracking and request queuing hooks into the existing AI Gateway service (from US_067). This task adds middleware/interceptor logic within the AI Gateway pipeline so that every AI request automatically logs token usage and estimated cost to the `AiRequestLog` table, and document parsing requests are routed through the Redis queue instead of being dispatched directly. This is the glue layer connecting the AI Gateway (US_067) with the cost monitoring service (task_002) and the request queue (task_003).

## Dependent Tasks

- task_001_db_ai_cost_tracking_schema — Requires AI cost tracking database tables
- task_002_be_ai_cost_monitoring — Requires cost aggregation service for real-time threshold checks
- task_003_be_request_queuing — Requires Redis FIFO queue service for document parsing
- US_067 (EP-012) — Requires AI Gateway service with provider abstraction and Polly resilience

## Impacted Components

- **NEW** - `AiCostTrackingMiddleware` (Server project, AiGateway/Middleware)
- **NEW** - `IAiRequestCostLogger` interface (Server project, AiGateway/Services)
- **NEW** - `AiRequestCostLogger` implementation (Server project, AiGateway/Services)
- **MODIFY** - AI Gateway request pipeline — Insert cost tracking middleware after response received
- **MODIFY** - AI Gateway document parsing endpoint — Route through Redis queue instead of direct dispatch
- **MODIFY** - `Program.cs` — Register new middleware and services

## Implementation Plan

1. **Implement `IAiRequestCostLogger`** — Service that extracts token usage from AI provider responses (OpenAI returns `usage.prompt_tokens` and `usage.completion_tokens`; Anthropic returns `usage.input_tokens` and `usage.output_tokens`). Calculates estimated cost using provider-reported cost when available, or falls back to rate card from `AiCostBudgetConfig` and flags cost_source as `Approximate`. Creates `AiRequestLog` entry with provider, request_type, input_tokens, output_tokens, estimated_cost, cost_source, and correlation_id.

2. **Implement `AiCostTrackingMiddleware`** — Middleware component injected into the AI Gateway pipeline that executes after each AI provider response. Calls `IAiRequestCostLogger` to persist cost data. Also calls `IAiCostAggregationService.GetRunningDailyCostAsync()` to check if the running daily total has breached the budget threshold (near-real-time alerting). Middleware is non-blocking — cost logging failures must not prevent the AI response from being returned to the caller.

3. **Modify document parsing request routing** — Intercept document parsing requests at the AI Gateway entry point. Instead of dispatching directly to the AI provider, enqueue the request via `IDocumentParsingQueue.EnqueueAsync()`. Return an accepted/queued status to the caller with a correlation_id for tracking. The `DocumentParsingQueueProcessor` (from task_003) handles actual dispatch.

4. **Implement provider-specific token extraction** — Create provider adapter methods that normalize token usage from different provider response formats into a unified `TokenUsage` record (input_tokens, output_tokens, total_tokens). Handle cases where provider response omits cost data (e.g., network timeout with partial response) by calculating from token count × rate card.

5. **Implement non-blocking cost logging** — Use `Task.Run` with exception swallowing (log errors to Serilog) to ensure cost tracking middleware never blocks or fails the main AI response pipeline. If Redis is unavailable for real-time threshold check, skip the check and log a warning.

6. **Register middleware and services in DI** — Add `AiCostTrackingMiddleware` to the AI Gateway pipeline. Register `IAiRequestCostLogger` in the DI container.

### Pseudocode: Cost Tracking Middleware

```pseudocode
class AiCostTrackingMiddleware:
    function OnResponseReceived(request, response, provider):
        // Non-blocking fire-and-forget cost logging
        Task.Run(async () =>
            try:
                tokenUsage = ExtractTokenUsage(response, provider)
                costSource = CostSource.Actual

                if tokenUsage.Cost is null:
                    rateCard = await budgetConfig.GetRateCardAsync(provider)
                    tokenUsage.Cost = CalculateFromRateCard(tokenUsage, rateCard)
                    costSource = CostSource.Approximate

                await costLogger.LogAsync(new AiRequestLog {
                    Provider = provider,
                    RequestType = request.Type,
                    InputTokens = tokenUsage.InputTokens,
                    OutputTokens = tokenUsage.OutputTokens,
                    EstimatedCost = tokenUsage.Cost,
                    CostSource = costSource,
                    CorrelationId = request.CorrelationId
                })

                // Near-real-time threshold check
                await costAggregation.CheckRunningThresholdAsync(provider)

            catch (Exception ex):
                log.Warning(ex, "Cost tracking failed for {CorrelationId}", request.CorrelationId)
        )

    function ExtractTokenUsage(response, provider):
        switch provider:
            case OpenAI:
                return (response.Usage.PromptTokens, response.Usage.CompletionTokens)
            case Anthropic:
                return (response.Usage.InputTokens, response.Usage.OutputTokens)
```

### Pseudocode: Document Parsing Queue Routing

```pseudocode
function HandleDocumentParsingRequest(request):
    queueItem = new QueueItem {
        DocumentId = request.DocumentId,
        PatientId = request.PatientId,
        CorrelationId = Guid.NewGuid(),
        EnqueueTimestamp = DateTime.UtcNow,
        RetryCount = 0
    }

    await documentParsingQueue.EnqueueAsync(queueItem)

    return AcceptedResult(new {
        CorrelationId = queueItem.CorrelationId,
        Status = "Queued",
        Message = "Document parsing request queued for processing"
    })
```

## Current Project State

- [Placeholder — to be updated during execution based on dependent task completion]

```text
Server/
├── AiGateway/
│   ├── Middleware/
│   │   └── ... (existing gateway middleware)
│   ├── Providers/
│   │   ├── OpenAiProvider.cs        (from US_067)
│   │   └── AnthropicProvider.cs     (from US_067)
│   ├── Services/
│   │   └── ... (existing gateway services)
│   └── AiGatewayPipeline.cs        (from US_067)
├── Services/
│   ├── AiCost/                      (from task_002)
│   └── Queue/                       (from task_003)
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/AiGateway/Services/IAiRequestCostLogger.cs | Interface for logging per-request AI cost data |
| CREATE | Server/AiGateway/Services/AiRequestCostLogger.cs | Implementation: extract token usage, calculate cost, persist to AiRequestLog |
| CREATE | Server/AiGateway/Middleware/AiCostTrackingMiddleware.cs | Non-blocking middleware: log costs + near-real-time threshold check after each AI response |
| CREATE | Server/AiGateway/Models/TokenUsage.cs | Unified token usage record normalized across providers |
| MODIFY | Server/AiGateway/AiGatewayPipeline.cs | Insert AiCostTrackingMiddleware into pipeline; route document parsing through queue |
| MODIFY | Server/Program.cs | Register IAiRequestCostLogger and AiCostTrackingMiddleware in DI |

## External References

- [OpenAI Chat Completion Response — usage object](https://platform.openai.com/docs/api-reference/chat/object)
- [Anthropic Messages API — usage object](https://docs.anthropic.com/en/api/messages)
- [Polly .NET 8 Resilience Pipelines](https://www.thepollyproject.org/)
- [ASP.NET Core Middleware Pipeline](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-8.0)

## Build Commands

- `dotnet build Server/`
- `dotnet test Server.Tests/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Cost logging persists correct token counts and estimated cost for OpenAI provider
- [ ] Cost logging persists correct token counts and estimated cost for Anthropic provider
- [ ] Approximate cost flagged correctly when provider cost data unavailable
- [ ] Document parsing requests routed through queue (not direct dispatch)
- [ ] Cost tracking middleware does not block AI response pipeline
- [ ] Near-real-time threshold check triggers alert when budget exceeded

## Implementation Checklist

- [ ] Create `IAiRequestCostLogger` interface with `LogAsync(AiRequestLog)` method and `TokenUsage` model for normalized provider token data
- [ ] Implement `AiRequestCostLogger` — extract token usage from OpenAI (`usage.prompt_tokens`, `usage.completion_tokens`) and Anthropic (`usage.input_tokens`, `usage.output_tokens`), calculate cost or apply rate card fallback, persist `AiRequestLog` entry
- [ ] Implement `AiCostTrackingMiddleware` — non-blocking post-response middleware that logs cost data and checks running daily threshold; cost logging failures must not block AI responses (fire-and-forget with Serilog error logging)
- [ ] Modify AI Gateway document parsing endpoint to route requests through `IDocumentParsingQueue.EnqueueAsync()` instead of direct provider dispatch; return 202 Accepted with correlation_id
- [ ] Implement provider-specific token extraction adapters for OpenAI and Anthropic response formats with graceful handling of missing cost data
- [ ] Register `IAiRequestCostLogger`, `AiCostTrackingMiddleware` in DI and insert middleware into AI Gateway pipeline
