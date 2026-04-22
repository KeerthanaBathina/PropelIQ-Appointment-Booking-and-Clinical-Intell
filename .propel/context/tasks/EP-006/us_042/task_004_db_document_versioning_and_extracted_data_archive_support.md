# Task - task_004_db_document_versioning_and_extracted_data_archive_support

## Requirement Reference

- User Story: US_042
- Story Location: .propel/context/tasks/EP-006/us_042/us_042.md
- Acceptance Criteria:
    - AC-2: Given a staff member determines extraction quality is insufficient, When they click "Re-Upload," Then the system allows a new version upload that replaces the previous document and triggers re-processing.
    - AC-3: Given a document is re-uploaded, When parsing completes, Then previous extracted data is archived (not deleted) and new extraction results replace the active data.
- Edge Case:
    - EC-1: Old extracted data must remain queryable for audit or rollback purposes even after a replacement becomes active.
    - EC-2: The schema must support a reconsolidation-needed signal or durable flag after successful replacement activation without forcing EP-007 consolidation tables into this story.

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

Extend persistence to support document replacement history and extracted-data archival. This database task adds the version-linkage and active-version markers needed to represent replacement uploads on `ClinicalDocument`, and adds archive-state fields on `ExtractedData` so superseded results remain durable instead of being deleted. The schema must allow preview and review queries to target only active document versions by default while preserving the lineage needed for audit, rollback, and later reconsolidation triggers.

## Dependent Tasks

- US_038 task_003_db_clinical_document_metadata_and_upload_status_support (`ClinicalDocument` upload metadata baseline must exist)
- US_040 task_003_db_extracted_data_attribution_and_document_status_support (Extracted-data attribution fields must already exist)
- US_041 task_004_db_extracted_data_confidence_and_verification_support (Review status and verification metadata must already exist)

## Impacted Components

- **MODIFY** `ClinicalDocument` entity - Add version lineage, active-version markers, and replacement-status support (src/UPACIP.DataAccess/Entities/)
- **MODIFY** `ExtractedData` entity - Add archived-state fields that preserve superseded results without deleting them (src/UPACIP.DataAccess/Entities/)
- **MODIFY** document-related enums or value objects - Add replacement lifecycle values needed to distinguish active, superseded, and replacement-processing states (src/UPACIP.DataAccess/Entities/Enums/ or shared model path)
- **MODIFY** `ClinicalDocumentConfiguration` - Configure self-referencing version lineage and active-version indexes (src/UPACIP.DataAccess/Configurations/)
- **MODIFY** `ExtractedDataConfiguration` - Configure archive fields and indexes that default queries can use to exclude superseded rows efficiently (src/UPACIP.DataAccess/Configurations/)
- **MODIFY** `ApplicationDbContext` - Register updated document and extracted-data mappings (src/UPACIP.DataAccess/)
- **CREATE** EF Core migration - Add document-version and extracted-data archive support safely (src/UPACIP.DataAccess/Migrations/)

## Implementation Plan

1. **Add document version lineage fields** such as replacement-parent linkage, version number, and active-version markers so a replacement file can coexist with the original until activation succeeds.
2. **Add extracted-data archival fields** such as `IsArchived`, `ArchivedAtUtc`, and archival linkage to the superseding document version so old rows remain queryable but are excluded from active workflows by default.
3. **Support replacement lifecycle states on documents** so the application can distinguish active documents from superseded or replacement-processing versions.
4. **Add indexes for active-version queries** by patient, document group, processing status, and archive state so SCR-012 preview and review flows can retrieve the correct active records efficiently.
5. **Keep migration defaults backward-compatible** by treating existing rows as active current versions with non-archived extracted data.
6. **Provide a durable reconsolidation-needed persistence hook** such as a flag or timestamp on the active document version that EP-007 can consume later without introducing consolidation tables prematurely.

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
| MODIFY | src/UPACIP.DataAccess/Entities/ClinicalDocument.cs | Add version lineage, active-version, and reconsolidation-needed persistence fields |
| MODIFY | src/UPACIP.DataAccess/Entities/ExtractedData.cs | Add archived-state fields for superseded extracted rows |
| MODIFY | src/UPACIP.DataAccess/Entities/Enums/ProcessingStatus.cs | Add replacement-processing or superseded states needed for document version workflows |
| MODIFY | src/UPACIP.DataAccess/Configurations/ClinicalDocumentConfiguration.cs | Configure self-reference, defaults, and active-version indexes |
| MODIFY | src/UPACIP.DataAccess/Configurations/ExtractedDataConfiguration.cs | Configure archive fields and non-archived query indexes |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Register updated document-version and extracted-data archive mappings |
| CREATE | src/UPACIP.DataAccess/Migrations/AddClinicalDocumentVersioningAndExtractedDataArchiveSupport.cs | EF Core migration for replacement lineage and archived extracted-data support |

## External References

- EF Core one-to-many and self-referencing relationships: https://learn.microsoft.com/en-us/ef/core/modeling/relationships/one-to-many
- EF Core indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- PostgreSQL partial indexes: https://www.postgresql.org/docs/current/indexes-partial.html

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Add document version-lineage fields that allow replacements to coexist with the current active version during processing
- [x] Add extracted-data archive fields so superseded rows are retained instead of deleted
- [x] Support document lifecycle distinctions for active, superseded, and replacement-processing versions
- [x] Add indexes that make active-version and non-archived extracted-data queries efficient
- [x] Default existing rows to active current-version and non-archived states during migration
- [x] Persist a reconsolidation-needed hook without implementing EP-007 consolidation tables in this story