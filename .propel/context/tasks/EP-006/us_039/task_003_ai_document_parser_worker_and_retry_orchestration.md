# Task - task_003_ai_document_parser_worker_and_retry_orchestration

## Requirement Reference

- User Story: US_039
- Story Location: .propel/context/tasks/EP-006/us_039/us_039.md
- Acceptance Criteria:
    - AC-2: Given a parsing job is picked up from the queue, When the AI parser begins processing, Then the status updates to `parsing` and the system emits the corresponding staff notification event.
    - AC-3: Given the AI parser completes extraction, When results are ready, Then the status updates to `parsed` and the review-result notification payload is available.
    - AC-4: Given document parsing fails, When the failure is detected, Then the system retries up to 3 times with exponential backoff and logs each failure.
    - AC-5: Given all retry attempts fail, When the final retry fails, Then the status is set to `failed` and the worker returns a manual-review fallback outcome.
- Edge Case:
    - EC-1: If Redis is unavailable and processing falls back to synchronous mode, the parsing worker must still apply the same AI guardrails and failure semantics.
    - EC-2: If the queue floods with 100 or more documents, each worker execution must remain idempotent and safe under the configured concurrency cap.

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
| AI Model Provider | OpenAI GPT-4o-mini + Anthropic Claude 3.5 Sonnet | 2024-07-18 / claude-3-5-sonnet-20241022 |
| AI Gateway | Custom .NET Service with Polly | 8.x |
| Caching | Upstash Redis | 7.x |
| Database | PostgreSQL | 16.x |
| Logging | Serilog | 8.x |
| Language | C# | 12 / .NET 8 |
| Mobile | N/A | N/A |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-002, AIR-S01, AIR-O01, AIR-O07, AIR-O08 |
| **AI Pattern** | Tool Calling |
| **Prompt Template Path** | Server/AI/DocumentParsing/Prompts/ |
| **Guardrails Config** | Server/AI/DocumentParsing/guardrails.json |
| **Model Provider** | OpenAI GPT-4o-mini primary, Claude 3.5 Sonnet fallback via AI Gateway |

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

Implement the AI worker-side orchestration that turns queued clinical documents into parse results. This task owns status transitions into `parsing` and `parsed`, applies prompt construction and guardrails for document parsing, routes requests through the AI Gateway with token budgeting and provider fallback, emits failure logs for each retry attempt, and returns a manual-review outcome after the final failure. The worker must remain transport-agnostic so it can run from Redis dispatch or synchronous fallback mode while preserving the same resilience and audit guarantees.

## Dependent Tasks

- task_002_be_document_parsing_queue_orchestration (Queue payload and dispatch contract must exist)
- US_038 task_002_be_secure_clinical_document_upload_api (Stored documents and secure file retrieval path must exist)
- US_067 - Cross-Epic (EP-012) - AI Gateway abstraction and provider routing must exist
- US_068 - Cross-Epic (EP-012) - Document parsing token-budget enforcement baseline must exist
- task_004_db_document_parsing_status_and_retry_support (Status, retry, and failure-log persistence must exist)

## Impacted Components

- **NEW** `DocumentParsingWorker` - Executes parsing jobs, loads document content, and manages status transitions (Server/AI/DocumentParsing/)
- **NEW** `DocumentParsingPromptBuilder` - Builds versioned prompt payloads for OCR-capable clinical document parsing (Server/AI/DocumentParsing/)
- **NEW** `DocumentParsingResultValidator` - Validates structured parser responses before downstream persistence or status completion (Server/AI/DocumentParsing/)
- **NEW** prompt templates and guardrails config - Versioned document-parsing prompt artifacts and AI safety rules (Server/AI/DocumentParsing/Prompts/)
- **MODIFY** `AiGatewayService` - Accept document parsing requests and provider-fallback semantics needed by the worker (Server/AI/)
- **MODIFY** `DocumentParsingQueueService` - Emit start, success, and terminal-failure outcomes consumable by SCR-012 notifications (Server/Services/Documents/)

## Implementation Plan

1. **Create the document parsing worker orchestration** that loads the stored document, marks it `parsing`, and invokes the AI Gateway through a document-specific prompt path.
2. **Add versioned prompt templates for document parsing** covering system instructions, structured extraction expectations, and manual-review fallback framing for unparseable outputs.
3. **Apply AI guardrails before and after model invocation** by redacting PII from prompts, enforcing the 4K-in or 1K-out token budget, and validating the returned structured payload shape.
4. **Use provider resilience through the AI Gateway** so GPT-4o-mini remains primary, Claude fallback is available for provider failures, and transient parsing failures re-enter the retry schedule.
5. **Emit failure logs on every retry attempt** with enough detail for operational debugging while avoiding PHI leakage in logs or audit trails.
6. **Mark successful executions as `parsed`** and return a result envelope that later EP-006 extraction stories can consume without changing the worker contract.
7. **Return manual-review fallback after the final failed attempt** so the status becomes `failed` and the UI can offer deterministic staff intervention.

## Current Project State

```text
Server/
  AI/
  Services/
    Documents/
  Models/
    Entities/
      ClinicalDocument.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/AI/DocumentParsing/DocumentParsingWorker.cs | Worker orchestration for queued or synchronous document parsing |
| CREATE | Server/AI/DocumentParsing/DocumentParsingPromptBuilder.cs | Builds prompts for clinical document parsing via the AI Gateway |
| CREATE | Server/AI/DocumentParsing/DocumentParsingResultValidator.cs | Validates structured parsing results and failure conditions |
| CREATE | Server/AI/DocumentParsing/Prompts/document-parse-system.liquid | System prompt template for clinical document parsing |
| CREATE | Server/AI/DocumentParsing/Prompts/document-parse-schema.liquid | Structured extraction or output-format prompt template |
| CREATE | Server/AI/DocumentParsing/guardrails.json | PII redaction, token budget, validation, and fallback thresholds for parsing |
| MODIFY | Server/AI/AiGatewayService.cs | Accept and route document parsing requests with provider fallback and retry semantics |
| MODIFY | Server/Services/Documents/DocumentParsingQueueService.cs | Invoke the parser worker for queued and fallback synchronous execution paths |

## External References

- OpenAI text-generation guide: https://platform.openai.com/docs/guides/text-generation
- Anthropic API docs: https://docs.anthropic.com/
- Polly resilience pipelines (.NET 8): https://learn.microsoft.com/en-us/dotnet/core/resilience/
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)

## Implementation Checklist

- [x] Create the worker that moves documents into `parsing` and processes them through the AI Gateway
- [x] Add versioned prompt templates and guardrails for clinical document parsing
- [x] Enforce PII redaction and the 4K input or 1K output token budget before external model calls
- [x] Route provider failures through configured fallback behavior and retry scheduling
- [x] Log each parsing failure attempt without leaking PHI
- [x] Mark successful jobs `parsed` and emit a result envelope usable by downstream review flows
- [x] Mark terminal failures `failed` and return a manual-review fallback outcome after the last retry
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-XXX requirements are met (quality, safety, operational)