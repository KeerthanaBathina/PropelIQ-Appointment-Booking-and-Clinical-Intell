# Task - task_004_ai_cpt_prompt_rag

## Requirement Reference

- User Story: US_048
- Story Location: .propel/context/tasks/EP-008/us_048/us_048.md
- Acceptance Criteria:
  - AC-1: Given clinical procedures have been extracted from patient documents, When AI coding runs, Then the system maps each procedure to the most appropriate CPT code with justification text.
  - AC-2: Given a CPT mapping is generated, When the results are displayed, Then each code shows the CPT code, description, confidence score, and justification text.
  - AC-3: Given multiple CPT codes apply to a single procedure, When the AI identifies this, Then the system presents all applicable codes ranked by relevance with multi-code assignment support.
- Edge Case:
  - Ambiguous procedure description: System assigns the closest match with reduced confidence and flags for staff verification.
  - Bundled procedures: System identifies bundling opportunities and presents the bundled code option alongside individual codes.

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
| AI/ML | OpenAI GPT-4o-mini (Primary), Anthropic Claude 3.5 Sonnet (Fallback) | 2024-07-18 / claude-3-5-sonnet-20241022 |
| Vector Store | PostgreSQL pgvector | 0.5.x |
| AI Gateway | Custom .NET Service with Polly | 8.x |
| Embedding Model | OpenAI text-embedding-3-small | 2024 |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-004, AIR-R01, AIR-R02, AIR-R03, AIR-R05, AIR-R06, AIR-S01, AIR-S02, AIR-O03, AIR-O04, AIR-Q05, AIR-Q06, AIR-Q07, AIR-Q08 |
| **AI Pattern** | Hybrid (RAG + Tool Calling) |
| **Prompt Template Path** | prompts/medical-coding/cpt/ |
| **Guardrails Config** | config/ai/cpt-coding-guardrails.json |
| **Model Provider** | OpenAI (Primary), Anthropic (Fallback) |

### **CRITICAL: AI Implementation Requirement (AI Tasks Only)**

**IF AI Impact = Yes:**

- **MUST** reference prompt templates from Prompt Template Path during implementation
- **MUST** implement guardrails for input sanitization and output validation
- **MUST** enforce token budget limits per AIR-O03 requirements
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

Implement the AI layer for CPT procedure code mapping including prompt template engineering, RAG pipeline for CPT coding guidelines retrieval, structured output schema validation, and all AI safety/operational guardrails. This task configures the LLM to analyze clinical procedure descriptions, retrieve relevant CPT coding guidelines via pgvector similarity search, and generate structured CPT code suggestions with confidence scores and justification text. Includes PII redaction, token budget enforcement, fallback logic for low-confidence responses, bundled procedure detection, and audit logging of all AI interactions.

## Dependent Tasks

- US_008 tasks (EP-DATA) — MedicalCode entity must exist for persistence
- US_040 tasks (EP-006) — ExtractedData with procedure data must be available as input
- task_003_db_cpt_code_library — CPT code library must be seeded for validation and RAG context

## Impacted Components

- **NEW** `CptCodingPromptTemplate` — Versioned prompt template for CPT code suggestion
- **NEW** `CptRagPipeline` — RAG retrieval service for CPT coding guidelines from pgvector
- **NEW** `CptCodingOutputSchema` — JSON schema for structured LLM output validation
- **NEW** `CptPiiRedactionService` — PII scrubbing for procedure descriptions before API calls
- **NEW** `CptCodingGuardrails` — Input sanitization and output validation rules
- **NEW** `CptEmbeddingIndexer` — Service to chunk and embed CPT coding guidelines into pgvector
- **MODIFY** AI Gateway service — Add CPT-specific routing, token budget, and fallback configuration

## Implementation Plan

1. **Design CPT coding prompt template** with structured sections: system instructions (role as medical coding specialist, output format constraints), clinical procedure context (procedure description, patient demographics, clinical notes), CPT coding guidelines (injected via RAG retrieval), and output requirements (JSON array of suggestions with cpt_code, description, confidence 0-1, justification text, bundled_flag). Version-control the template with semantic versioning (v1.0.0). Include instructions for handling ambiguous procedures (assign closest match with reduced confidence) and bundled procedures (identify bundling opportunities).
2. **Set up pgvector index for CPT coding guidelines** per AIR-R01: chunk CPT coding reference documents into 512-token segments with 20% overlap (102 tokens). Use OpenAI text-embedding-3-small to generate 384-dimensional embeddings. Create separate vector index `idx_cpt_coding_guidelines` in PostgreSQL. Store metadata (source document, section, CPT category) alongside embeddings for filtered retrieval per AIR-R04.
3. **Implement RAG retrieval pipeline** per AIR-R02/AIR-R03/AIR-R06: for each procedure description, generate embedding → query pgvector for top-5 chunks with cosine similarity ≥ 0.75 → re-rank retrieved chunks using semantic similarity → compose prompt with retrieved context. Implement hybrid search combining semantic similarity and keyword matching (PostgreSQL full-text search) per AIR-R06 for medical entity retrieval.
4. **Implement structured JSON output schema** for CPT suggestions: define schema with fields `cpt_code` (string), `description` (string), `confidence` (float 0-1), `justification` (string, max 500 chars), `is_bundled` (boolean), `bundle_components` (array of cpt_codes, nullable). Use OpenAI JSON mode for schema enforcement. Validate all returned codes against CPT code library before returning to caller. Reject hallucinated codes per AIR-Q06 (<5% hallucination rate).
5. **Enforce token budget** per AIR-O03: limit input tokens to 2000 and output tokens to 500 per CPT coding request. Measure prompt size after RAG context injection. If exceeding budget, truncate least-relevant RAG chunks. Log token usage per request for cost tracking (AIR-O09). Target <5s latency per procedure per AIR-Q05.
6. **Implement confidence score calibration** per AIR-Q07/AIR-Q08: parse model confidence from structured output. Flag codes with confidence < 0.80 with `flagged_for_review = true` in MedicalCode entity. For ambiguous procedures (edge case), the prompt instructs the model to assign reduced confidence and explain ambiguity in justification text. When confidence < 0.80, trigger mandatory manual verification workflow per AIR-010.
7. **Implement bundled procedure detection** in prompt logic: instruct the LLM to identify when multiple procedures could be billed under a single bundled CPT code. Return both the bundled code option and individual codes with relevance rankings. Cross-reference with `cpt_bundle_rules` table from task_003 for validation.
8. **Add PII redaction and audit logging** per AIR-S01/AIR-S04: before sending procedure descriptions to external AI APIs, redact patient name, DOB, SSN, and other PII using regex patterns and named entity recognition. Log all prompts (redacted) and responses in audit trail with patient_id correlation for compliance review. Sanitize inputs to prevent prompt injection per AIR-S06.

