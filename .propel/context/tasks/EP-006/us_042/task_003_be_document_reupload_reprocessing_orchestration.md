# Task - task_003_be_document_reupload_reprocessing_orchestration

## Requirement Reference

- User Story: US_042
- Story Location: .propel/context/tasks/EP-006/us_042/us_042.md
- Acceptance Criteria:
    - AC-2: Given a staff member determines extraction quality is insufficient, When they click "Re-Upload," Then the system allows a new version upload that replaces the previous document and triggers re-processing.
    - AC-3: Given a document is re-uploaded, When parsing completes, Then previous extracted data is archived (not deleted) and new extraction results replace the active data.
- Edge Case:
    - EC-1: If replacement parsing or extraction fails, keep the previous document version and extracted data active instead of archiving them prematurely.
    - EC-2: After a successful replacement becomes active, emit a reconsolidation-needed signal for the patient profile without implementing the full EP-007 consolidation flow here.

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
| API Framework | ASP.NET Core MVC | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Logging | Serilog | 8.x |
| Resilience | Polly | 8.x |
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

Implement the replacement-upload orchestration for clinical documents. This backend task accepts a new document version for an existing document, stores and enqueues it through the existing parsing pipeline, keeps the prior version active while the replacement is processing, and only flips active status after the new version parses and extraction persistence succeeds. When the replacement succeeds, the workflow archives the old extracted rows rather than deleting them and emits a reconsolidation-needed signal that EP-007 can consume later. The orchestration must remain deterministic and transactional around version activation, without trying to implement the full consolidation pipeline itself.

## Dependent Tasks

- US_038 task_002_be_secure_clinical_document_upload_api (Secure upload and encrypted storage workflow must exist)
- US_039 task_002_be_document_parsing_queue_orchestration (Replacement documents must be enqueueable for reprocessing)
- US_040 task_002_be_extracted_data_persistence_and_document_outcome_orchestration (Replacement extraction persistence must exist)
- task_004_db_document_versioning_and_extracted_data_archive_support (Document-version and extracted-data archive fields must exist)
- US_013 task_002_be_rbac_authorization (Staff-only replacement authorization must exist)

## Impacted Components

- **MODIFY** `ClinicalDocumentsController` - Add staff-authorized replacement-upload endpoint bound to an existing document version (Server/Controllers/)
- **NEW** `IDocumentReplacementService` / `DocumentReplacementService` - Coordinate replacement upload, queueing, activation, archival, and reconsolidation-needed signaling (Server/Services/Documents/)
- **NEW** replacement DTOs - Request and response contracts for replacement uploads and activation status (Server/Models/DTOs/)
- **MODIFY** `ClinicalDocumentUploadService` - Reuse secure upload primitives for replacement versions without duplicating storage logic (Server/Services/Documents/)
- **MODIFY** `ExtractedDataPersistenceService` - Finalize replacement activation and archive superseded extracted rows only after successful replacement persistence (Server/Services/Documents/)
- **MODIFY** document-status event path - Emit a reconsolidation-needed signal or durable flag for later EP-007 processing when a replacement becomes active (Server/Services/Documents/ or Server/BackgroundServices/)
- **MODIFY** `Program.cs` - Register replacement orchestration services

## Implementation Plan

1. **Add a staff-authorized replacement endpoint** that accepts a file and metadata for a target document being replaced while preserving the original document ID lineage.
2. **Create a new document version rather than mutating the old one in place** so the old file and extracted rows remain available until replacement processing succeeds.
3. **Reuse the existing secure upload and queue orchestration path** so replacement documents are stored, validated, and reprocessed consistently with initial uploads.
4. **Delay active-version promotion until parsing and extraction persistence succeed** to avoid losing the currently active data set when the replacement file is poor or the pipeline fails.
5. **Archive superseded extracted rows after successful replacement activation** rather than deleting them, and mark the previous document version as superseded.
6. **Emit a reconsolidation-needed signal after activation** so EP-007 can refresh the patient’s 360 profile from the newly active extracted data without coupling this story to the full consolidation implementation.
7. **Return replacement status clearly** so the frontend can distinguish uploading, reprocessing, activated, superseded, and failed-replacement outcomes.

## Current Project State

```text
Server/
  Controllers/
    ClinicalDocumentsController.cs
  Services/
    Documents/
      ClinicalDocumentUploadService.cs
      DocumentParsingQueueService.cs
      ExtractedDataPersistenceService.cs
  Models/
    DTOs/
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Controllers/ClinicalDocumentsController.cs | Add replacement-upload endpoint for superseding an existing document version |
| CREATE | Server/Services/Documents/IDocumentReplacementService.cs | Interface for replacement upload and activation orchestration |
| CREATE | Server/Services/Documents/DocumentReplacementService.cs | Coordinate replacement storage, queueing, activation, archival, and reconsolidation signaling |
| CREATE | Server/Models/DTOs/ReplaceClinicalDocumentRequest.cs | Request contract for replacement upload operations |
| CREATE | Server/Models/DTOs/ClinicalDocumentReplacementResponse.cs | Response contract for replacement processing and activation state |
| MODIFY | Server/Services/Documents/ClinicalDocumentUploadService.cs | Reuse upload validation and storage logic for replacement versions |
| MODIFY | Server/Services/Documents/ExtractedDataPersistenceService.cs | Archive superseded extracted rows and finalize active-version promotion after successful replacement persistence |
| MODIFY | Server/Program.cs | Register replacement orchestration services |

## External References

- ASP.NET Core file uploads: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-8.0
- EF Core transactions: https://learn.microsoft.com/en-us/ef/core/saving/transactions
- ASP.NET Core hosted services: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Expose a staff-only replacement-upload endpoint for existing clinical documents
- [ ] Create new document versions instead of mutating the existing active version in place
- [ ] Reuse secure upload and queue orchestration for replacement processing
- [ ] Keep prior versions active until replacement parsing and extraction persistence succeed
- [ ] Archive superseded extracted rows instead of deleting them after successful activation
- [ ] Emit a reconsolidation-needed signal or flag after replacement activation
- [ ] Return replacement lifecycle status clearly to SCR-012 without implementing full profile consolidation here