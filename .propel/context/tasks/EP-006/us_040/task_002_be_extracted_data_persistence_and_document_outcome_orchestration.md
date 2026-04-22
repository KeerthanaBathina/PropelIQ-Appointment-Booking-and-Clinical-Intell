# Task - task_002_be_extracted_data_persistence_and_document_outcome_orchestration

## Requirement Reference

- User Story: US_040
- Story Location: .propel/context/tasks/EP-006/us_040/us_040.md
- Acceptance Criteria:
    - AC-1: Given a clinical document is parsed, When AI extraction completes, Then medication records are stored with the required fields.
    - AC-2: Given a clinical document is parsed, When AI extraction completes, Then diagnosis records are stored with dates and treating providers.
    - AC-3: Given a clinical document is parsed, When AI extraction completes, Then procedure history is stored with dates, descriptions, and performing physicians.
    - AC-4: Given a clinical document is parsed, When AI extraction completes, Then allergy information is stored with allergen, reaction type, and severity.
    - AC-5: Given any data point is extracted, When it is stored in `ExtractedData`, Then source document ID, page number, and extraction region are recorded.
- Edge Case:
    - EC-1: If no structured data is found, mark the document `no-data-extracted` and flag it for manual review without inserting empty extraction rows.
    - EC-2: If the document language is unsupported, mark it `unsupported-language`, persist the reason, and skip normal extraction-row creation.

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
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Logging | Serilog | 8.x |
| API Framework | ASP.NET Core MVC | 8.x |
| Language | C# | 12 / .NET 8 |
| Mobile | N/A | N/A |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the deterministic application layer that converts normalized AI extraction results into persisted `ExtractedData` rows and final document outcomes. This task maps extraction categories into the existing data model, writes source attribution with document, page, and region details, avoids partial persistence when the extraction envelope is empty or unsupported, and updates the document outcome so later review workflows can distinguish successful extraction from `no-data-extracted` or `unsupported-language` cases.

## Dependent Tasks

- task_001_ai_clinical_data_extraction_orchestration (Normalized extraction envelope and edge-case outcomes must exist)
- US_039 task_003_ai_document_parser_worker_and_retry_orchestration (Parsing worker lifecycle and AI execution contract must exist)
- task_003_db_extracted_data_attribution_and_document_status_support (Attribution fields and outcome statuses must exist)

## Impacted Components

- **NEW** `IExtractedDataPersistenceService` / `ExtractedDataPersistenceService` - Persists structured extraction results and updates document outcomes transactionally (Server/Services/Documents/)
- **NEW** `ExtractedDataMapper` - Maps normalized extraction payloads into `ExtractedData` entity instances for medication, diagnosis, procedure, and allergy rows (Server/Services/Documents/)
- **NEW** `ClinicalExtractionOutcome` - Backend contract describing persisted result counts, outcome status, and manual-review flags (Server/Models/DTOs/)
- **MODIFY** `DocumentParsingWorker` - Call deterministic persistence after AI extraction succeeds and before final document completion is committed (Server/AI/DocumentParsing/)
- **MODIFY** document-status event path - Emit extraction-complete or manual-review-required outcomes for later SCR-012 or SCR-013 consumption (Server/Services/Documents/)

## Implementation Plan

1. **Create a persistence service for extraction results** that accepts the normalized AI extraction envelope and writes all `ExtractedData` rows within a single transaction.
2. **Map each supported data type deterministically** so medication, diagnosis, procedure, and allergy records land in the correct `DataType` category with the expected field shape inside `DataContent`.
3. **Persist source attribution structurally** by writing document identifier linkage plus page number and extraction-region metadata for each extracted row rather than flattening everything into a single free-text attribution string.
4. **Handle no-data and unsupported-language outcomes explicitly** by skipping row creation, setting the document outcome accordingly, and flagging for manual review where required.
5. **Keep successful completion atomic** so document outcome changes and extracted rows succeed or fail together without leaving partial extraction sets.
6. **Emit a normalized backend outcome** containing counts by extraction type and whether manual review is required, so later UI or profile-refresh work can consume it without re-reading raw rows.

## Current Project State

```text
Server/
  AI/
    ClinicalExtraction/
      ClinicalExtractionService.cs
  Services/
    Documents/
  Models/
    Entities/
      ClinicalDocument.cs
      ExtractedData.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Documents/IExtractedDataPersistenceService.cs | Interface for transactional persistence of normalized extraction results |
| CREATE | Server/Services/Documents/ExtractedDataPersistenceService.cs | Persist extracted rows and update document outcome statuses atomically |
| CREATE | Server/Services/Documents/ExtractedDataMapper.cs | Map extraction payloads into `ExtractedData` entities and attribution fields |
| CREATE | Server/Models/DTOs/ClinicalExtractionOutcome.cs | Result contract describing persisted counts and manual-review outcome |
| MODIFY | Server/AI/DocumentParsing/DocumentParsingWorker.cs | Invoke deterministic persistence before final extraction completion outcome is emitted |
| MODIFY | Server/Program.cs | Register extraction persistence and mapping services |

## External References

- EF Core transactions: https://learn.microsoft.com/en-us/ef/core/saving/transactions
- EF Core saving related data: https://learn.microsoft.com/en-us/ef/core/saving/related-data
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Persist medication, diagnosis, procedure, and allergy extractions as `ExtractedData` rows with correct type mapping
- [x] Store source document, page number, and extraction-region attribution for every extracted data point
- [x] Skip empty extraction-row creation for `no-data-extracted` outcomes
- [x] Mark unsupported-language documents without attempting normal extracted-data persistence
- [x] Update document outcome and extracted rows transactionally so partial extraction sets are not committed
- [x] Return normalized persisted-result counts and manual-review status for downstream workflows