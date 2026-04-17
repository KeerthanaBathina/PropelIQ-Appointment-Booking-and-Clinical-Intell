# Task - task_004_ai_conflict_prompts

## Requirement Reference

- User Story: US_044
- Story Location: .propel/context/tasks/EP-007/us_044/us_044.md
- Acceptance Criteria:
  - AC-1: Given multiple documents are consolidated for a patient, When consolidation runs, Then the AI identifies medication discrepancies (different dosages, conflicting prescriptions) and flags them.
  - AC-2: Given duplicate diagnoses with different dates are found, When the conflict is detected, Then the system flags the conflict with source citations from both documents.
  - AC-4: Given AI confidence for conflict detection drops below 80%, When the result is generated, Then the system automatically flags the entire review for manual verification.
  - AC-5: Given clinical event dates fail chronological plausibility validation, When the inconsistency is detected, Then the system flags the date conflict with a clear explanation.
- Edge Case:
  - 3+ document conflicts: System shows all conflicting sources in the comparison view, not just pairs.

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
| AI Gateway | Custom .NET Service with Polly | 8.x |
| Vector Store | PostgreSQL pgvector | 0.5.x |
| Embedding Model | OpenAI text-embedding-3-small | 2024 |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-007, AIR-009, AIR-010, AIR-S09, AIR-S10, AIR-O01, AIR-O04, AIR-O08, AIR-Q07 |
| **AI Pattern** | Hybrid (RAG + Tool Calling) |
| **Prompt Template Path** | prompts/conflict-detection/ |
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

Enhance the AI conflict detection service (IConflictDetectionService from US_043/task_004) with specialized prompt templates for each conflict type: medication discrepancy detection, duplicate diagnosis identification, chronological date plausibility validation, and multi-source conflict analysis. Each prompt template extracts source citations from document sections (AIR-007), returns calibrated confidence scores (AIR-Q07), and supports the low-confidence fallback to manual verification (AIR-010). This task focuses on prompt engineering, output schema refinement, and conflict-type-specific AI behavior.

## Dependent Tasks

- US_043/task_004_ai_conflict_detection - Requires base IConflictDetectionService, AI Gateway integration, and foundational prompt template
- EP-012 (AI Gateway) - Requires AI Gateway with Polly circuit breaker
- EP-014 (RAG Pipeline) - Requires pgvector medical terminology index

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | Prompt template: `medication-discrepancy.liquid` | Server (Prompts) |
| NEW | Prompt template: `duplicate-diagnosis.liquid` | Server (Prompts) |
| NEW | Prompt template: `date-plausibility.liquid` | Server (Prompts) |
| NEW | Prompt template: `multi-source-conflict.liquid` | Server (Prompts) |
| MODIFY | `ConflictDetectionService` - Add conflict-type-specific detection methods | Server (AI Layer) |
| NEW | Output schema: `ConflictDetectionOutputSchema.cs` | Server (Models) |
| MODIFY | Guardrails config: `conflict-detection.json` - Add per-type validation rules | Server (Config) |

## Implementation Plan

1. Create `medication-discrepancy.liquid` prompt template instructing the LLM to:
   - Compare medication entries across documents (drug name, dosage, frequency, prescribing physician)
   - Identify dosage differences for the same medication (AC-1)
   - Detect conflicting prescriptions (same condition, different drugs)
   - Flag medication contraindications (drug-drug interactions) with Critical severity (AIR-S09)
   - Return source_document_id and extraction_section for each conflicting entry (AIR-007)
2. Create `duplicate-diagnosis.liquid` prompt template instructing the LLM to:
   - Match diagnoses by ICD-10 code or description across documents
   - Identify duplicate diagnoses with different dates (AC-2)
   - Provide source citations from both/all documents (AIR-007)
   - Return structured JSON with both source references and confidence score
3. Create `date-plausibility.liquid` prompt template instructing the LLM to:
   - Validate clinical event dates are chronologically plausible (AC-5)
   - Detect future-dated events, impossible sequences (discharge before admission), unrealistic gaps
   - Provide clear human-readable explanation of the date inconsistency
   - Reference the specific document section containing the implausible date
