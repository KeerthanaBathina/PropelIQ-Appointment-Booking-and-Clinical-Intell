# Task - TASK_003

## Requirement Reference

- User Story: US_046
- Story Location: .propel/context/tasks/EP-007/us_046/us_046.md
- Acceptance Criteria:
  - AC-1: Given AI confidence for a consolidation operation is below 80%, When the result is generated, Then the system presents the data in a manual review form pre-filled with AI suggestions marked as "low-confidence."
  - AC-2: Given the system detects clinical event dates that violate chronological plausibility (e.g., procedure date before diagnosis date), When the validation runs, Then the conflicting dates are flagged with an explanation of the inconsistency.
  - AC-4: Given the AI service is completely unavailable, When a document is uploaded, Then the system displays a banner "AI unavailable — switch to manual" and provides the manual data entry form.
- Edge Case:
  - Partial date parsing: System saves partial date, flags as "incomplete-date," and presents for staff completion.
  - Timezone: Phase 1 assumes all dates in clinic's local timezone; timezone metadata is ignored.

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
| Frontend | N/A | N/A |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Database | PostgreSQL | 16.x |
| AI/ML | OpenAI GPT-4o-mini (Primary) + Anthropic Claude 3.5 Sonnet (Fallback) | 2024-07-18 / claude-3-5-sonnet-20241022 |
| Vector Store | PostgreSQL pgvector | 0.5.x |
| AI Gateway | Custom .NET Service with Polly | 8.x |
| Mobile | N/A | N/A |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-010, AIR-S10, AIR-Q07, AIR-Q08, AIR-O01, AIR-O04, AIR-O08, AIR-O09, AIR-O10, AIR-S01, AIR-S04 |
| **AI Pattern** | Hybrid (RAG + Tool Calling) |
| **Prompt Template Path** | prompts/consolidation/ |
| **Guardrails Config** | Server/Config/ai-guardrails.json |
| **Model Provider** | OpenAI (Primary) / Anthropic (Fallback) |

> **AI Impact Legend:**
> - **Yes**: Task involves LLM integration, RAG pipeline, prompt engineering, or AI infrastructure

### **CRITICAL: AI Implementation Requirement (AI Tasks Only)**

**IF AI Impact = Yes:**

- **MUST** reference prompt templates from Prompt Template Path during implementation
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

Implement the AI layer configuration for confidence scoring during consolidation operations and chronological date plausibility analysis. This task delivers the prompt templates for confidence-aware extraction, the confidence threshold gate that triggers manual fallback at <80%, date plausibility analysis prompts, circuit breaker configuration for AI unavailability detection, token budget enforcement, and audit logging for AI interactions. This task integrates with the AI Gateway (Polly-based) and feeds results to the backend services (TASK_002).

## Dependent Tasks

- US_043 - Requires consolidation AI pipeline and AI Gateway infrastructure
- US_008 - Requires ExtractedData entity schema for confidence score storage
- TASK_002 (this US) - Backend services consume AI layer outputs

## Impacted Components

- `ConsolidationPromptTemplate` — NEW prompt template for confidence-scored consolidation extraction (Server/Prompts/Consolidation/)
- `DatePlausibilityPromptTemplate` — NEW prompt template for chronological date validation (Server/Prompts/Consolidation/)
- `ConfidenceThresholdGate` — NEW middleware in AI Gateway for confidence threshold evaluation and fallback trigger (Server/Services/AiGateway/)
- `AiGatewayService` — UPDATE to add confidence gate, date validation routing, and circuit breaker for unavailability (Server/Services/AiGateway/)
- `AiAuditLogger` — UPDATE to log consolidation prompts/responses with PII redaction (Server/Services/)
- `AiGuardrailsConfig` — NEW configuration for token budgets, confidence thresholds, retry policies (Server/Config/)

## Implementation Plan

1. **Configure consolidation confidence scoring prompt template** — Create a versioned prompt template for document consolidation that instructs the LLM to return structured JSON output with per-field confidence scores (0.0-1.0). The prompt includes: system role as "clinical data extraction specialist," explicit output schema with `{field, value, confidence_score, source_reference}` structure, instruction to assign calibrated confidence scores reflecting extraction certainty (AIR-Q07). Use Liquid/Handlebars template engine for variable substitution (patient context, document content, RAG chunks). Store in `prompts/consolidation/extract_with_confidence.liquid`.

2. **Implement confidence threshold gate in AI Gateway** — Create a `ConfidenceThresholdGate` component that executes after the AI model returns extraction results. For each extracted data point, evaluate `confidence_score` against the 0.80 threshold (AIR-010, AIR-Q08). If any entry falls below threshold, mark that entry as `flagged_for_review = true` and the overall consolidation result as `requires_manual_review = true`. If the aggregate mean confidence is below 0.80, flag the entire batch for manual fallback. Return structured metadata to the backend `ConsolidationConfidenceService` (TASK_002).

3. **Configure date plausibility analysis prompt template** — Create a prompt template that sends extracted date-bearing data points to the LLM for chronological plausibility analysis (AIR-S10). The prompt includes pairs of clinical events with dates, asks the model to identify violations (procedure before diagnosis, discharge before admission, follow-up before initial visit), and returns structured JSON with `{event_pair, violation_type, explanation}`. Include few-shot examples of valid and invalid chronological sequences. Store in `prompts/consolidation/date_plausibility_check.liquid`.

