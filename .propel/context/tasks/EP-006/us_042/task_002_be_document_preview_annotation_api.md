# Task - task_002_be_document_preview_annotation_api

## Requirement Reference

- User Story: US_042
- Story Location: .propel/context/tasks/EP-006/us_042/us_042.md
- Acceptance Criteria:
    - AC-1: Given a document has been parsed, When the staff member opens the document preview, Then the system displays the original document with highlighted regions indicating where data was extracted.
    - AC-4: Given a staff member views a document preview, When hovering over a highlighted region, Then a tooltip shows the extracted data value and confidence score for that region.
- Edge Case:
    - EC-1: If the document format does not support region highlighting, the API must return text-preview annotations rather than empty overlay data.
    - EC-2: Preview access must remain secure for encrypted stored files and must not expose raw storage paths directly to the client.

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
| Logging | Serilog | 8.x |
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

Implement the backend preview API that turns stored clinical documents plus extracted-data attribution into a frontend-ready preview model. This task exposes secure document-preview access, returns geometric highlight coordinates when they exist, falls back to inline text annotations when they do not, and packages extracted values with confidence information for tooltip rendering. The API must build on the structured page number and extraction-region metadata introduced by US_040 and the confidence metadata from US_041, while keeping preview retrieval separate from replacement upload and reprocessing orchestration.

## Dependent Tasks

- US_038 task_002_be_secure_clinical_document_upload_api (Stored documents and encrypted storage access path must exist)
- US_040 task_002_be_extracted_data_persistence_and_document_outcome_orchestration (Structured extracted-data rows and source attribution must exist)
- US_040 task_003_db_extracted_data_attribution_and_document_status_support (Page number and extraction-region metadata must exist)
- US_041 task_002_be_extracted_data_verification_api (Confidence and verification metadata response patterns should remain consistent)

## Impacted Components

- **NEW** `DocumentPreviewController` - Staff-authorized endpoints for preview metadata and secure preview-stream access (Server/Controllers/)
- **NEW** `IDocumentPreviewService` / `DocumentPreviewService` - Builds region-overlay or text-annotation preview models from stored documents and extracted data (Server/Services/Documents/)
- **NEW** preview DTOs - Preview response contracts for document metadata, region boxes, inline annotations, and tooltip payloads (Server/Models/DTOs/)
- **MODIFY** encrypted storage access path - Add secure read or stream support for preview generation without leaking storage implementation details (Server/Services/Documents/)
- **MODIFY** extracted-data query path - Include confidence score, archived-state filtering, and active-version constraints in preview queries (Server/Services/Documents/)
- **MODIFY** `Program.cs` - Register preview services and secure preview dependencies

## Implementation Plan

1. **Add staff-authorized preview endpoints** that load a document by ID, verify access, and return a preview model suitable for SCR-012.
2. **Return region overlays for supported document formats** by mapping stored page-number and extraction-region metadata into frontend-ready coordinates tied to the original document pages.
3. **Return inline annotations for unsupported highlight formats** by exposing extracted values and confidence details anchored to text segments or ordered annotation blocks instead of geometric boxes.
4. **Bundle tooltip metadata with every annotation** so the frontend can display extracted value and confidence score without extra round trips.
5. **Keep preview access secure** by issuing stream tokens, temporary URLs, or controller-mediated file reads rather than exposing raw encrypted storage keys or file-system paths.
6. **Filter preview data to the active document version** so superseded documents do not accidentally surface archived extraction rows as current annotations.
7. **Keep preview logic separate from replacement flows** so later re-upload orchestration can reuse the preview query without coupling upload-state transitions into read endpoints.

## Current Project State

```text
Server/
  Controllers/
    ClinicalDocumentsController.cs
  Services/
    Documents/
      EncryptedFileStorageService.cs
      ExtractedDataPersistenceService.cs
  Models/
    DTOs/
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/DocumentPreviewController.cs | Staff-only endpoints for document preview metadata and secure content access |
| CREATE | Server/Services/Documents/IDocumentPreviewService.cs | Interface for document preview retrieval and annotation mapping |
| CREATE | Server/Services/Documents/DocumentPreviewService.cs | Build region overlays or inline text annotations from stored documents and extracted data |
| CREATE | Server/Models/DTOs/DocumentPreviewResponse.cs | Preview contract containing document metadata, overlays, annotations, and tooltip payloads |
| CREATE | Server/Models/DTOs/DocumentPreviewAnnotation.cs | Annotation contract for region coordinates or text-based preview hints |
| MODIFY | Server/Services/Documents/IEncryptedFileStorageService.cs | Add secure preview-read support for stored encrypted files |
| MODIFY | Server/Services/Documents/EncryptedFileStorageService.cs | Implement secure preview-read support without exposing raw storage paths |
| MODIFY | Server/Program.cs | Register document preview services and secure preview dependencies |

## External References

- ASP.NET Core file results: https://learn.microsoft.com/en-us/aspnet/core/web-api/action-return-types?view=aspnetcore-8.0#file-results
- ASP.NET Core authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-8.0
- OWASP file upload cheat sheet: https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Expose secure staff-only preview endpoints for parsed clinical documents
- [ ] Return region overlays using stored page number and extraction-region metadata where available
- [ ] Return inline text annotations when region highlighting is not supported
- [ ] Include extracted values and confidence scores in preview annotation payloads for tooltip rendering
- [ ] Restrict preview annotations to the active document version and non-archived extracted rows
- [ ] Prevent raw storage paths or encryption keys from being exposed in preview responses
- [ ] Keep preview-read behavior separate from replacement-upload orchestration