## Current Project State

- No AI pipeline exists yet (green-field). AI Gateway infrastructure to be established by foundational tasks (EP-TECH).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/AI/Prompts/MedicalCoding/CptCodingPromptTemplate.cs | Versioned prompt template class with variable substitution |
| CREATE | Server/AI/Prompts/MedicalCoding/cpt-coding-v1.0.0.liquid | Liquid template file for CPT coding system/user prompts |
| CREATE | Server/AI/Rag/CptRagPipeline.cs | RAG retrieval service: embed → search pgvector → re-rank → compose |
| CREATE | Server/AI/Rag/CptEmbeddingIndexer.cs | Chunking and embedding service for CPT coding guidelines |
| CREATE | Server/AI/Schemas/CptCodingOutputSchema.cs | JSON schema definition and validation for structured LLM output |
| CREATE | Server/AI/Guardrails/CptPiiRedactionService.cs | PII regex scrubbing before external API calls |
| CREATE | Server/AI/Guardrails/CptCodingGuardrails.cs | Input sanitization, output validation, prompt injection detection |
| CREATE | Server/AI/Config/cpt-coding-guardrails.json | Configuration for token budgets, confidence thresholds, PII patterns |
| MODIFY | Server/AI/Gateway/AiGatewayService.cs | Add CptCoding method with token budget, provider routing, circuit breaker |

## External References

- [OpenAI JSON Mode](https://platform.openai.com/docs/guides/json-mode)
- [OpenAI text-embedding-3-small](https://platform.openai.com/docs/guides/embeddings)
- [pgvector — Cosine Similarity Search](https://github.com/pgvector/pgvector#querying)
- [Polly Circuit Breaker (.NET)](https://github.com/App-vNext/Polly/wiki/Circuit-Breaker)
- [CPT Coding Guidelines — AMA](https://www.ama-assn.org/practice-management/cpt)

## Build Commands

- Refer to applicable technology stack specific build commands in `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Prompt template generates valid CPT code suggestions for sample procedure descriptions
- [ ] RAG retrieval returns relevant CPT coding guideline chunks (cosine similarity ≥ 0.75)
- [ ] Structured output validation rejects malformed/hallucinated codes
- [ ] PII redaction removes patient identifiers from prompts
- [ ] Token budget stays within 2000 input / 500 output per request (AIR-O03)
- [ ] Confidence < 0.80 codes flagged for manual review (AIR-Q08)
- [ ] Bundled procedure detection identifies correct bundling opportunities
- [ ] Latency < 5 seconds per procedure coding request (AIR-Q05)

## Implementation Checklist

- [ ] Design versioned CPT coding prompt template (v1.0.0) with system instructions, clinical context injection points, output JSON schema, and ambiguity/bundling handling
- [ ] Set up pgvector index for CPT coding guidelines: 512-token chunks, 20% overlap, 384-dim embeddings via text-embedding-3-small (AIR-R01, AIR-R04)
- [ ] Implement RAG retrieval pipeline: top-5 chunks, cosine similarity ≥ 0.75, semantic re-ranking, hybrid search with PostgreSQL FTS (AIR-R02, AIR-R03, AIR-R06)
- [ ] Implement structured JSON output schema with code validation against CPT code library and hallucination rejection (AIR-Q06, AIR-S02)
- [ ] Enforce token budget of 2000 input / 500 output tokens per request with RAG chunk truncation when exceeding budget (AIR-O03)
- [ ] Implement confidence score calibration with <0.80 flagging for mandatory manual verification (AIR-Q07, AIR-Q08, AIR-010)
- [ ] Add bundled procedure detection in prompt with cross-reference against `cpt_bundle_rules` table and relevance ranking
- [ ] Implement PII redaction before API calls and audit logging of all prompts/responses with patient_id correlation (AIR-S01, AIR-S04, AIR-S06)
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-XXX requirements are met (quality, safety, operational)
