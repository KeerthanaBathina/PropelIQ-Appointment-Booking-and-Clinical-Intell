# Task - TASK_003

## Requirement Reference

- User Story: US_047
- Story Location: .propel/context/tasks/EP-008/us_047/us_047.md
- Acceptance Criteria:
  - AC-1: **Given** clinical diagnoses have been extracted from patient documents, **When** AI coding runs, **Then** the system maps each diagnosis to the most appropriate ICD-10 code with a justification explaining the mapping rationale.
  - AC-2: **Given** an ICD-10 mapping is generated, **When** the results are displayed, **Then** each code shows the ICD-10 code, description, confidence score, and justification text.
  - AC-4: **Given** multiple ICD-10 codes apply to a single diagnosis, **When** the AI identifies this, **Then** the system presents all applicable codes ranked by relevance.
- Edge Case:
  - What happens when the AI cannot find a matching ICD-10 code? System assigns "uncodable" status with a confidence of 0.00 and flags for manual coding.

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
| AI/ML | OpenAI GPT-4o-mini (Primary) | 2024-07-18 |
| AI/ML | Anthropic Claude 3.5 Sonnet (Fallback) | claude-3-5-sonnet-20241022 |
| Vector Store | PostgreSQL pgvector | 0.5.x |
| Embedding Model | OpenAI text-embedding-3-small | 2024 |
| AI Gateway | Custom .NET Service with Polly | 8.x |
| Database | PostgreSQL | 16.x |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-003, AIR-S01, AIR-S02, AIR-S04, AIR-O01, AIR-O03, AIR-O04, AIR-O08, AIR-R01, AIR-R02, AIR-R03, AIR-R04, AIR-R05 |
| **AI Pattern** | RAG + Tool Calling (Hybrid) |
| **Prompt Template Path** | prompts/medical-coding/icd10-mapping/ |
| **Guardrails Config** | config/ai-guardrails/coding-guardrails.json |
| **Model Provider** | OpenAI GPT-4o-mini (Primary), Anthropic Claude 3.5 Sonnet (Fallback) |

### **CRITICAL: AI Implementation Requirement**

- **MUST** reference prompt templates from Prompt Template Path during implementation
- **MUST** implement guardrails for input sanitization and output validation
- **MUST** enforce token budget limits per AIR-O03 (2000 input / 500 output tokens)
- **MUST** implement fallback logic for low-confidence responses
- **MUST** log all prompts/responses for audit (redact PII per AIR-S01)
- **MUST** handle model failures gracefully (timeout, rate limit, 5xx errors)

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the AI pipeline for ICD-10 diagnosis code mapping, including prompt configuration, RAG retrieval of ICD-10 coding guidelines from pgvector, LLM integration via the AI Gateway with multi-provider fallback (GPT-4o-mini primary, Claude 3.5 Sonnet fallback), structured output validation, confidence scoring, multi-code ranking, and comprehensive guardrails. This task implements the `IAiCodingGateway` interface defined in task_002 and provides the core intelligence for mapping clinical diagnoses to ICD-10 codes with human-readable justification text (AIR-003, FR-061).

## Dependent Tasks

- task_001_db_icd10_code_library_schema.md — ICD-10 reference library must exist for RAG knowledge base population and code validation.
- task_002_be_icd10_mapping_api.md — Defines `IAiCodingGateway` interface this task implements.

## Impacted Components

- **NEW** `AiCodingGateway` — Implementation of `IAiCodingGateway` for ICD-10 mapping
- **NEW** `Icd10PromptBuilder` — Prompt template composition with RAG context injection
- **NEW** `Icd10ResponseParser` — Structured output parsing and validation
- **NEW** `CodingGuardrailsService` — Input sanitization (PII redaction) and output validation
- **NEW** Prompt templates — Versioned Liquid/Handlebars templates for ICD-10 mapping
- **NEW** RAG retrieval module — pgvector query for coding guideline chunks
- **MODIFY** AI Gateway configuration — Register ICD-10 coding route with Polly resilience policies

## Implementation Plan

1. **Create prompt templates** for ICD-10 mapping:
   - System prompt: Define the AI role as medical coding assistant, instruct to output structured JSON with ICD-10 codes, descriptions, confidence scores (0.0-1.0), and justification text per code.
   - User prompt template: Inject diagnosis text, patient context (age, relevant conditions), and RAG-retrieved coding guideline chunks.
   - Output schema: JSON array of `{ code: string, description: string, confidence: float, justification: string }` sorted by relevance.
   - Version control: Store templates in `prompts/medical-coding/icd10-mapping/` with version suffix (e.g., `v1.0`).

2. **Implement RAG retrieval for ICD-10 coding guidelines**:
   - Query pgvector index `icd10_coding_guidelines` with diagnosis text embedding.
   - Retrieve top-5 chunks with cosine similarity >= 0.75 (AIR-R02).
   - Re-rank retrieved chunks using semantic similarity (AIR-R03).
   - Use separate vector index for coding guidelines per AIR-R04.
   - Generate diagnosis embedding using OpenAI text-embedding-3-small (384 dimensions).

3. **Implement `AiCodingGateway`** (implements `IAiCodingGateway`):
   - Compose prompt: Combine system prompt + RAG context + diagnosis data.
   - Enforce token budget: 2000 input tokens, 500 output tokens (AIR-O03). Truncate context if exceeded.
   - Primary request to GPT-4o-mini via AI Gateway with Polly policies.
   - Fallback to Claude 3.5 Sonnet if GPT fails or confidence < 0.80 (AIR-O04, AIR-010).
   - Circuit breaker: Open after 5 consecutive failures, retry after 30 seconds (AIR-O04).
   - Retry: Up to 3 retries with exponential backoff for transient failures (AIR-O08).
   - Request JSON mode for structured output validation.

