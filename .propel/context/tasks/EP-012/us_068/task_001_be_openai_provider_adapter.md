# Task - TASK_001

## Requirement Reference

- User Story: US_068
- Story Location: .propel/context/tasks/EP-012/us_068/us_068.md
- Acceptance Criteria:
    - AC-1: **Given** the AI Gateway is configured, **When** an AI request is routed, **Then** OpenAI GPT-4o-mini is used as the primary provider by default.
    - AC-2: **Given** a document parsing request is sent, **When** token budget is enforced, **Then** the request is limited to 4K input tokens and 1K output tokens per the AIR-O01 specification.
    - AC-3: **Given** an intake conversational request is sent, **When** token budget is enforced, **Then** the request is limited to 500 input tokens and 200 output tokens per the AIR-O02 specification.
    - AC-4: **Given** a medical coding request is sent, **When** token budget is enforced, **Then** the request is limited to 2K input tokens and 500 output tokens per the AIR-O03 specification.
- Edge Case:
    - How does the system handle OpenAI API rate limits? System implements exponential backoff (max 3 retries) and queues excess requests rather than failing immediately.
    - What happens when both primary and fallback providers are unavailable? System returns a structured error response with "AI service unavailable" and the caller falls back to manual workflow (handled by US_067 AI Gateway foundation).

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
| API Framework | ASP.NET Core MVC | 8.x |
| ORM | Entity Framework Core | 8.x |
| AI Gateway | Custom .NET Service with Polly | N/A / 8.x |
| AI/ML | OpenAI GPT-4o-mini | 2024-07-18 |
| Library | Polly (Resilience) | 8.x |
| Library | OpenAI .NET SDK | 2.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O01, AIR-O02, AIR-O03, AIR-O04, AIR-O08 |
| **AI Pattern** | Hybrid (RAG + Tool Calling + Classification) |
| **Prompt Template Path** | N/A (provider adapter layer) |
| **Guardrails Config** | N/A (guardrails in dependent AI tasks) |
| **Model Provider** | OpenAI GPT-4o-mini (Primary) |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the OpenAI GPT-4o-mini provider adapter that fulfils the `IAIProviderAdapter` interface defined in US_067. This adapter serves as the primary LLM provider for all AI features (conversational intake, document parsing, medical coding). It configures the OpenAI .NET SDK `ChatClient` as a typed HTTP client, maps the gateway's unified `AIRequest`/`AIResponse` contracts to OpenAI Chat Completion API payloads, implements health checks via the `/models` endpoint, and integrates Polly resilience policies for exponential backoff retry on transient failures (429 rate limits, 5xx server errors). The adapter is registered in DI as the primary `IAIProviderAdapter`, ensuring all AI Gateway requests route to GPT-4o-mini by default per TR-006.

## Dependent Tasks

- US_067 / task_001_be_ai_gateway_scaffold — Requires `IAIProviderAdapter` interface, `AIRequest`/`AIResponse` DTOs, `AIRequestType` enum, `TokenBudget` model, and `AIGatewayService` orchestration.
- US_001 — Foundational — Requires .NET 8 project scaffold with ASP.NET Core Web API.

## Impacted Components

- **NEW** `Server/Features/AIGateway/Providers/OpenAI/OpenAIProviderAdapter.cs` — Primary provider adapter implementing IAIProviderAdapter
- **NEW** `Server/Features/AIGateway/Providers/OpenAI/OpenAIProviderOptions.cs` — Configuration POCO for OpenAI-specific settings
- **NEW** `Server/Features/AIGateway/Providers/OpenAI/OpenAIRequestMapper.cs` — Maps AIRequest to OpenAI ChatCompletionRequest
- **NEW** `Server/Features/AIGateway/Providers/OpenAI/OpenAIResponseMapper.cs` — Maps OpenAI ChatCompletionResponse to AIResponse
- **MODIFY** `Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs` — Register OpenAI provider adapter and Polly policies
- **MODIFY** `Server/Program.cs` — Wire OpenAI provider DI registrations
- **MODIFY** `Server/appsettings.json` — Add OpenAI provider configuration section

## Implementation Plan

1. **Install NuGet packages**: Add `OpenAI` (2.x) SDK and ensure `Microsoft.Extensions.Http.Resilience` (for Polly integration with HttpClientFactory) is available.

2. **Define OpenAI configuration POCO** (`OpenAIProviderOptions`): Properties for ApiKey, Endpoint (default: `https://api.openai.com/v1`), Model (default: `gpt-4o-mini`), ModelVersion (`2024-07-18`), TimeoutSeconds (default: 30), MaxRetryAttempts (default: 3). Bind from `appsettings.json` section `AIGateway:Providers:OpenAI`.

