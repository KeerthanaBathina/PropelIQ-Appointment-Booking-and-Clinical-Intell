# Task - TASK_001

## Requirement Reference

- User Story: US_067
- Story Location: .propel/context/tasks/EP-012/us_067/us_067.md
- Acceptance Criteria:
    - AC-1: **Given** the AI Gateway service is scaffolded, **When** a developer inspects the architecture, **Then** it follows the vertical slice pattern with provider abstraction, request routing, and Polly resilience policies.
    - AC-3: **Given** the AI Gateway is configured, **When** a request is processed, **Then** it enforces the API Gateway pattern with request validation, authentication, and response normalization.
- Edge Case:
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
| API Framework | ASP.NET Core MVC | 8.x |
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| ORM | Entity Framework Core | 8.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |
| AI Gateway | Custom .NET Service with Polly | N/A / 8.x |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O01, AIR-O02, AIR-O03 |
| **AI Pattern** | Hybrid (RAG + Tool Calling + Classification) |
| **Prompt Template Path** | N/A (gateway infrastructure layer) |
| **Guardrails Config** | N/A (guardrails in dependent tasks) |
| **Model Provider** | OpenAI GPT-4o-mini (Primary) / Anthropic Claude 3.5 Sonnet (Fallback) |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Scaffold the AI Gateway .NET service following the vertical slice architecture pattern for AI components (Decision #1 from design.md). This service acts as the centralized abstraction layer between all AI feature consumers (conversational intake, document parsing, medical coding) and external LLM providers (OpenAI, Anthropic). The task establishes the project structure, unified request/response contracts, provider abstraction interfaces, API Gateway middleware pipeline (request validation, JWT authentication, response normalization), dependency injection configuration, and structured logging with correlation IDs.

This is the foundational task that other AI Gateway tasks (provider adapters, resilience policies, Redis queuing) depend on.

## Dependent Tasks

- US_001 - Foundational - Requires .NET project scaffold (ASP.NET Core Web API base)

## Impacted Components

- **NEW** `Features/AIGateway/` — Vertical slice root folder for AI Gateway feature
- **NEW** `Features/AIGateway/Contracts/IAIProviderAdapter.cs` — Provider abstraction interface
- **NEW** `Features/AIGateway/Contracts/AIRequest.cs` — Unified request DTO
- **NEW** `Features/AIGateway/Contracts/AIResponse.cs` — Unified response DTO
- **NEW** `Features/AIGateway/Contracts/AIRequestType.cs` — Request type enum
- **NEW** `Features/AIGateway/Contracts/TokenBudget.cs` — Token budget configuration model
- **NEW** `Features/AIGateway/Middleware/AIRequestValidationMiddleware.cs` — Request validation middleware
- **NEW** `Features/AIGateway/Middleware/AIAuthenticationMiddleware.cs` — JWT authentication middleware for AI endpoints
- **NEW** `Features/AIGateway/Middleware/AIResponseNormalizationMiddleware.cs` — Response normalization middleware
- **NEW** `Features/AIGateway/Services/AIGatewayService.cs` — Core gateway orchestration service
- **NEW** `Features/AIGateway/Services/IAIGatewayService.cs` — Gateway service interface
- **NEW** `Features/AIGateway/Configuration/AIGatewayOptions.cs` — Configuration POCO for AI Gateway settings
- **NEW** `Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs` — DI registration extensions
- **MODIFY** `Program.cs` — Register AI Gateway services and middleware

## Implementation Plan

1. **Create vertical slice folder structure** under `Features/AIGateway/` with subfolders: `Contracts/`, `Middleware/`, `Services/`, `Configuration/`, `Extensions/`, `Providers/` (placeholder for Task 002).

2. **Define IAIProviderAdapter interface** with methods:
   - `Task<AIResponse> SendCompletionAsync(AIRequest request, CancellationToken cancellationToken)`
   - `Task<bool> IsHealthyAsync(CancellationToken cancellationToken)`
   - `string ProviderName { get; }`
   - `string ModelVersion { get; }`

3. **Define unified DTOs**:
   - `AIRequest`: RequestId (Guid), RequestType (enum), Prompt (string), SystemMessage (string), MaxInputTokens (int), MaxOutputTokens (int), Temperature (float), Metadata (Dictionary), CorrelationId (string)
   - `AIResponse`: RequestId (Guid), Content (string), ProviderName (string), ModelVersion (string), InputTokensUsed (int), OutputTokensUsed (int), ConfidenceScore (float), LatencyMs (long), Success (bool), ErrorMessage (string)
   - `AIRequestType` enum: DocumentParsing, ConversationalIntake, MedicalCoding
   - `TokenBudget`: MaxInputTokens (int), MaxOutputTokens (int) per request type

4. **Implement request validation middleware** that validates:
   - Request schema completeness (required fields present)
   - Token budget limits per request type (AIR-O01: 4K/1K, AIR-O02: 500/200, AIR-O03: 2K/500)
   - Request payload size bounds
   - Returns 400 Bad Request with structured error for invalid requests

5. **Implement authentication middleware** that validates JWT tokens on AI Gateway endpoints using ASP.NET Core Identity claims, enforcing role-based access (Staff/Admin only for AI operations).

6. **Implement response normalization** that maps provider-specific response formats to unified AIResponse DTO, stripping provider internals and ensuring consistent contract for all consumers.

7. **Configure DI registrations** in `AIGatewayServiceCollectionExtensions.cs`:
   - Register `IAIGatewayService` → `AIGatewayService` (scoped)
   - Register configuration options from `appsettings.json` section `AIGateway`
   - Register middleware pipeline

8. **Implement structured logging** with Serilog enrichment for AI requests: CorrelationId, RequestType, ProviderName, TokensUsed, LatencyMs. Use `ILogger<AIGatewayService>` for request/response lifecycle logging.

## Current Project State

```
[Placeholder — update after US_001 scaffold is complete]
Server/
├── Program.cs
├── appsettings.json
├── Features/
│   └── AIGateway/           ← NEW (this task)
│       ├── Contracts/
│       ├── Middleware/
│       ├── Services/
│       ├── Configuration/
│       ├── Extensions/
│       └── Providers/       ← placeholder for task_002
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Features/AIGateway/Contracts/IAIProviderAdapter.cs | Provider abstraction interface with SendCompletionAsync, IsHealthyAsync |
| CREATE | Server/Features/AIGateway/Contracts/AIRequest.cs | Unified request DTO with RequestType, Prompt, TokenBudget fields |
| CREATE | Server/Features/AIGateway/Contracts/AIResponse.cs | Unified response DTO with Content, ConfidenceScore, LatencyMs |
| CREATE | Server/Features/AIGateway/Contracts/AIRequestType.cs | Enum: DocumentParsing, ConversationalIntake, MedicalCoding |
| CREATE | Server/Features/AIGateway/Contracts/TokenBudget.cs | Token budget configuration per request type |
| CREATE | Server/Features/AIGateway/Middleware/AIRequestValidationMiddleware.cs | Validates schema, token budget limits per AIR-O01/O02/O03 |
| CREATE | Server/Features/AIGateway/Middleware/AIAuthenticationMiddleware.cs | JWT validation for AI Gateway endpoints (Staff/Admin roles) |
| CREATE | Server/Features/AIGateway/Middleware/AIResponseNormalizationMiddleware.cs | Maps provider responses to unified AIResponse |
| CREATE | Server/Features/AIGateway/Services/IAIGatewayService.cs | Gateway service interface |
| CREATE | Server/Features/AIGateway/Services/AIGatewayService.cs | Core orchestration: route requests, invoke provider, normalize response |
| CREATE | Server/Features/AIGateway/Configuration/AIGatewayOptions.cs | POCO for appsettings.json AIGateway section |
| CREATE | Server/Features/AIGateway/Extensions/AIGatewayServiceCollectionExtensions.cs | DI registration extension methods |
| MODIFY | Server/Program.cs | Add builder.Services.AddAIGateway(config) and middleware pipeline |
| MODIFY | Server/appsettings.json | Add AIGateway configuration section (providers, budgets, endpoints) |

## External References

- [ASP.NET Core Middleware — Microsoft Docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-8.0)
- [Vertical Slice Architecture — Jimmy Bogard](https://www.jimmybogard.com/vertical-slice-architecture/)
- [ASP.NET Core Dependency Injection — Microsoft Docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-8.0)
- [Serilog Structured Logging — .NET](https://serilog.net/)
- [API Gateway Pattern — Microsoft](https://learn.microsoft.com/en-us/azure/architecture/microservices/design/gateway)

## Build Commands

- `dotnet build` — Compile AI Gateway project
- `dotnet run` — Start backend with AI Gateway middleware registered

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Token budget enforcement verified (AIR-O01: 4K/1K, AIR-O02: 500/200, AIR-O03: 2K/500)
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] DI container resolves IAIGatewayService without errors
- [ ] Request validation middleware rejects invalid requests with 400 status
- [ ] Authentication middleware rejects unauthenticated requests with 401 status
- [ ] Response normalization produces consistent AIResponse across all request types
- [ ] Structured logging includes CorrelationId, RequestType, LatencyMs fields

## Implementation Checklist

- [ ] Create AI Gateway vertical slice folder structure (Features/AIGateway/ with Contracts, Middleware, Services, Configuration, Extensions, Providers subfolders)
- [ ] Define IAIProviderAdapter interface with SendCompletionAsync, IsHealthyAsync, ProviderName, and ModelVersion contracts
- [ ] Define unified AIRequest/AIResponse DTOs with AIRequestType enum (DocumentParsing, ConversationalIntake, MedicalCoding) and TokenBudget model
- [ ] Implement AIRequestValidationMiddleware enforcing schema validation and token budget limits per request type (AIR-O01: 4K/1K, AIR-O02: 500/200, AIR-O03: 2K/500)
- [ ] Implement AIAuthenticationMiddleware for JWT validation on AI Gateway endpoints (Staff/Admin roles only)
- [ ] Implement AIResponseNormalizationMiddleware mapping provider-specific responses to unified AIResponse DTO
- [ ] Configure DI registrations in AIGatewayServiceCollectionExtensions and wire into Program.cs
- [ ] Implement structured logging with Serilog correlation IDs for AI request tracing (CorrelationId, RequestType, ProviderName, TokensUsed, LatencyMs)
- **[AI Tasks - MANDATORY]** Verify AIR-O01, AIR-O02, AIR-O03 token budget limits are enforced in validation middleware