4. **Implement circuit breaker and unavailability detection** — Configure Polly circuit breaker policy for the AI Gateway: open after 5 consecutive failures (AIR-O04), half-open retry after 30 seconds. When circuit breaker is open, the `AiGatewayService` sets a Redis-cached flag `ai:health:consolidation = unavailable` (consumed by `AiHealthCheckService` in TASK_002). Implement retry with exponential backoff: max 3 retries with 1s, 2s, 4s delays (AIR-O08). After 3 retries fail, trigger manual fallback routing.

5. **Enforce token budget for consolidation operations** — Configure token limits per AIR-O01: 4000 input tokens and 1000 output tokens for document parsing/consolidation requests. Implement pre-request token counting using tiktoken-compatible library for .NET. If input exceeds budget, chunk the document and process in multiple requests. If output exceeds budget, truncate and flag for manual review. Log token usage per request for cost tracking (AIR-O09).

6. **Implement PII redaction and audit logging** — Extend `AiAuditLogger` to log all consolidation AI requests and responses with correlation IDs (AIR-S04). Before sending to AI provider, invoke the PII redaction pipeline (AIR-S01) to strip patient names, DOB, SSN, and contact info from prompt content. Log redacted prompts and full responses (responses don't contain PII since input was redacted). Store audit entries with `patient_id` correlation for compliance review. Use structured logging via Serilog.

7. **Implement fallback model routing** — Configure the AI Gateway to route consolidation requests to OpenAI GPT-4o-mini as primary provider. When the rolling mean of the last 5 GPT-4o-mini requests falls below 0.80 confidence for a specific document type, or when the primary provider fails (circuit breaker open), route to Anthropic Claude 3.5 Sonnet as fallback (per UC-006 sequence diagram). Track provider performance metrics: latency, confidence distribution, and failure rate per provider for A/B comparison (AIR-O10).

## Current Project State

- Project structure is placeholder; to be updated based on completion of dependent tasks (US_043, US_008).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Prompts/Consolidation/extract_with_confidence.liquid | Prompt template for confidence-scored clinical data extraction |
| CREATE | Server/Prompts/Consolidation/date_plausibility_check.liquid | Prompt template for chronological date plausibility analysis |
| CREATE | Server/Services/AiGateway/ConfidenceThresholdGate.cs | Middleware evaluating per-entry and aggregate confidence against 80% threshold |
| CREATE | Server/Services/AiGateway/IConfidenceThresholdGate.cs | Interface for confidence threshold gate |
| CREATE | Server/Config/ai-guardrails.json | Configuration for token budgets, confidence thresholds, retry policies |
| MODIFY | Server/Services/AiGateway/AiGatewayService.cs | Add confidence gate, date validation routing, circuit breaker for unavailability |
| MODIFY | Server/Services/AiAuditLogger.cs | Add consolidation-specific audit logging with PII redaction |
| MODIFY | Server/Services/AiGateway/PollyPolicies.cs | Add circuit breaker (5 failures / 30s retry) and retry (3x exponential backoff) for consolidation |
| CREATE | Server/Models/ConfidenceGateResult.cs | Model for confidence evaluation result with per-entry and aggregate scores |
| CREATE | Server/Models/DatePlausibilityResult.cs | Model for date plausibility violation results |

## External References

- OpenAI GPT-4o-mini API documentation: https://platform.openai.com/docs/models/gpt-4o-mini
- Anthropic Claude 3.5 Sonnet API documentation: https://docs.anthropic.com/en/docs/about-claude/models
- Polly resilience framework (circuit breaker): https://www.thepollyproject.org/
- tiktoken .NET library for token counting: https://github.com/aiqinxuancai/TiktokenSharp
- Liquid template engine for .NET: https://github.com/dotliquid/dotliquid
- Serilog structured logging: https://serilog.net/
- pgvector .NET integration: https://github.com/pgvector/pgvector-dotnet

## Build Commands

- Refer to applicable technology stack specific build commands in .propel/build/

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Confidence scoring prompt returns calibrated scores (0.0-1.0) with structured JSON output
- [ ] Threshold gate correctly flags entries below 80% and triggers manual fallback
- [ ] Date plausibility prompt identifies chronological violations with explanations
- [ ] Circuit breaker opens after 5 failures and auto-recovers after 30 seconds
- [ ] Token budget enforced: 4K input / 1K output for consolidation requests
- [ ] PII redacted from all prompts before AI provider calls
- [ ] Fallback routing switches to Claude when GPT-4o-mini fails or returns low confidence

## Implementation Checklist

- [ ] Configure consolidation confidence scoring prompt template with structured JSON output schema
- [ ] Implement ConfidenceThresholdGate evaluating per-entry and aggregate confidence against 80% threshold
- [ ] Configure date plausibility analysis prompt template with few-shot examples and violation detection
- [ ] Implement circuit breaker (5 failures / 30s retry) and exponential backoff retry (3x) in AI Gateway
- [ ] Enforce token budget (4K input / 1K output) with pre-request counting and chunking strategy
- [ ] Implement PII redaction pipeline and audit logging for all consolidation AI interactions
- [ ] Implement fallback model routing from GPT-4o-mini to Claude 3.5 Sonnet with performance tracking
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-XXX requirements are met (AIR-010, AIR-S10, AIR-Q07, AIR-Q08, AIR-O01, AIR-O04, AIR-O08, AIR-S01, AIR-S04)