4. **Implement `CodingGuardrailsService`**:
   - **Input sanitization**: Redact PII (patient name, DOB, SSN) from prompts before external API calls (AIR-S01). Use regex-based pattern matching for PII detection.
   - **Output validation**: Validate AI response schema (code format matches ICD-10 pattern `[A-Z]\d{2}(\.\d{1,4})?`), confidence within 0.0-1.0 range, justification non-empty.
   - **Code library validation**: Cross-reference each suggested code against `Icd10CodeLibrary.is_current = true` (AIR-S02, DR-015).
   - **Content filtering**: Reject responses containing inappropriate or harmful content (AIR-S05).
   - **Hallucination guard**: Flag justifications that contradict RAG-retrieved guidelines (AIR-Q06 < 5%).

5. **Implement `Icd10ResponseParser`**:
   - Parse JSON response from LLM into structured `Icd10CodeSuggestion` objects.
   - Assign `relevance_rank` based on confidence score ordering (AC-4).
   - Handle edge case: If no codes returned or all confidence < threshold, return "uncodable" result with confidence 0.00.
   - Handle malformed JSON: Return fallback uncodable status, log parsing failure.

6. **Implement audit logging**:
   - Log all prompts and responses with `patient_id` correlation (AIR-S04).
   - Redact PII from logged prompts (AIR-S01).
   - Include token usage, latency, model provider, and confidence scores in log entries.
   - Use Serilog structured logging with correlation IDs (NFR-035).

## Current Project State

```text
[Placeholder — to be updated based on dependent task completion]
Server/
├── Services/
│   ├── AI/
│   │   ├── AiCodingGateway.cs             # New — implements IAiCodingGateway
│   │   ├── Icd10PromptBuilder.cs          # New — prompt composition
│   │   ├── Icd10ResponseParser.cs         # New — response parsing
│   │   └── CodingGuardrailsService.cs     # New — input/output guardrails
│   └── RAG/
│       └── Icd10RagRetriever.cs           # New — pgvector retrieval
├── Config/
│   └── ai-guardrails/
│       └── coding-guardrails.json         # New — guardrail configuration
└── prompts/
    └── medical-coding/
        └── icd10-mapping/
            ├── system-prompt.v1.0.liquid   # New — system prompt template
            └── user-prompt.v1.0.liquid     # New — user prompt template
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/AI/AiCodingGateway.cs | AI Gateway implementation for ICD-10 mapping with multi-provider fallback |
| CREATE | Server/Services/AI/Icd10PromptBuilder.cs | Prompt template composition with RAG context injection |
| CREATE | Server/Services/AI/Icd10ResponseParser.cs | Structured JSON response parsing and validation |
| CREATE | Server/Services/AI/CodingGuardrailsService.cs | PII redaction, output schema validation, code library validation |
| CREATE | Server/Services/RAG/Icd10RagRetriever.cs | pgvector cosine similarity query for coding guideline retrieval |
| CREATE | Server/Config/ai-guardrails/coding-guardrails.json | Guardrail thresholds configuration (confidence, token limits) |
| CREATE | Server/prompts/medical-coding/icd10-mapping/system-prompt.v1.0.liquid | System prompt template for ICD-10 coding role |
| CREATE | Server/prompts/medical-coding/icd10-mapping/user-prompt.v1.0.liquid | User prompt template with diagnosis and RAG context slots |
| MODIFY | Server/Program.cs | Register AI services, guardrails, and RAG retriever in DI container |

## External References

- [OpenAI GPT-4o-mini API documentation](https://platform.openai.com/docs/models/gpt-4o-mini)
- [OpenAI text-embedding-3-small documentation](https://platform.openai.com/docs/guides/embeddings)
- [pgvector 0.5.x documentation](https://github.com/pgvector/pgvector)
- [Polly 8 Circuit Breaker documentation](https://www.thepollyproject.org/docs/strategies/circuit-breaker/)
- [ICD-10-CM Official Guidelines for Coding](https://www.cdc.gov/nchs/icd/icd-10-cm/index.html)

## Build Commands

- `dotnet build Server`
- `dotnet test Server.Tests`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified (2000 in / 500 out)
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] RAG retrieval returns top-5 chunks with cosine similarity >= 0.75 for test diagnosis
- [ ] GPT-4o-mini produces valid ICD-10 code format in structured JSON
- [ ] Claude fallback triggers when GPT confidence < 0.80 or GPT fails
- [ ] Circuit breaker opens after 5 consecutive failures and retries after 30s
- [ ] Uncodable diagnosis returns confidence 0.00 with appropriate justification

## Implementation Checklist

- [ ] Create versioned prompt templates (system + user) in `prompts/medical-coding/icd10-mapping/`
- [ ] Implement `Icd10RagRetriever` with pgvector cosine similarity query (top-5, threshold >= 0.75)
- [ ] Implement `AiCodingGateway` with GPT-4o-mini primary, Claude 3.5 Sonnet fallback, Polly circuit breaker and retry
- [ ] Implement `CodingGuardrailsService` with PII redaction, ICD-10 format validation, code library cross-reference
- [ ] Implement `Icd10ResponseParser` with JSON parsing, relevance ranking, and uncodable fallback handling
- [ ] Implement audit logging with PII redaction, token usage, latency, and correlation IDs
- [ ] Configure guardrails thresholds in `coding-guardrails.json` (confidence threshold, token budgets)
- [ ] Register all AI services in DI container and verify end-to-end pipeline with test diagnosis
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-003, AIR-S01, AIR-S02, AIR-S04, AIR-O03, AIR-O04 requirements are met
