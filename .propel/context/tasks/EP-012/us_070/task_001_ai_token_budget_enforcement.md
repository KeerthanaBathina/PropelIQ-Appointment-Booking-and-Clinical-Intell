# Task - task_001_ai_token_budget_enforcement

## Requirement Reference

- User Story: US_070
- Story Location: .propel/context/tasks/EP-012/us_070/us_070.md
- Acceptance Criteria:
    - **AC-1**: Given the AI Gateway processes requests, When a request's token count exceeds the configured budget, Then the request is rejected with a "token budget exceeded" error before being sent to the provider.
- Edge Case:
    - None directly applicable (token budget enforcement is deterministic rejection)

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
| Testing | xUnit + Moq | 2.x / 4.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O01, AIR-O02, AIR-O03 |
| **AI Pattern** | Tool Calling / Gateway |
| **Prompt Template Path** | N/A (infrastructure task, no prompt templates) |
| **Guardrails Config** | Token budget configuration in appsettings.json |
| **Model Provider** | OpenAI (Primary) / Anthropic (Fallback) |

### **CRITICAL: AI Implementation Requirement (AI Tasks Only)**

**IF AI Impact = Yes:**

- **MUST** enforce token budget limits per AIR-O01 (4000 input / 1000 output for document parsing), AIR-O02 (500 input / 200 output for intake), AIR-O03 (2000 input / 500 output for medical coding)
- **MUST** reject requests exceeding budget BEFORE sending to provider
- **MUST** return structured error response with "token budget exceeded" message
- **MUST** log all token budget enforcement decisions for audit (redact PII)
- **MUST** handle model failures gracefully (timeout, rate limit, 5xx errors)

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement per-request token budget enforcement for the AI Gateway service. This service validates incoming AI requests against configured token limits before forwarding to the LLM provider. Three distinct budget profiles are enforced:

- **Document Parsing** (AIR-O01): 4000 input tokens, 1000 output tokens
- **Conversational Intake** (AIR-O02): 500 input tokens, 200 output tokens
- **Medical Coding** (AIR-O03): 2000 input tokens, 500 output tokens

Requests exceeding the configured budget are rejected with a structured error before any provider call is made, ensuring cost control and predictable API spending.

## Dependent Tasks

- US_068 tasks (OpenAI GPT-4o-mini Primary Provider Integration) - Requires primary provider integration to be in place
- US_069 tasks (Claude 3.5 Sonnet Fallback Provider Integration) - Requires fallback provider integration
- US_067 tasks (AI Gateway Foundation) - Requires base AI Gateway service scaffold

## Impacted Components

- **NEW** `AiGateway/Configuration/TokenBudgetOptions.cs` - Configuration POCO for per-request-type token limits
- **NEW** `AiGateway/Services/ITokenBudgetValidator.cs` - Interface for token budget validation
- **NEW** `AiGateway/Services/TokenBudgetValidator.cs` - Implementation with token counting and budget enforcement
- **NEW** `AiGateway/Models/AiRequestType.cs` - Enum for AI request types (DocumentParsing, ConversationalIntake, MedicalCoding)
- **NEW** `AiGateway/Models/TokenBudgetResult.cs` - Result object for budget validation (IsWithinBudget, ActualTokenCount, BudgetLimit, RequestType)
- **MODIFY** `AiGateway/Services/AiGatewayService.cs` - Integrate token budget validation into request pipeline (pre-provider call)
- **MODIFY** `appsettings.json` - Add TokenBudget configuration section

## Implementation Plan

1. **Define configuration model**: Create `TokenBudgetOptions` class mapping to `appsettings.json` configuration with per-request-type input/output token limits. Use the .NET Options pattern (`IOptions<TokenBudgetOptions>`) for injection.

2. **Create request type enum**: Define `AiRequestType` enum with values `DocumentParsing`, `ConversationalIntake`, `MedicalCoding` to categorize incoming requests for budget lookup.

3. **Implement token counting**: Use `Microsoft.ML.Tokenizers` (SharpToken) or equivalent .NET tokenizer compatible with OpenAI's tiktoken (cl100k_base encoding for GPT-4o-mini). Count input tokens from the prompt/messages payload before sending to the provider.

