# Task - TASK_002

## Requirement Reference

- User Story: US_068
- Story Location: .propel/context/tasks/EP-012/us_068/us_068.md
- Acceptance Criteria:
    - AC-2: **Given** a document parsing request is sent, **When** token budget is enforced, **Then** the request is limited to 4K input tokens and 1K output tokens per the AIR-O01 specification.
    - AC-3: **Given** an intake conversational request is sent, **When** token budget is enforced, **Then** the request is limited to 500 input tokens and 200 output tokens per the AIR-O02 specification.
    - AC-4: **Given** a medical coding request is sent, **When** token budget is enforced, **Then** the request is limited to 2K input tokens and 500 output tokens per the AIR-O03 specification.
- Edge Case:
    - What happens when a request exceeds the token budget? System truncates the input to fit within budget, logs the truncation, and includes a "truncated" flag in the response.
    - How does the system handle OpenAI API rate limits? System implements exponential backoff (max 3 retries) and queues excess requests rather than failing immediately.

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
| AI/ML | OpenAI GPT-4o-mini | 2024-07-18 |
| Library | SharpToken (tiktoken .NET) | 2.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |
| Caching | Upstash Redis | 7.x |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O01, AIR-O02, AIR-O03, AIR-O07 |
| **AI Pattern** | Hybrid (RAG + Tool Calling + Classification) |
| **Prompt Template Path** | N/A (token budget infrastructure layer) |
| **Guardrails Config** | Server/Features/AIGateway/Configuration/TokenBudgetConfiguration.cs |
| **Model Provider** | OpenAI GPT-4o-mini (Primary) |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement per-request token budget enforcement for the AI Gateway, ensuring each request type (document parsing, conversational intake, medical coding) adheres to its designated token limits before reaching the OpenAI provider. This includes a token estimation service using SharpToken (tiktoken-compatible .NET library for GPT-4o-mini's `cl100k_base` encoding), an input truncation strategy that trims oversized inputs to fit within budget while preserving sentence boundaries, a truncation metadata flag in the response, and rate limit queue handling for excess requests. The enforcement service is wired into the `AIGatewayService` pipeline between request validation and provider invocation, ensuring token budgets are enforced at the application layer regardless of which provider handles the request.

## Dependent Tasks

- US_068 / task_001_be_openai_provider_adapter — Requires OpenAI provider adapter registered and functional for end-to-end budget enforcement testing.
- US_067 / task_001_be_ai_gateway_scaffold — Requires `AIGatewayService` pipeline, `AIRequest`/`AIResponse` DTOs, `AIRequestType` enum, `TokenBudget` model, and request validation middleware.

## Impacted Components

- **NEW** `Server/Features/AIGateway/Services/ITokenEstimationService.cs` — Token counting interface
- **NEW** `Server/Features/AIGateway/Services/TokenEstimationService.cs` — SharpToken-based token counting for cl100k_base
- **NEW** `Server/Features/AIGateway/Services/ITokenBudgetEnforcementService.cs` — Budget enforcement interface
- **NEW** `Server/Features/AIGateway/Services/TokenBudgetEnforcementService.cs` — Budget resolution, truncation, metadata tracking
- **NEW** `Server/Features/AIGateway/Configuration/TokenBudgetConfiguration.cs` — Per-request-type budget configuration POCO
- **NEW** `Server/Features/AIGateway/Models/TruncationMetadata.cs` — DTO for truncation event details
- **MODIFY** `Server/Features/AIGateway/Contracts/AIResponse.cs` — Add Truncated flag and TruncationMetadata property
- **MODIFY** `Server/Features/AIGateway/Services/AIGatewayService.cs` — Inject and invoke token budget enforcement in request pipeline
- **MODIFY** `Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs` — Register token estimation and budget enforcement services
- **MODIFY** `Server/appsettings.json` — Add token budget configuration per request type

## Implementation Plan

1. **Install NuGet package**: Add `SharpToken` (2.x) for tiktoken-compatible token encoding. SharpToken provides BPE encoding for `cl100k_base` (GPT-4o-mini's tokenizer), enabling accurate server-side token counting without API calls.

2. **Define ITokenEstimationService interface**:
   - `int EstimateTokenCount(string text)` — Count tokens for a given text string.
   - `string TruncateToTokenLimit(string text, int maxTokens)` — Truncate text to fit within a token limit while preserving sentence boundaries.

3. **Implement TokenEstimationService**:
   - Constructor: Initialize SharpToken `GptEncoding` for model `"gpt-4o-mini"` (resolves to `cl100k_base` encoding).
   - `EstimateTokenCount`: Encode text to token IDs using `encoding.Encode(text)` and return count.
   - `TruncateToTokenLimit`: Encode full text → if token count <= maxTokens, return original. Otherwise: decode tokens up to maxTokens limit → find last sentence boundary (period, question mark, exclamation mark) → truncate at boundary. If no boundary found within last 10% of tokens, truncate at exact token limit.

4. **Define TokenBudgetConfiguration POCO**:

```csharp
public class TokenBudgetConfiguration
{
    public Dictionary<string, TokenBudgetLimit> RequestTypeBudgets { get; set; } = new();
}

public class TokenBudgetLimit
{
    public int MaxInputTokens { get; set; }
    public int MaxOutputTokens { get; set; }
}
```

   Bind from `appsettings.json` section `AIGateway:TokenBudgets`:

```json
{
  "AIGateway": {
    "TokenBudgets": {
      "RequestTypeBudgets": {
        "DocumentParsing": { "MaxInputTokens": 4000, "MaxOutputTokens": 1000 },
        "ConversationalIntake": { "MaxInputTokens": 500, "MaxOutputTokens": 200 },
        "MedicalCoding": { "MaxInputTokens": 2000, "MaxOutputTokens": 500 }
      }
    }
  }
}
```

5. **Define TruncationMetadata DTO**:
   - `OriginalTokenCount` (int): Token count before truncation.
   - `TruncatedTokenCount` (int): Token count after truncation.
   - `MaxAllowedTokens` (int): Budget limit that triggered truncation.
   - `RequestType` (AIRequestType): Which request type's budget applied.

6. **Extend AIResponse**: Add `bool Truncated` property (default: false) and `TruncationMetadata? TruncationInfo` property (default: null). These fields are populated by the enforcement service when input truncation occurs.

7. **Implement TokenBudgetEnforcementService**:
   - Constructor injects `ITokenEstimationService`, `IOptions<TokenBudgetConfiguration>`, and `ILogger<TokenBudgetEnforcementService>`.
   - `EnforceAsync(AIRequest request) -> (AIRequest enforcedRequest, TruncationMetadata? metadata)`:
     1. Resolve budget from configuration by `request.RequestType` key.
     2. Count input tokens via `_tokenEstimation.EstimateTokenCount(request.Prompt)`.
     3. If input tokens > budget.MaxInputTokens: truncate prompt via `_tokenEstimation.TruncateToTokenLimit(request.Prompt, budget.MaxInputTokens)` → create `TruncationMetadata` → log truncation event.
     4. Set `request.MaxOutputTokens = budget.MaxOutputTokens` (ensuring provider respects output limit).
     5. Return modified request and optional truncation metadata.

8. **Wire into AIGatewayService pipeline**: In `AIGatewayService.ProcessRequestAsync`, insert token budget enforcement between request validation and provider invocation:
   - Step 1: Validate request (existing middleware)
   - **Step 2 (NEW)**: `_budgetEnforcement.EnforceAsync(request)` → get enforced request + truncation metadata
   - Step 3: Invoke provider with enforced request
   - Step 4: If truncation occurred, set `response.Truncated = true` and `response.TruncationInfo = metadata`
   - Step 5: Normalize and return response

**Focus on how to implement**: SharpToken's `GptEncoding.GetEncodingForModel("gpt-4o-mini")` returns the cl100k_base encoding. Use `encoding.Encode(text).Count` for counting and `encoding.Decode(encoding.Encode(text).Take(maxTokens).ToList())` for truncation. Sentence boundary detection uses regex `(?<=[.!?])\s+` to find split points within the last 10% of the truncated text.

## Current Project State

```
[Placeholder — update after task_001_be_openai_provider_adapter is complete]
Server/
├── Program.cs
├── appsettings.json
├── Features/
│   └── AIGateway/
│       ├── Contracts/
│       │   ├── IAIProviderAdapter.cs
│       │   ├── AIRequest.cs
│       │   ├── AIResponse.cs          ← MODIFY (add Truncated, TruncationInfo)
│       │   ├── AIRequestType.cs
│       │   └── TokenBudget.cs
│       ├── Middleware/
│       ├── Services/
│       │   ├── IAIGatewayService.cs
│       │   ├── AIGatewayService.cs    ← MODIFY (inject budget enforcement)
│       │   ├── ITokenEstimationService.cs       ← NEW
│       │   ├── TokenEstimationService.cs        ← NEW
│       │   ├── ITokenBudgetEnforcementService.cs ← NEW
│       │   └── TokenBudgetEnforcementService.cs  ← NEW
│       ├── Configuration/
│       │   ├── AIGatewayOptions.cs
│       │   └── TokenBudgetConfiguration.cs      ← NEW
│       ├── Models/
│       │   └── TruncationMetadata.cs            ← NEW
│       ├── Extensions/
│       │   └── AIGatewayServiceCollectionExtensions.cs ← MODIFY
│       └── Providers/
│           └── OpenAI/
│               ├── OpenAIProviderAdapter.cs
│               ├── OpenAIProviderOptions.cs
│               ├── OpenAIRequestMapper.cs
│               └── OpenAIResponseMapper.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Features/AIGateway/Services/ITokenEstimationService.cs | Interface with EstimateTokenCount and TruncateToTokenLimit methods |
| CREATE | Server/Features/AIGateway/Services/TokenEstimationService.cs | SharpToken-based implementation using cl100k_base encoding for GPT-4o-mini |
| CREATE | Server/Features/AIGateway/Services/ITokenBudgetEnforcementService.cs | Interface with EnforceAsync method returning enforced request and truncation metadata |
| CREATE | Server/Features/AIGateway/Services/TokenBudgetEnforcementService.cs | Per-request-type budget resolution, input truncation, metadata tracking, structured logging |
| CREATE | Server/Features/AIGateway/Configuration/TokenBudgetConfiguration.cs | POCO binding token budget limits per AIRequestType from appsettings.json |
| CREATE | Server/Features/AIGateway/Models/TruncationMetadata.cs | DTO: OriginalTokenCount, TruncatedTokenCount, MaxAllowedTokens, RequestType |
| MODIFY | Server/Features/AIGateway/Contracts/AIResponse.cs | Add bool Truncated and TruncationMetadata? TruncationInfo properties |
| MODIFY | Server/Features/AIGateway/Services/AIGatewayService.cs | Inject ITokenBudgetEnforcementService, invoke in pipeline before provider call |
| MODIFY | Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs | Register ITokenEstimationService, ITokenBudgetEnforcementService, bind TokenBudgetConfiguration |
| MODIFY | Server/appsettings.json | Add AIGateway:TokenBudgets section with per-request-type MaxInputTokens and MaxOutputTokens |

## External References

- [SharpToken — GitHub](https://github.com/dmitry-brazhenko/SharpToken) — Tiktoken-compatible BPE tokenizer for .NET, supports cl100k_base encoding used by GPT-4o-mini
- [OpenAI Tokenizer — Documentation](https://platform.openai.com/tokenizer) — Token counting reference and cl100k_base encoding details
- [OpenAI Token Usage — API Reference](https://platform.openai.com/docs/api-reference/chat/object) — Usage object schema: prompt_tokens, completion_tokens, total_tokens
- [OpenAI Rate Limits — Documentation](https://platform.openai.com/docs/guides/rate-limits) — 429 response handling, Retry-After header, rate limit tiers
- [ASP.NET Core Options Pattern — Microsoft Docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0) — IOptions binding for TokenBudgetConfiguration

## Build Commands

- `dotnet add package SharpToken --version 2.*` — Install SharpToken tokenizer
- `dotnet build` — Compile project with token budget enforcement
- `dotnet run` — Start backend with budget enforcement in pipeline

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] TokenEstimationService correctly counts tokens using cl100k_base encoding (verify against OpenAI tokenizer)
- [ ] Document parsing requests enforce 4000 input / 1000 output token limits (AIR-O01)
- [ ] Conversational intake requests enforce 500 input / 200 output token limits (AIR-O02)
- [ ] Medical coding requests enforce 2000 input / 500 output token limits (AIR-O03)
- [ ] Input exceeding budget is truncated at sentence boundary with Truncated flag set to true
- [ ] TruncationMetadata includes OriginalTokenCount, TruncatedTokenCount, MaxAllowedTokens
- [ ] Structured logging captures budget enforcement events with CorrelationId, RequestType, WasTruncated

## Implementation Checklist

- [ ] Implement `ITokenEstimationService` and `TokenEstimationService` using SharpToken `GptEncoding.GetEncodingForModel("gpt-4o-mini")` for `cl100k_base` token counting
- [ ] Create `TokenBudgetConfiguration` POCO and bind from `appsettings.json` section `AIGateway:TokenBudgets` with per-request-type limits (DocumentParsing: 4K/1K, ConversationalIntake: 500/200, MedicalCoding: 2K/500)
- [ ] Implement input truncation logic in `TruncateToTokenLimit` that trims text to fit within `MaxInputTokens` while preserving sentence boundaries using regex split
- [ ] Add `Truncated` (bool) flag and `TruncationInfo` (`TruncationMetadata?`) properties to `AIResponse` DTO, populated when input truncation occurs
- [ ] Implement `MaxOutputTokens` enforcement by passing `TokenBudget.MaxOutputTokens` to OpenAI API `MaxOutputTokenCount` parameter on every request
- [ ] Wire `ITokenBudgetEnforcementService` into `AIGatewayService.ProcessRequestAsync` pipeline: count tokens → truncate if over budget → set max_tokens → invoke provider → attach truncation metadata to response
- [ ] Log all budget enforcement events via Serilog structured logging with fields: CorrelationId, RequestType, OriginalTokenCount, EnforcedBudget, WasTruncated, TruncatedTokenCount
- [ ] Handle OpenAI rate limit (429) responses: parse `Retry-After` header value and queue excess requests via Redis Queue rather than failing immediately (AIR-O07)
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-O01 (4K/1K), AIR-O02 (500/200), AIR-O03 (2K/500) token budgets are enforced per request type
