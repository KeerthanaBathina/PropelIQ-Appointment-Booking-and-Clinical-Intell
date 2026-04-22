# Task - task_003_db_extracted_data_attribution_and_document_status_support

## Requirement Reference

- User Story: US_040
- Story Location: .propel/context/tasks/EP-006/us_040/us_040.md
- Acceptance Criteria:
    - AC-5: Given any data point is extracted, When it is stored in `ExtractedData`, Then source document ID, page number, and extraction region are recorded for attribution.
    - AC-1: Given medications are extracted, When they are stored, Then the schema can retain the required medication fields.
    - AC-2: Given diagnoses are extracted, When they are stored, Then the schema can retain the required diagnosis fields.
    - AC-3: Given procedures are extracted, When they are stored, Then the schema can retain the required procedure fields.
    - AC-4: Given allergies are extracted, When they are stored, Then the schema can retain the required allergy fields.
- Edge Case:
    - EC-1: If no structured data is extracted, the document status must support `no-data-extracted` without overloading generic `failed` semantics.
    - EC-2: If the document language is unsupported, the status model must support `unsupported-language` and manual-review follow-up.

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
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
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

Extend the extraction persistence model so structured clinical data remains traceable to its source. This database task adds structured attribution fields to `ExtractedData`, preserves the flexible JSONB payload used for medications, diagnoses, procedures, and allergies, and expands `ClinicalDocument` outcome support for `no-data-extracted` and `unsupported-language`. The schema must remain compatible with existing parsed-document flows while preserving the source metadata required for later traceability features.

## Dependent Tasks

- US_008 task_001_be_domain_entity_models (`ExtractedData` and `ClinicalDocument` baseline entities must exist)
- US_008 task_002_be_efcore_configuration_migrations (`ExtractedData` JSONB and configuration baseline must exist)
- US_039 task_004_db_document_parsing_status_and_retry_support (Current parsing lifecycle support must exist)

## Impacted Components

- **MODIFY** `ExtractedData` entity - Add page number, extraction-region metadata, and structured source-attribution support beyond the existing free-text field (src/UPACIP.DataAccess/Entities/)
- **MODIFY** `ClinicalDocument` entity - Add `no-data-extracted` and `unsupported-language` outcome support plus manual-review metadata if not already present (src/UPACIP.DataAccess/Entities/)
- **MODIFY** `ExtractedDataConfiguration` - Configure attribution columns or JSON fields and indexes for document plus data-type retrieval (src/UPACIP.DataAccess/Configurations/)
- **MODIFY** `ClinicalDocumentConfiguration` - Configure new outcome-status values and review-related query indexes (src/UPACIP.DataAccess/Configurations/)
- **MODIFY** `ApplicationDbContext` - Register updated extraction and document mappings (src/UPACIP.DataAccess/)
- **CREATE** EF Core migration - Add extraction-attribution fields and document-outcome support safely (src/UPACIP.DataAccess/Migrations/)

## Implementation Plan

1. **Extend `ExtractedData` with structured attribution fields** for page number and extraction region while retaining document linkage through `DocumentId`.
2. **Preserve category-specific payload flexibility** by continuing to use JSONB-backed `DataContent` for medications, diagnoses, procedures, and allergies instead of splitting every field into separate tables during Phase 1.
3. **Expand `ClinicalDocument` outcome support** with `no-data-extracted` and `unsupported-language` so those cases do not masquerade as parser failures.
4. **Add indexes supporting review and profile assembly** across document ID, data type, and created-at fields so extracted rows can be grouped efficiently by source document and category.
5. **Keep migration rollout backward-compatible** by defaulting new attribution fields safely for existing extraction rows and preserving current parsed-document records.
6. **Store attribution in a structured way** so later traceability features can consume it without another schema redesign.

## Current Project State

```text
src/
  UPACIP.DataAccess/
    Entities/
      ClinicalDocument.cs
      ExtractedData.cs
    Configurations/
      ClinicalDocumentConfiguration.cs
      ExtractedDataConfiguration.cs
    ApplicationDbContext.cs
    Migrations/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.DataAccess/Entities/ExtractedData.cs | Add structured attribution metadata for page number and extraction region |
| MODIFY | src/UPACIP.DataAccess/Entities/ClinicalDocument.cs | Add `no-data-extracted` and `unsupported-language` outcome support |
| MODIFY | src/UPACIP.DataAccess/Configurations/ExtractedDataConfiguration.cs | Configure attribution fields and retrieval indexes |
| MODIFY | src/UPACIP.DataAccess/Configurations/ClinicalDocumentConfiguration.cs | Configure new extraction-outcome statuses and related indexes |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Register updated extraction and document mappings |
| CREATE | src/UPACIP.DataAccess/Migrations/<timestamp>_AddExtractedDataAttributionAndDocumentOutcomes.cs | Migration for attribution fields and new document outcomes |

## External References

- EF Core JSON columns: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-7.0/whatsnew#json-columns
- EF Core indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- PostgreSQL JSONB documentation: https://www.postgresql.org/docs/current/datatype-json.html

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Add page-number and extraction-region attribution support to `ExtractedData`
- [x] Keep medications, diagnoses, procedures, and allergies storable in JSONB-backed `DataContent`
- [x] Add `no-data-extracted` and `unsupported-language` support to `ClinicalDocument`
- [x] Add indexes supporting grouping and retrieval by document and data type
- [x] Keep migrations backward-compatible for existing `ExtractedData` and `ClinicalDocument` rows
- [x] Preserve schema compatibility with later traceability and verification workflows