4. **Build validation service**: Implement `ITokenBudgetValidator` with a `ValidateAsync(string prompt, AiRequestType requestType)` method that:
   - Counts tokens in the input payload
   - Compares against the configured budget for the request type
   - Returns a `TokenBudgetResult` indicating pass/fail with details

5. **Integrate into AI Gateway pipeline**: Insert token budget validation as a middleware step in `AiGatewayService` BEFORE the provider call. If validation fails, short-circuit with a 422 Unprocessable Entity response containing a structured error body.

6. **Add structured logging**: Log every budget check (request type, token count, limit, outcome) using Serilog with correlation IDs. Redact any PII from logged prompt content.

7. **Configure via appsettings.json**: Add `TokenBudget` section with per-type limits so they can be changed without redeployment.

## Current Project State

- [Placeholder - to be updated based on completion of dependent tasks from US_067, US_068, US_069]

```
Server/
├── AiGateway/
│   ├── Configuration/
│   ├── Models/
│   ├── Services/
│   └── ...
├── appsettings.json
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/AiGateway/Configuration/TokenBudgetOptions.cs | Configuration POCO with InputTokenLimit and OutputTokenLimit per AiRequestType |
| CREATE | Server/AiGateway/Models/AiRequestType.cs | Enum: DocumentParsing, ConversationalIntake, MedicalCoding |
| CREATE | Server/AiGateway/Models/TokenBudgetResult.cs | Validation result: IsWithinBudget, ActualInputTokens, ActualOutputTokens, InputLimit, OutputLimit, RequestType |
| CREATE | Server/AiGateway/Services/ITokenBudgetValidator.cs | Interface with ValidateAsync method |
| CREATE | Server/AiGateway/Services/TokenBudgetValidator.cs | Token counting via tiktoken-compatible tokenizer + budget comparison logic |
| MODIFY | Server/AiGateway/Services/AiGatewayService.cs | Add pre-request token budget validation call; reject if over budget |
| MODIFY | Server/appsettings.json | Add TokenBudget configuration section with per-type limits |

## External References

- [Polly v8 Documentation - Resilience Pipelines](https://www.pollydocs.org/)
- [Microsoft.ML.Tokenizers NuGet - .NET tokenizer for OpenAI models](https://www.nuget.org/packages/Microsoft.ML.Tokenizers)
- [OpenAI Tokenizer - tiktoken cl100k_base encoding](https://platform.openai.com/tokenizer)
- [ASP.NET Core Options Pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0)
- [Serilog Structured Logging](https://serilog.net/)

## Build Commands

- `dotnet build` - Build the solution
- `dotnet test` - Run unit tests
- `dotnet run --project Server` - Run the backend server

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Token budget correctly rejects requests exceeding AIR-O01 limits (4000 input / 1000 output)
- [ ] Token budget correctly rejects requests exceeding AIR-O02 limits (500 input / 200 output)
- [ ] Token budget correctly rejects requests exceeding AIR-O03 limits (2000 input / 500 output)
- [ ] Requests within budget pass through to provider successfully
- [ ] Rejection response returns structured error with "token budget exceeded" message
- [ ] Structured logging captures token counts, limits, and outcomes with correlation IDs

## Implementation Checklist

- [ ] Create `TokenBudgetOptions` configuration class with per-request-type input/output limits and register with `IOptions<T>` pattern
- [ ] Create `AiRequestType` enum (DocumentParsing, ConversationalIntake, MedicalCoding)
- [ ] Create `TokenBudgetResult` model with validation outcome fields
- [ ] Implement `ITokenBudgetValidator` interface with `ValidateAsync` method signature
- [ ] Implement `TokenBudgetValidator` with tiktoken-compatible token counting (cl100k_base encoding) and budget comparison
- [ ] Integrate token budget validation into `AiGatewayService` request pipeline as pre-provider middleware (reject with 422 if over budget)
- [ ] Add `TokenBudget` section to `appsettings.json` with configured limits (DocumentParsing: 4000/1000, Intake: 500/200, Coding: 2000/500)
- [ ] Add structured Serilog logging for all budget enforcement decisions with correlation IDs and PII redaction
- **[AI Tasks - MANDATORY]** Verify AIR-O01, AIR-O02, AIR-O03 requirements are met
