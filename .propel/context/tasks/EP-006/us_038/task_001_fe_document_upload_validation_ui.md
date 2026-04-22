# Task - task_001_fe_document_upload_validation_ui

## Requirement Reference

- User Story: US_038
- Story Location: .propel/context/tasks/EP-006/us_038/us_038.md
- Acceptance Criteria:
    - AC-1: Given a staff member is on the Document Upload screen, When they select or drag-and-drop a file, Then the system validates the format and size before upload begins.
    - AC-3: Given the staff member uploads a document, When they assign a category, Then the category is saved to the ClinicalDocument record.
    - AC-4: Given the upload succeeds, When the operation completes, Then the source document attribution is recorded.
    - AC-5: Given a file exceeds 10MB or has an unsupported format, When the upload is attempted, Then the system rejects it with an error listing supported formats and size limits.
- Edge Case:
    - EC-1: If the upload is interrupted mid-transfer, show a retry path in the UI, remove any partial-progress state, and do not present the document as uploaded.
    - EC-2: If a file has a valid extension but corrupt content, allow the upload success state while keeping later parsing-error messaging out of this story scope.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-012-document-upload.html |
| **Screen Spec** | figma_spec.md#SCR-012 |
| **UXR Requirements** | UXR-206, UXR-505, UXR-506, UXR-606 |
| **Design Tokens** | designsystem.md#Color Palette, designsystem.md#Typography, designsystem.md#Spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement (UI Tasks Only)**

**Wireframe Status = AVAILABLE:**
- **MUST** open and reference the wireframe file during UI implementation
- **MUST** match layout, spacing, typography, and colors from the wireframe
- **MUST** implement all states shown in wireframe (default, hover, focus, error, loading)
- **MUST** validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | React | 18.x |
| UI Component Library | Material-UI (MUI) | 5.x |
| State Management | React Query + Zustand | 4.x / 4.x |
| Language | TypeScript | 5.x |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
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

Implement the staff-facing document upload experience for SCR-012. This frontend task delivers drag-and-drop and file-picker upload entry points, pre-upload validation for the story-supported formats and 10MB limit, category selection constrained to lab result, prescription, clinical note, and imaging report, progress and retry states for in-flight uploads, and clear success or validation messaging aligned to the wireframe. The UI must treat upload as complete only after the backend confirms secure storage and `ClinicalDocument` creation, while remaining ready for later parsing-status enhancements in US_039 and later EP-006 stories.

## Dependent Tasks

- task_002_be_secure_clinical_document_upload_api (Upload endpoint, validation rules, and success payload must exist)
- US_062 - Cross-Epic (EP-010) - Patient profile document list patterns should be reused if available for post-upload refresh behavior

## Impacted Components

- **NEW** `DocumentUploadPanel` - Staff upload panel with category selection, drag-and-drop zone, and supported-format guidance (app/components/documents/)
- **NEW** `UploadedDocumentList` - Client-side file list showing progress, success, retry, and validation-error states (app/components/documents/)
- **NEW** `useClinicalDocumentUpload` - React Query mutation hook for multipart upload and upload-state transitions (app/hooks/)
- **NEW** `documentUploadValidation.ts` - Shared client-side validation helpers for file type and 10MB size rules (app/utils/ or app/features/documents/)
- **MODIFY** `PatientProfilePage` or `DocumentUploadPage` - Mount SCR-012 upload workflow and refresh the visible document list after success (app/pages/)
- **MODIFY** shared notification or toast path - Show upload completion and recoverable upload-error feedback per UXR-505 and UXR-606 (app/components/common/ or app/store/)

## Implementation Plan

1. **Build the SCR-012 upload panel** using the wireframe layout, with category selection shown before or alongside file drop so the chosen category travels with each submitted file.
2. **Implement client-side validation before upload begins** for PDF, DOCX, TXT, PNG, and JPG only, enforcing the 10MB maximum from the story even though the mock wireframe hint is broader.
3. **Support both drag-and-drop and file-picker flows** with visible drag-over styling, keyboard-triggered browse behavior, and screen-reader announcements for validation or progress updates.
4. **Show upload progress and retry affordances** for in-flight or interrupted uploads, clearing failed partial client state and preventing interrupted files from appearing as persisted documents.
5. **Persist category and attribution details through the UI contract** by sending category with the upload request and rendering returned filename, upload timestamp, uploader attribution, and `uploaded` status after success.
6. **Handle success and error messaging explicitly** by listing supported formats and size limits in validation errors, showing completion toasts for successful uploads, and deferring corrupt-content handling to later parsing flows.
7. **Keep the upload UI ready for later status expansion** so US_039 can add queued or processing indicators without reworking the initial file-selection and validation interaction model.

## Current Project State

```text
app/
  components/
    documents/
    common/
  hooks/
  pages/
    PatientProfilePage.tsx
    DocumentUploadPage.tsx
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/components/documents/DocumentUploadPanel.tsx | SCR-012 upload UI with category selector, drop zone, and validation guidance |
| CREATE | app/components/documents/UploadedDocumentList.tsx | Per-file progress, success, retry, and error state list |
| CREATE | app/hooks/useClinicalDocumentUpload.ts | Multipart upload mutation hook with progress and retry state handling |
| CREATE | app/utils/documentUploadValidation.ts | Client-side validation helpers for allowed formats and 10MB size limit |
| MODIFY | app/pages/DocumentUploadPage.tsx | Mount upload panel, list, and success refresh behavior for SCR-012 |
| MODIFY | app/components/common/ToastProvider.tsx | Surface upload completion and validation or interruption feedback |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 file-upload patterns (general component guidance): https://mui.com/material-ui/react-button/
- TanStack Query v4 mutations: https://tanstack.com/query/v4/docs/framework/react/guides/mutations
- WAI-ARIA live regions: https://www.w3.org/WAI/WCAG21/Techniques/aria/ARIA19

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [x] Implement drag-and-drop and browse-based document selection on SCR-012
- [x] Validate PDF, DOCX, TXT, PNG, and JPG files against the 10MB limit before upload starts
- [x] Capture the document category values required by the story and submit them with the upload request
- [x] Show progress, interruption, retry, success, and validation-error states in the uploaded-file list
- [x] Render backend-confirmed filename, upload timestamp, uploader attribution, and `uploaded` status after success
- [x] Announce upload progress and validation feedback accessibly for dynamic updates on the screen
- [x] Keep the UI aligned to the wireframe while using story rules where the wireframe hint is broader than the acceptance criteria
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete