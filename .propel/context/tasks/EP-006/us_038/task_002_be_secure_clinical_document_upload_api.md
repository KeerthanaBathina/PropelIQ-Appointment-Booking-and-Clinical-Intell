# Task - task_002_be_secure_clinical_document_upload_api

## Requirement Reference

- User Story: US_038
- Story Location: .propel/context/tasks/EP-006/us_038/us_038.md
- Acceptance Criteria:
    - AC-1: Given a staff member selects a file, When upload begins, Then the system validates format and size before upload processing.
    - AC-2: Given the document passes validation, When upload completes, Then the file is stored with AES-256 encryption at rest and a `ClinicalDocument` record is created with status `uploaded`.
    - AC-3: Given a category is assigned, When the upload completes, Then the category is saved to the `ClinicalDocument` record.
    - AC-4: Given the upload succeeds, When the operation completes, Then filename, upload timestamp, and uploading user are recorded.
    - AC-5: Given the upload is invalid, When the request is attempted, Then the system rejects it with explicit supported-format and size guidance.
- Edge Case:
    - EC-1: If the transfer is interrupted or storage fails mid-request, discard the partial upload and do not create a `ClinicalDocument` row.
    - EC-2: If the file content is corrupt but passes extension and size checks, accept storage and persistence, leaving parsing failure handling to the later AI extraction workflow.

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
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| Logging | Serilog | 8.x |
| API Documentation | Swagger (Swashbuckle) | 6.x |
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

Implement the staff-only clinical document upload API and secure storage orchestration. This backend task validates multipart uploads against the story-supported file types and 10MB maximum, encrypts accepted files before durable storage, records document metadata and attribution in `ClinicalDocument`, and returns the persisted document details needed by SCR-012. The workflow must be atomic at the application level so interrupted transfers or storage failures do not leave partial files or orphaned records behind, and it must stop at `uploaded` status because queueing for AI parsing is introduced by US_039.

## Dependent Tasks

- task_003_db_clinical_document_metadata_and_upload_status_support (Upload metadata and `uploaded` status persistence must exist)
- US_001 - Foundational - ASP.NET Core API scaffold and authenticated request pipeline must exist
- US_008 - Foundational - `ClinicalDocument` entity baseline must exist
- US_013 task_002_be_rbac_authorization (Staff-only authorization policies must exist)

## Impacted Components

- **NEW** `ClinicalDocumentsController` upload endpoint - Staff-authorized multipart endpoint for secure document upload (Server/Controllers/)
- **NEW** `IClinicalDocumentUploadService` / `ClinicalDocumentUploadService` - Validates uploads, encrypts files, persists `ClinicalDocument`, and cleans up partial failures (Server/Services/Documents/)
- **NEW** `IEncryptedFileStorageService` / `EncryptedFileStorageService` - Applies AES-256 encryption before durable file storage and supports delete-on-failure cleanup (Server/Services/Documents/)
- **NEW** `ClinicalDocumentUploadRequest` / `ClinicalDocumentUploadResponse` - Multipart request contract and persisted metadata response DTOs (Server/Models/DTOs/)
- **MODIFY** `AuditService` or document-audit integration path - Record secure document upload events with user attribution (Server/Services/)
- **MODIFY** `Program.cs` - Register secure file storage and upload orchestration services

## Implementation Plan

1. **Add a staff-authorized multipart upload endpoint** that accepts file plus category, validates request size early, and rejects unsupported file types with clear supported-format guidance.
2. **Perform server-side validation independently of the UI** by checking extension, MIME type where possible, and maximum file size of 10MB before any durable persistence occurs.
3. **Encrypt accepted file content with AES-256 before storage** using application-managed encryption configuration, storing only the encrypted payload and the resulting storage location metadata.
4. **Persist a `ClinicalDocument` record in the same upload workflow** with category, original filename, uploader user, upload timestamp, secure storage pointer, and processing status `uploaded`.
5. **Keep upload atomic across storage and persistence** by deleting encrypted partial files if the database write fails, and by skipping record creation entirely if upload or encryption is interrupted.
6. **Log upload activity with attribution** so operational or audit trails capture who uploaded which document and when, without exposing raw PHI in application logs.
7. **Leave parsing orchestration out of this story** by returning the uploaded document metadata immediately and deferring any queue transition from `uploaded` to `queued` to US_039.

## Current Project State

```text
Server/
  Controllers/
  Services/
    Documents/
  Models/
    DTOs/
    Entities/
      ClinicalDocument.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/ClinicalDocumentsController.cs | Staff-only multipart upload endpoint for secure clinical document ingestion |
| CREATE | Server/Services/Documents/IClinicalDocumentUploadService.cs | Interface for validation, storage, and ClinicalDocument persistence orchestration |
| CREATE | Server/Services/Documents/ClinicalDocumentUploadService.cs | Validate uploads, encrypt files, create ClinicalDocument records, and clean up failures |
| CREATE | Server/Services/Documents/IEncryptedFileStorageService.cs | Interface for AES-256 encrypted file write and cleanup operations |
| CREATE | Server/Services/Documents/EncryptedFileStorageService.cs | Store encrypted files at rest and remove partial artifacts on failure |
| CREATE | Server/Models/DTOs/ClinicalDocumentUploadRequest.cs | Category and multipart upload request contract |
| CREATE | Server/Models/DTOs/ClinicalDocumentUploadResponse.cs | Persisted document metadata returned to the frontend after successful upload |
| MODIFY | Server/Services/AuditService.cs | Record document upload events with uploader attribution |
| MODIFY | Server/Program.cs | Register secure upload and encrypted file storage services |

## External References

- ASP.NET Core file uploads: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads?view=aspnetcore-8.0
- ASP.NET Core authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-8.0
- .NET AES cryptography: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes
- OWASP file upload cheat sheet: https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Expose a staff-only multipart upload endpoint for clinical documents
- [ ] Enforce server-side file type and 10MB size validation regardless of client behavior
- [ ] Encrypt accepted file payloads with AES-256 before durable storage
- [ ] Persist ClinicalDocument category, original filename, upload timestamp, uploader user, and `uploaded` status
- [ ] Remove partial files and avoid orphaned ClinicalDocument rows when upload, encryption, or persistence fails
- [ ] Return the metadata needed by SCR-012 to show uploaded attribution immediately after success
- [ ] Keep AI queueing and later processing-status transitions out of this story scope