4. Create `multi-source-conflict.liquid` prompt template for 3+ document analysis:
   - Accept data points from N documents (not just pairs) (Edge Case)
   - Group conflicting data by entity (medication, diagnosis) across all sources
   - Return all source document references per conflict, not just the first two found
5. Implement conflict-type routing in `ConflictDetectionService`: based on data_type of incoming data, select the appropriate prompt template (medication → medication-discrepancy, diagnosis → duplicate-diagnosis, etc.)
6. Implement confidence score calibration: normalize LLM-returned confidence to 0-1 range, flag conflicts with score <0.80 for manual verification (AC-4, AIR-010, AIR-Q07)
7. Update guardrails config (`conflict-detection.json`) with per-type output schema validation rules: required fields, allowed severity values, source citation format

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  AI/
    Services/
      Interfaces/
        IConflictDetectionService.cs
      ConflictDetectionService.cs
    Gateway/
      AIGatewayService.cs
    RAG/
      RagRetrievalService.cs
    PII/
      PiiRedactor.cs
  Prompts/
    consolidation/
      conflict-detection.liquid
  Config/
    ai-guardrails/
      conflict-detection.json
  Models/
    AI/
      ConflictAnalysisResult.cs
      DetectedConflict.cs
      ConflictSeverity.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Prompts/conflict-detection/medication-discrepancy.liquid | Prompt for medication dosage/prescription conflicts with source citations |
| CREATE | Server/Prompts/conflict-detection/duplicate-diagnosis.liquid | Prompt for duplicate diagnosis detection across documents with date comparison |
| CREATE | Server/Prompts/conflict-detection/date-plausibility.liquid | Prompt for chronological validation with human-readable explanations |
| CREATE | Server/Prompts/conflict-detection/multi-source-conflict.liquid | Prompt for N-document conflict analysis (3+ sources) |
| MODIFY | Server/AI/Services/ConflictDetectionService.cs | Add conflict-type routing to select appropriate prompt template; add confidence calibration |
| CREATE | Server/Models/AI/ConflictDetectionOutputSchema.cs | Strongly-typed output schema with source citation fields per conflict type |
| MODIFY | Server/Config/ai-guardrails/conflict-detection.json | Add per-type output validation rules (required fields, severity values, citation format) |

## External References

- [OpenAI GPT-4o-mini JSON Mode](https://platform.openai.com/docs/guides/structured-outputs) - Structured JSON output for reliable parsing
- [Liquid Template Language](https://shopify.github.io/liquid/) - Variable substitution and control flow in prompt templates
- [Drug Interaction Databases](https://www.drugs.com/drug_interactions.html) - Reference for medication contraindication patterns
- [ICD-10 Code Structure](https://www.cms.gov/medicare/coding-billing/icd-10-codes) - Diagnosis code matching logic

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
- [ ] Medication discrepancy prompt correctly identifies dosage differences and returns source citations
- [ ] Date plausibility prompt flags future-dated events with clear explanation

## Implementation Checklist

- [ ] Create `medication-discrepancy.liquid` prompt template with drug comparison instructions, contraindication detection, and source citation extraction (AC-1, AIR-S09, AIR-007)
- [ ] Create `duplicate-diagnosis.liquid` prompt template with ICD-10 code matching, date comparison, and dual/multi-source citation output (AC-2, AIR-007)
- [ ] Create `date-plausibility.liquid` prompt template with chronological validation rules, implausible sequence detection, and human-readable explanations (AC-5, AIR-S10)
- [ ] Create `multi-source-conflict.liquid` prompt template for N-document conflict analysis grouping conflicts across all sources (Edge Case)
- [ ] Implement conflict-type routing in ConflictDetectionService selecting the appropriate prompt template based on data_type
- [ ] Implement confidence score calibration normalizing LLM output to 0-1 range with <0.80 flagging for manual verification (AC-4, AIR-010, AIR-Q07)
- [ ] Update guardrails config with per-type output schema validation rules and required source citation format
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-XXX requirements are met (AIR-007, AIR-009, AIR-010, AIR-S09, AIR-S10)
