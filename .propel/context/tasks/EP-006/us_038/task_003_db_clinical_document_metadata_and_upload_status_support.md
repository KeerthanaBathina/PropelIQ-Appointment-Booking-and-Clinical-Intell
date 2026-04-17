# Task - task_003_db_clinical_document_metadata_and_upload_status_support

## Requirement Reference

- User Story: US_038
- Story Location: .propel/context/tasks/EP-006/us_038/us_038.md
- Acceptance Criteria:
    - AC-2: Given the document passes validation, When upload completes, Then the file is stored securely and a `ClinicalDocument` record is created with status `uploaded`.
    - AC-3: Given a category is assigned, When upload completes, Then the category is saved to the `ClinicalDocument` record.
    - AC-4: Given upload succeeds, When the operation completes, Then filename, upload timestamp, and uploading user attribution are recorded.
    - AC-5: Given invalid uploads are rejected, When persistence is inspected, Then partial upload records are not created.
- Edge Case:
    - EC-1: Upload interruptions must not leave incomplete database rows or storage metadata that looks persisted.
    - EC-2: The schema must support later AI parsing workflows without forcing US_038 to mark documents `queued` prematurely.

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
| Security | AES | 256-bit |
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

Extend `ClinicalDocument` persistence to represent upload-stage metadata explicitly. This database task adds the fields and enum support required to store original filename, MIME type, file size, secure storage reference, uploader attribution, upload timestamp, and the `uploaded` lifecycle state expected by US_038. The schema must remain compatible with later EP-006 stories that enqueue parsing jobs and attach extraction artifacts, while ensuring rejected or interrupted uploads do not create misleading rows.

## Dependent Tasks

- US_008 task_001_be_domain_entity_models (ClinicalDocument baseline entity must exist)
- US_008 task_002_be_efcore_configuration_migrations (ClinicalDocument configuration and migration baseline must exist)
- EP-DATA us_010 task_001_be_constraint_migration (ClinicalDocument FK behavior and uploader-user restrictions must already exist)

## Impacted Components

- **MODIFY** `ClinicalDocument` entity - Add original filename, content type, file size, encrypted storage reference, and `uploaded` status support (src/UPACIP.DataAccess/Entities/)
- **MODIFY** `ClinicalDocumentConfiguration` - Configure column lengths, required upload metadata, and indexes for patient plus upload status queries (src/UPACIP.DataAccess/Configurations/)
- **MODIFY** `ApplicationDbContext` - Register updated model configuration if needed (src/UPACIP.DataAccess/)
- **CREATE** EF Core migration - Add upload metadata columns and status mapping changes safely (src/UPACIP.DataAccess/Migrations/)

## Implementation Plan

1. **Extend `ClinicalDocument` with upload metadata fields** for original filename, content type, file size bytes, secure storage path or blob key, and optional encryption metadata needed by the storage service.
2. **Add or confirm `uploaded` in the processing-status model** so US_038 can persist post-upload state without skipping ahead to the `queued` state reserved for US_039.
3. **Preserve attribution data explicitly** by treating upload timestamp and uploader user as first-class persisted properties tied to the document row.
4. **Strengthen configuration for upload retrieval** with indexes on patient plus processing status and any timestamp fields used for recent document lists.
5. **Keep schema evolution backward-compatible** by making new columns nullable only where necessary for migration safety and backfilling defaults for existing rows where practical.
6. **Avoid partial-record semantics** by keeping fields required for a valid uploaded document non-null once the migration is fully applied and the backend workflow writes them transactionally.

## Current Project State

```text
src/
  UPACIP.DataAccess/
    Entities/
      ClinicalDocument.cs
    Configurations/
      ClinicalDocumentConfiguration.cs
    ApplicationDbContext.cs
    Migrations/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.DataAccess/Entities/ClinicalDocument.cs | Add upload metadata properties and `uploaded` status support |
| MODIFY | src/UPACIP.DataAccess/Configurations/ClinicalDocumentConfiguration.cs | Configure upload metadata columns, constraints, and indexes |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Register updated ClinicalDocument mapping and configuration |
| CREATE | src/UPACIP.DataAccess/Migrations/<timestamp>_AddClinicalDocumentUploadMetadata.cs | Migration for upload metadata and status updates |

## External References

- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/
- EF Core indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- PostgreSQL binary data and large object considerations: https://www.postgresql.org/docs/current/datatype-binary.html

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Add persisted metadata for original filename, MIME type, file size, secure storage reference, upload timestamp, and uploader attribution
- [ ] Ensure `ClinicalDocument` can persist the `uploaded` status required by US_038 before later queueing workflows run
- [ ] Keep patient-document and uploader relationships intact with existing FK behavior
- [ ] Add indexes that support recent uploaded-document retrieval by patient and status
- [ ] Keep migration rollout backward-compatible for existing ClinicalDocument rows
- [ ] Support transactional upload writes so interrupted transfers do not create partial persisted records