3. **Implement request mapper** (`OpenAIRequestMapper`): Convert `AIRequest` to OpenAI SDK `ChatCompletionOptions` — map `SystemMessage` to system role message, `Prompt` to user role message, set `MaxOutputTokenCount` from `AIRequest.MaxOutputTokens`, set `Temperature` from `AIRequest.Temperature`. Static method `MapToOptions(AIRequest request) -> ChatCompletionOptions`.

4. **Implement response mapper** (`OpenAIResponseMapper`): Convert `ChatCompletion` to `AIResponse` — map `Content[0].Text` to `Content`, map `Usage.InputTokenCount` / `Usage.OutputTokenCount` to token fields, capture `Model` as `ModelVersion`, compute `LatencyMs` from stopwatch, set `ProviderName` to `"OpenAI"`, set `Success` based on completion finish reason. Static method `MapToResponse(ChatCompletion completion, AIRequest request, long latencyMs) -> AIResponse`.

5. **Implement OpenAIProviderAdapter**:
   - Constructor injects `ChatClient` (from OpenAI SDK), `IOptions<OpenAIProviderOptions>`, and `ILogger<OpenAIProviderAdapter>`.
   - `SendCompletionAsync`: Start stopwatch → call `OpenAIRequestMapper.MapToOptions` → invoke `_chatClient.CompleteChatAsync(messages, options, cancellationToken)` → call `OpenAIResponseMapper.MapToResponse` → log structured result → return `AIResponse`.
   - `IsHealthyAsync`: Call `_chatClient` list models or a lightweight ping to verify API connectivity. Return `true` if 200, `false` otherwise.
   - Properties: `ProviderName => "OpenAI"`, `ModelVersion` from options.

6. **Configure Polly resilience pipeline** for the OpenAI HttpClient:
   - Retry: `MaxRetryAttempts = 3`, `BackoffType = Exponential`, `UseJitter = true`, `Delay = 1s`, handle `HttpRequestException` and 429/500/502/503 status codes.
   - On retry callback: Log retry attempt number, delay, and exception via Serilog.
   - Register via `AddResilienceHandler` on the named HttpClient.

7. **Register in DI** (`AIGatewayServiceCollectionExtensions`):
   - Bind `OpenAIProviderOptions` from configuration.
   - Register `ChatClient` as singleton using `OpenAIProviderOptions.ApiKey` and `OpenAIProviderOptions.Model`.
   - Register `OpenAIProviderAdapter` as the primary (keyed) `IAIProviderAdapter`.
   - Configure named HttpClient `"OpenAI"` with base address, default headers, and Polly resilience handler.

8. **Add configuration** to `appsettings.json`:

```json
{
  "AIGateway": {
    "Providers": {
      "OpenAI": {
        "ApiKey": "[REPLACE_WITH_OPENAI_API_KEY]",
        "Endpoint": "https://api.openai.com/v1",
        "Model": "gpt-4o-mini",
        "ModelVersion": "2024-07-18",
        "TimeoutSeconds": 30,
        "MaxRetryAttempts": 3
      }
    }
  }
}
```

**Focus on how to implement**: Use the OpenAI .NET SDK `ChatClient` for API communication rather than raw HttpClient calls. The SDK handles JSON serialization, streaming, and error parsing internally. Polly resilience wraps the underlying HttpClient transport used by the SDK. Request/response mappers are static utility classes to maintain testability and avoid coupling.

## Current Project State

