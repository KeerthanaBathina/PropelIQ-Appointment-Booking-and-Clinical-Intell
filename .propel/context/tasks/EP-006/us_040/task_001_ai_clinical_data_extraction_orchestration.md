# Task - task_001_ai_clinical_data_extraction_orchestration

## Requirement Reference

- User Story: US_040
- Story Location: .propel/context/tasks/EP-006/us_040/us_040.md
- Acceptance Criteria:
    - AC-1: Given a clinical document is parsed, When AI extraction completes, Then the system extracts medication records including drug name, dosage, frequency, and prescribing physician.
    - AC-2: Given a clinical document is parsed, When AI extraction completes, Then the system extracts diagnosis information with associated dates and treating providers.
    - AC-3: Given a clinical document is parsed, When AI extraction completes, Then the system extracts procedure history with dates, descriptions, and performing physicians.
    - AC-4: Given a clinical document is parsed, When AI extraction completes, Then the system extracts allergy information including allergen, reaction type, and severity.
    - AC-5: Given any data point is extracted, When it is stored in `ExtractedData`, Then the source document ID, page number, and extraction region are recorded for attribution.
- Edge Case:
    - EC-1: If the AI cannot identify any supported structured data, return a `no-data-extracted` outcome that flags the document for manual review.
    - EC-2: If the document language is not English, return an `unsupported-language` outcome for Phase 1 rather than attempting unreliable extraction.

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
| Caching | Upstash Redis | 7.x |
| Logging | Serilog | 8.x |
| Language | C# | 12 / .NET 8 |
| Mobile | N/A | N/A |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-002, AIR-S01, AIR-O01, AIR-O08 |
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

Implement the AI extraction orchestration that turns parsed clinical-document content into structured candidate data for medications, diagnoses, procedures, and allergies. This task builds document-extraction prompts, detects unsupported-language documents, distinguishes genuine no-data cases from parser failure, validates the returned structured payload, and produces extraction results with source-attribution metadata such as page number and extraction region. The orchestration must remain focused on extraction quality and traceability, leaving persistence and downstream review workflows to adjacent tasks.

## Dependent Tasks

- US_039 task_003_ai_document_parser_worker_and_retry_orchestration (Parsed-document worker flow and AI Gateway integration must exist)
- US_067 - Cross-Epic (EP-012) - AI Gateway abstraction and provider routing must exist
- US_068 - Cross-Epic (EP-012) - Document parsing token-budget enforcement baseline must exist
- task_003_db_extracted_data_attribution_and_document_status_support (Attribution and status persistence support must exist)

## Impacted Components

- **NEW** `ClinicalExtractionService` - Orchestrates structured extraction for medications, diagnoses, procedures, and allergies from parsed document content (Server/AI/ClinicalExtraction/)
- **NEW** `ClinicalExtractionPromptBuilder` - Builds prompts for category-specific clinical data extraction with attribution requirements (Server/AI/ClinicalExtraction/)
- **NEW** `ClinicalExtractionResultValidator` - Validates structured extraction payloads, language constraints, and no-data outcomes (Server/AI/ClinicalExtraction/)
- **NEW** `ClinicalExtractionLanguageGate` - Detects unsupported-language inputs before external model invocation (Server/AI/ClinicalExtraction/)
- **NEW** prompt templates and guardrails config - Versioned extraction prompt artifacts and AI safety rules (Server/AI/ClinicalExtraction/Prompts/)
- **MODIFY** `DocumentParsingWorker` - Invoke clinical extraction after parsed content is available and hand off a structured extraction result envelope (Server/AI/DocumentParsing/)
- **MODIFY** `AiGatewayService` - Accept clinical-extraction prompt paths and response-shape normalization needed by the worker (Server/AI/)

## Implementation Plan

1. **Create a clinical extraction orchestration service** that receives parsed document text or OCR output and invokes the AI Gateway with extraction-specific prompts rather than generic parsing prompts.
2. **Add versioned prompt templates for each extraction concern** so medications, diagnoses, procedures, and allergies can be requested in a single structured result while still enforcing field-level requirements for each category.
3. **Detect unsupported-language inputs before extraction** using a lightweight language gate and return the Phase 1 `unsupported-language` outcome without sending unreliable prompts to external models.
4. **Distinguish no-data outcomes from extraction failures** by allowing the model and validator to return an explicit `no-data-extracted` result when no supported clinical entities are present.
5. **Require attribution metadata in the AI output contract** so each extracted item carries page number and extraction-region context suitable for downstream persistence and traceability.
6. **Apply AI guardrails and provider resilience** by redacting PII where required, enforcing the token budget, validating the structured response shape, and using the AI Gateway fallback path when provider errors occur.
7. **Return a normalized extraction envelope** that downstream persistence logic can store without needing to understand raw model output formats.

## Current Project State

```text
Server/
  AI/
    DocumentParsing/
      DocumentParsingWorker.cs
      DocumentParsingPromptBuilder.cs
  Services/
  Models/
    Entities/
      ClinicalDocument.cs
      ExtractedData.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/AI/ClinicalExtraction/ClinicalExtractionService.cs | Orchestrates structured extraction of medications, diagnoses, procedures, and allergies |
| CREATE | Server/AI/ClinicalExtraction/ClinicalExtractionPromptBuilder.cs | Builds extraction prompts with required attribution fields |
| CREATE | Server/AI/ClinicalExtraction/ClinicalExtractionResultValidator.cs | Validates output schema, no-data outcomes, and unsupported-language handling |
| CREATE | Server/AI/ClinicalExtraction/ClinicalExtractionLanguageGate.cs | Detects unsupported-language documents before extraction |
| CREATE | Server/AI/ClinicalExtraction/Prompts/clinical-extraction-system.liquid | System prompt template for clinical data extraction |
| CREATE | Server/AI/ClinicalExtraction/Prompts/clinical-extraction-schema.liquid | Output-shape prompt template covering all four clinical data types |
| CREATE | Server/AI/ClinicalExtraction/guardrails.json | PII redaction, token budget, language gating, and fallback thresholds for extraction |
| MODIFY | Server/AI/DocumentParsing/DocumentParsingWorker.cs | Invoke clinical extraction after parsing completes and pass the normalized extraction result onward |
| MODIFY | Server/AI/AiGatewayService.cs | Route clinical extraction requests with provider fallback and response normalization |

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

- [x] Extract medication fields including drug name, dosage, frequency, and prescribing physician
- [x] Extract diagnosis fields including associated dates and treating providers
- [x] Extract procedure fields including dates, descriptions, and performing physicians
- [x] Extract allergy fields including allergen, reaction type, and severity
- [x] Return page number and extraction-region attribution for every extracted data point
- [x] Return explicit `no-data-extracted` and `unsupported-language` outcomes when applicable
- [x] Enforce AI guardrails, token budget, and provider fallback without exposing PHI in logs
- [x] **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- [x] **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- [x] **[AI Tasks - MANDATORY]** Verify AIR-XXX requirements are met (quality, safety, operational)