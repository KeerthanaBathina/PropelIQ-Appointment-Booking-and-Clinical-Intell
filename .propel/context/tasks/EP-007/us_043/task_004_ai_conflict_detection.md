# Task - task_004_ai_conflict_detection

## Requirement Reference

- User Story: US_043
- Story Location: .propel/context/tasks/EP-007/us_043/us_043.md
- Acceptance Criteria:
  - AC-1: Given multiple documents have been parsed for a patient, When consolidation runs, Then the system merges extracted medications, diagnoses, procedures, and allergies into a unified patient profile. (conflict detection during merge)
- Edge Case:
  - Identical data points: System deduplicates by matching drug name + dosage or diagnosis code + date and retains the higher-confidence entry.

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
| AI Model Provider | OpenAI GPT-4o-mini (Primary) | 2024-07-18 |
| AI Model Provider | Anthropic Claude 3.5 Sonnet (Fallback) | claude-3-5-sonnet-20241022 |
| AI Gateway | Custom .NET Service with Polly | 8.x |
| Vector Store | PostgreSQL pgvector | 0.5.x |
| Embedding Model | OpenAI text-embedding-3-small | 2024 |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-005, AIR-S09, AIR-S10, AIR-O01, AIR-O04, AIR-O08, AIR-Q07 |
| **AI Pattern** | Hybrid (RAG + Tool Calling) |
| **Prompt Template Path** | prompts/consolidation/ |
| **Guardrails Config** | config/ai-guardrails/conflict-detection.json |
| **Model Provider** | OpenAI GPT-4o-mini (Primary), Anthropic Claude 3.5 Sonnet (Fallback) |

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

Implement the AI-powered conflict detection and deduplication service that analyzes extracted clinical data across multiple patient documents. The service uses GPT-4o-mini (with Claude 3.5 Sonnet fallback) through the AI Gateway to identify contradictions (medication contraindications, conflicting diagnoses), flag critical conflicts for urgent staff review (AIR-S09), validate chronological plausibility of clinical events (AIR-S10), and classify conflicts by severity. RAG retrieval provides medical terminology context for accurate conflict analysis.

## Dependent Tasks

- US_040 (EP-006) - Requires extracted data from document parsing
- EP-012 (AI Gateway) - Requires AI Gateway service with Polly resilience for provider routing
- EP-014 (RAG Pipeline) - Requires pgvector medical terminology index for context retrieval

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `IConflictDetectionService` interface | Server (AI Layer) |
| NEW | `ConflictDetectionService` implementation | Server (AI Layer) |
| NEW | Prompt template: `conflict-detection.liquid` | Server (Prompts) |
| NEW | DTOs: `ConflictAnalysisResult`, `DetectedConflict`, `ConflictSeverity` | Server (Models) |
| NEW | Guardrails config for output schema validation | Server (Config) |

## Implementation Plan

1. Define `IConflictDetectionService` interface with method: `DetectConflictsAsync(List<MergedDataPoint> existingData, List<MergedDataPoint> newData, Guid patientId)` returning `ConflictAnalysisResult`
2. Create versioned prompt template (`conflict-detection.liquid`) that instructs the LLM to:
   - Compare existing patient data against newly extracted data
   - Identify medication contraindications (e.g., drug-drug interactions)
   - Detect conflicting diagnoses (same condition, different dates or severity)
   - Flag chronologically implausible events (AIR-S10)
   - Return structured JSON with conflict type, severity, affected data points, and reasoning
3. Implement RAG context retrieval: query pgvector for top-5 medical terminology chunks (cosine similarity >= 0.75) relevant to the medications/diagnoses being analyzed (AIR-R02)
4. Enforce token budget: 4000 input tokens / 1000 output tokens per conflict detection request (AIR-O01)
5. Implement AI Gateway integration with Polly circuit breaker (open after 5 consecutive failures, retry after 30s per AIR-O04) and 3 retries with exponential backoff (AIR-O08)
6. Implement output schema validation using System.Text.Json to parse and validate LLM response against `ConflictAnalysisResult` schema
7. Classify conflicts by severity: Critical (medication contraindication → urgent flag per AIR-S09), High (conflicting diagnoses), Medium (date discrepancy), Low (duplicate data)
8. Redact PII from prompts before API calls (AIR-S01) and log all prompts/responses to audit trail (AIR-S04)

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  AI/
    Gateway/
      AIGatewayService.cs
    RAG/
      RagRetrievalService.cs
    PII/
      PiiRedactor.cs
  Prompts/
  Config/
  Models/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/AI/Services/Interfaces/IConflictDetectionService.cs | Interface for conflict detection with typed input/output |
| CREATE | Server/AI/Services/ConflictDetectionService.cs | LLM-powered conflict analysis with RAG context, circuit breaker, and output validation |
| CREATE | Server/Prompts/consolidation/conflict-detection.liquid | Versioned prompt template for conflict analysis with structured JSON output instructions |
| CREATE | Server/Models/AI/ConflictAnalysisResult.cs | DTO: list of conflicts, summary statistics, processing metadata |
| CREATE | Server/Models/AI/DetectedConflict.cs | DTO: conflict type, severity, affected data point IDs, LLM reasoning, confidence score |
| CREATE | Server/Models/AI/ConflictSeverity.cs | Enum: Critical, High, Medium, Low |
| CREATE | Server/Config/ai-guardrails/conflict-detection.json | Output schema definition and validation rules |

## External References

- [OpenAI GPT-4o-mini API](https://platform.openai.com/docs/models/gpt-4o-mini) - Model capabilities and token limits
- [Polly Circuit Breaker](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker) - Circuit breaker pattern for AI provider failures
- [pgvector Cosine Similarity](https://github.com/pgvector/pgvector#querying) - Vector similarity search for RAG retrieval
- [Liquid Template Engine](https://shopify.github.io/liquid/) - Prompt template variable substitution

## Build Commands

- `dotnet build Server/`
- `dotnet run --project Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Conflict detection identifies medication contraindication as Critical severity
- [ ] Circuit breaker opens after 5 consecutive AI provider failures and routes to fallback

## Implementation Checklist

- [ ] Define `IConflictDetectionService` interface with `DetectConflictsAsync` method accepting existing and new data points
- [ ] Create `conflict-detection.liquid` prompt template with structured JSON output schema, medical conflict instructions, and chronological validation rules
- [ ] Implement RAG context retrieval querying pgvector for top-5 medical terminology chunks with cosine similarity >= 0.75
- [ ] Enforce token budget (4000 input / 1000 output) with pre-request token counting and truncation logic
- [ ] Implement AI Gateway call with Polly circuit breaker (5 failures → open, 30s retry) and exponential backoff (3 retries)
- [ ] Implement output schema validation parsing LLM JSON response into `ConflictAnalysisResult` with fallback for malformed responses
- [ ] Classify conflicts by severity (Critical/High/Medium/Low) and flag Critical conflicts for urgent staff notification (AIR-S09)
- [ ] Implement PII redaction on prompt inputs (AIR-S01) and structured audit logging of all AI requests/responses (AIR-S04)
