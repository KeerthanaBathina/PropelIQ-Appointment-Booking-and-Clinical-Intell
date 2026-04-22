# Task - task_003_ai_confidence_score_assignment_orchestration

## Requirement Reference

- User Story: US_041
- Story Location: .propel/context/tasks/EP-006/us_041/us_041.md
- Acceptance Criteria:
    - AC-1: Given AI extraction produces data points, When each data point is stored, Then a confidence score (0.00-1.00) is calculated and saved with the ExtractedData record.
    - AC-2: Given an extracted data point has confidence below 0.80, When the results are displayed, Then the data point is visually flagged with an amber/red indicator and marked for mandatory manual verification.
- Edge Case:
    - EC-1: If the AI cannot assign a confidence score, default the item to `0.00`, label the review reason as `confidence-unavailable`, and require manual verification downstream.
    - EC-2: If a single extraction response contains mixed confidence levels, return per-item scores and review flags so later bulk verification can operate on only the affected rows.

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
| Database | PostgreSQL | 16.x |
| Logging | Serilog | 8.x |
| Language | C# | 12 / .NET 8 |
| Mobile | N/A | N/A |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-010, AIR-Q07, AIR-Q08, AIR-O01 |
| **AI Pattern** | Tool Calling |
| **Prompt Template Path** | Server/AI/ClinicalExtraction/Prompts/ |
| **Guardrails Config** | Server/AI/ClinicalExtraction/guardrails.json |
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

Extend the clinical extraction AI orchestration so every extracted candidate includes a normalized confidence score and review metadata before persistence. This task updates prompts, response validation, and extraction-result shaping so the model returns per-item confidence in the `0.00-1.00` range, the application flags any item below `0.80` for mandatory review, and missing confidence values are downgraded to a deterministic `confidence-unavailable` state with score `0.00`. The output contract must remain precise enough for downstream persistence, verification APIs, and SCR-012 or SCR-013 review surfaces without requiring those layers to infer AI confidence behavior themselves.

## Dependent Tasks

- US_040 task_001_ai_clinical_data_extraction_orchestration (Clinical extraction prompts and worker integration must exist)
- US_039 task_003_ai_document_parser_worker_and_retry_orchestration (Worker lifecycle and AI Gateway fallback path must exist)
- US_067 - Cross-Epic (EP-012) - AI Gateway abstraction and provider routing must exist
- US_068 - Cross-Epic (EP-012) - Token-budget enforcement baseline must exist
- task_004_db_extracted_data_confidence_and_verification_support (Persistence target for review metadata must exist)

## Impacted Components

- **MODIFY** `ClinicalExtractionService` - Request and return per-item confidence plus mandatory-review metadata in the normalized extraction envelope (Server/AI/ClinicalExtraction/)
- **MODIFY** `ClinicalExtractionPromptBuilder` - Instruct the model to provide confidence scores and explicit unavailable markers for every extracted item (Server/AI/ClinicalExtraction/)
- **MODIFY** `ClinicalExtractionResultValidator` - Enforce `0.00-1.00` confidence bounds, default missing values, and classify review reasons deterministically (Server/AI/ClinicalExtraction/)
- **MODIFY** prompt templates and guardrails config - Update extraction output schema and validation rules for per-item confidence data (Server/AI/ClinicalExtraction/Prompts/)
- **MODIFY** `DocumentParsingWorker` - Pass the richer extraction envelope into persistence without dropping review metadata (Server/AI/DocumentParsing/)
- **MODIFY** extraction result DTOs - Carry confidence score, review-required flag, and review reason for each extracted item (Server/Models/DTOs/ or Server/AI/ClinicalExtraction/)

## Implementation Plan

1. **Extend the extraction prompt contract** so every medication, diagnosis, procedure, and allergy item must include a confidence value and a reason when the model cannot supply one confidently.
2. **Validate confidence values strictly** by clamping or rejecting out-of-range responses, normalizing them to `0.00-1.00`, and preventing malformed model output from reaching persistence unchanged.
3. **Classify review requirements deterministically** so scores below `0.80` set `FlaggedForReview = true`, while missing or omitted scores become `confidence-unavailable` with score `0.00`.
4. **Emit per-item review metadata in the normalized extraction envelope** including the numeric score, whether review is mandatory, and the reason code needed by downstream APIs and UI.
5. **Preserve provider resilience and token discipline** by keeping the existing AI Gateway fallback path, token budget checks, and guardrails active after the confidence fields are introduced.
6. **Audit prompts and responses safely** by logging confidence-related failures and provider fallbacks without writing raw PHI-rich document content to standard logs.
7. **Keep scoring scope limited to extraction output quality** and avoid mixing manual verification or profile-consolidation behavior into the AI layer.

## Current Project State

```text
Server/
  AI/
    ClinicalExtraction/
      ClinicalExtractionService.cs
      ClinicalExtractionPromptBuilder.cs
      ClinicalExtractionResultValidator.cs
      Prompts/
        clinical-extraction-system.liquid
        clinical-extraction-schema.liquid
      guardrails.json
    DocumentParsing/
      DocumentParsingWorker.cs
  Models/
    DTOs/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/AI/ClinicalExtraction/ClinicalExtractionService.cs | Return per-item confidence and review metadata in the extraction envelope |
| MODIFY | Server/AI/ClinicalExtraction/ClinicalExtractionPromptBuilder.cs | Require confidence score and unavailable-reason output for each extracted item |
| MODIFY | Server/AI/ClinicalExtraction/ClinicalExtractionResultValidator.cs | Validate score range, default missing values, and set review flags |
| MODIFY | Server/AI/ClinicalExtraction/Prompts/clinical-extraction-system.liquid | Instruct the model on confidence semantics and mandatory output behavior |
| MODIFY | Server/AI/ClinicalExtraction/Prompts/clinical-extraction-schema.liquid | Add per-item confidence and review-reason fields to the structured schema |
| MODIFY | Server/AI/ClinicalExtraction/guardrails.json | Validate confidence output presence, bounds, and fallback handling |
| MODIFY | Server/AI/DocumentParsing/DocumentParsingWorker.cs | Preserve confidence metadata when handing extraction results to persistence |
| MODIFY | Server/Models/DTOs/ClinicalExtractionOutcome.cs | Carry confidence-review summary fields needed by downstream consumers |

## External References

- OpenAI structured outputs guide: https://platform.openai.com/docs/guides/structured-outputs
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

- [x] Require a confidence score or explicit unavailable reason for every extracted item in the AI output contract
- [x] Normalize and validate all confidence values to the `0.00-1.00` range before persistence
- [x] Flag every item below `0.80` for mandatory review in the normalized extraction envelope
- [x] Default missing confidence values to `0.00` with `confidence-unavailable` review reason
- [x] Preserve AI fallback, token-budget, and guardrail behavior after adding confidence metadata
- [x] Keep prompts and logs free of unnecessary PHI exposure while still auditing scoring failures
- [x] Emit downstream-ready review metadata without mixing manual verification logic into the AI layer
- [x] **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- [x] **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- [x] **[AI Tasks - MANDATORY]** Verify AIR-010, AIR-Q07, AIR-Q08, and AIR-O01 requirements are met