```
[Placeholder — update after US_067 task_001 scaffold is complete]
Server/
├── Program.cs
├── appsettings.json
├── Features/
│   └── AIGateway/
│       ├── Contracts/
│       │   ├── IAIProviderAdapter.cs
│       │   ├── AIRequest.cs
│       │   ├── AIResponse.cs
│       │   ├── AIRequestType.cs
│       │   └── TokenBudget.cs
│       ├── Middleware/
│       │   ├── AIRequestValidationMiddleware.cs
│       │   ├── AIAuthenticationMiddleware.cs
│       │   └── AIResponseNormalizationMiddleware.cs
│       ├── Services/
│       │   ├── IAIGatewayService.cs
│       │   └── AIGatewayService.cs
│       ├── Configuration/
│       │   └── AIGatewayOptions.cs
│       ├── Extensions/
│       │   └── AIGatewayServiceCollectionExtensions.cs
│       └── Providers/
│           └── OpenAI/              ← NEW (this task)
│               ├── OpenAIProviderAdapter.cs
│               ├── OpenAIProviderOptions.cs
│               ├── OpenAIRequestMapper.cs
│               └── OpenAIResponseMapper.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Features/AIGateway/Providers/OpenAI/OpenAIProviderAdapter.cs | Primary provider adapter implementing IAIProviderAdapter with ChatClient, health check, structured logging |
| CREATE | Server/Features/AIGateway/Providers/OpenAI/OpenAIProviderOptions.cs | Configuration POCO: ApiKey, Endpoint, Model, ModelVersion, TimeoutSeconds, MaxRetryAttempts |
| CREATE | Server/Features/AIGateway/Providers/OpenAI/OpenAIRequestMapper.cs | Static mapper: AIRequest → ChatCompletionOptions (system/user messages, max_tokens, temperature) |
| CREATE | Server/Features/AIGateway/Providers/OpenAI/OpenAIResponseMapper.cs | Static mapper: ChatCompletion → AIResponse (content, token usage, latency, provider metadata) |
| MODIFY | Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs | Add OpenAI provider DI registration, ChatClient singleton, Polly resilience pipeline |
| MODIFY | Server/Program.cs | Wire OpenAI provider configuration binding and DI registrations |
| MODIFY | Server/appsettings.json | Add AIGateway:Providers:OpenAI configuration section |

## External References

- [OpenAI .NET SDK — GitHub](https://github.com/openai/openai-dotnet) — ChatClient usage, ASP.NET Core DI registration pattern, ChatCompletion API
- [OpenAI Chat Completion API — Official Docs](https://platform.openai.com/docs/api-reference/chat) — Request/response schema, max_tokens, model parameter
- [Polly v8 Resilience Pipelines — GitHub](https://github.com/App-vNext/Polly) — RetryStrategyOptions, CircuitBreakerStrategyOptions, exponential backoff with jitter
- [Microsoft.Extensions.Http.Resilience — Docs](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience) — AddResilienceHandler for named HttpClients
- [ASP.NET Core Keyed Services — .NET 8](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-8.0#keyed-services) — Keyed DI registration for primary/fallback providers
- [OpenAI GPT-4o-mini Model Card](https://platform.openai.com/docs/models/gpt-4o-mini) — Model version 2024-07-18, 128K context window, token pricing

## Build Commands

- `dotnet add package OpenAI --version 2.*` — Install OpenAI .NET SDK
- `dotnet add package Microsoft.Extensions.Http.Resilience` — Polly HTTP integration
- `dotnet build` — Compile project with new provider adapter
- `dotnet run` — Start backend with OpenAI provider registered

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] DI container resolves OpenAIProviderAdapter as primary IAIProviderAdapter without errors
- [ ] ChatClient singleton correctly configured with model "gpt-4o-mini" and API key from configuration
- [ ] Polly retry policy triggers on 429/5xx responses with exponential backoff and jitter
- [ ] IsHealthyAsync returns true when OpenAI API is reachable, false otherwise
- [ ] Structured logging includes ProviderName, ModelVersion, LatencyMs, InputTokensUsed, OutputTokensUsed

## Implementation Checklist

- [ ] Create `OpenAIProviderAdapter` class implementing `IAIProviderAdapter` with constructor injecting `ChatClient`, `IOptions<OpenAIProviderOptions>`, and `ILogger<OpenAIProviderAdapter>`
- [ ] Configure typed `HttpClient` for OpenAI SDK with base address (`https://api.openai.com/v1`), API key authorization header, and configurable timeout from `OpenAIProviderOptions`
- [ ] Implement `OpenAIRequestMapper.MapToOptions` mapping `AIRequest` to `ChatCompletionOptions` (model: gpt-4o-mini, system/user messages, `MaxOutputTokenCount` from `TokenBudget`, temperature)
- [ ] Implement `OpenAIResponseMapper.MapToResponse` mapping `ChatCompletion` to `AIResponse` (content from `Content[0].Text`, `Usage.InputTokenCount`, `Usage.OutputTokenCount`, latency from stopwatch, `ProviderName: "OpenAI"`)
- [ ] Implement `IsHealthyAsync` health check verifying OpenAI API connectivity via models endpoint and returning boolean result
- [ ] Configure Polly resilience pipeline with exponential backoff retry (max 3 attempts, 1s base delay, jitter) handling `HttpRequestException`, 429 rate limit, and 5xx server errors
- [ ] Register `OpenAIProviderAdapter` as primary keyed `IAIProviderAdapter` in DI with `ChatClient` singleton and bind `OpenAIProviderOptions` from `appsettings.json` section `AIGateway:Providers:OpenAI`
- [ ] Add OpenAI provider configuration section to `appsettings.json` with ApiKey placeholder, Endpoint, Model (`gpt-4o-mini`), ModelVersion (`2024-07-18`), TimeoutSeconds (30), and MaxRetryAttempts (3)
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-O01, AIR-O02, AIR-O03 token budget pass-through and AIR-O08 retry requirements are met
