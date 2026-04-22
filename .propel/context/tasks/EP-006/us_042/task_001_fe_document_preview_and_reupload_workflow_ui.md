# Task - task_001_fe_document_preview_and_reupload_workflow_ui

## Requirement Reference

- User Story: US_042
- Story Location: .propel/context/tasks/EP-006/us_042/us_042.md
- Acceptance Criteria:
    - AC-1: Given a document has been parsed, When the staff member opens the document preview, Then the system displays the original document with highlighted regions indicating where data was extracted.
    - AC-2: Given a staff member determines extraction quality is insufficient, When they click "Re-Upload," Then the system allows a new version upload that replaces the previous document and triggers re-processing.
    - AC-4: Given a staff member views a document preview, When hovering over a highlighted region, Then a tooltip shows the extracted data value and confidence score for that region.
- Edge Case:
    - EC-1: If the document format does not support region highlighting, show a text-based preview with extracted values annotated inline instead of box overlays.
    - EC-2: When re-upload is launched from preview, keep the user in the SCR-012 workflow with clear processing feedback rather than forcing a separate navigation path.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-012-document-upload.html |
| **Screen Spec** | figma_spec.md#SCR-012 |
| **UXR Requirements** | UXR-202, UXR-203, UXR-205, UXR-301, UXR-505, UXR-606 |
| **Design Tokens** | designsystem.md#Spacing, designsystem.md#Tooltip, designsystem.md#Modal / Dialog |

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
| Backend API Contract | ASP.NET Core Web API | 8.x |
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

Implement the document preview and replacement workflow on SCR-012. This frontend task adds an in-context preview surface for parsed documents, renders extraction-region overlays when coordinates are available, falls back to inline annotated text for formats such as TXT, shows tooltips with extracted values and confidence scores, and launches a replacement upload flow when document quality is poor. The experience must stay within the existing document-upload screen patterns, use explicit progress and error feedback, and avoid expanding into patient-profile consolidation or conflict-resolution features owned by later stories.

## Dependent Tasks

- task_002_be_document_preview_annotation_api (Preview content, overlay coordinates, and tooltip metadata must exist)
- task_003_be_document_reupload_reprocessing_orchestration (Replacement upload and reprocessing endpoints must exist)
- task_004_db_document_versioning_and_extracted_data_archive_support (Document versioning and archived-result persistence must exist)
- US_041 task_001_fe_confidence_display_and_bulk_verification_ui (Confidence badge semantics and status language should remain consistent)

## Impacted Components

- **NEW** `DocumentPreviewDrawer` - SCR-012 preview surface for rendered documents and inline text fallback (app/components/documents/)
- **NEW** `DocumentExtractionOverlay` - Positions extracted-region highlights over previewable documents and handles hover or focus interactions (app/components/documents/)
- **NEW** `InlineExtractionAnnotations` - Text-preview fallback for non-region-capable formats such as TXT (app/components/documents/)
- **NEW** `useDocumentPreview` - Fetches preview metadata, annotations, and preview-stream URLs for a selected document version (app/hooks/)
- **NEW** `useDocumentReplacementUpload` - Submits replacement files and tracks reprocessing state for re-upload actions (app/hooks/)
- **MODIFY** `UploadedDocumentList` - Add preview and re-upload actions to extracted or low-quality document rows on SCR-012 (app/components/documents/)
- **MODIFY** `DocumentUploadPage` - Host preview drawer state, replacement-upload flow, and processing feedback (app/pages/)
- **MODIFY** `ToastProvider` - Surface preview-load failure, replacement success, and reprocessing feedback (app/components/common/)

## Implementation Plan

1. **Add a preview entry point on SCR-012** so parsed documents can be opened directly from the uploaded-file list without leaving the document workflow.
2. **Render overlay-based previews when region metadata exists** by layering highlight boxes over supported document renderers and syncing hover or focus behavior with tooltip content.
3. **Provide a text-annotation fallback for unsupported formats** so TXT and similar documents still expose extracted values and confidence details inline rather than pretending geometric highlighting is available.
4. **Show tooltip details for every highlighted extraction** including the extracted value, data type context where useful, and the confidence score already established by US_041.
5. **Launch replacement upload from preview context** with a clear confirmation, replacement-file picker, and progress feedback that keeps the old version visible until the new one finishes processing.
6. **Refresh SCR-012 document state after replacement actions** so superseded and active versions, reprocessing progress, and preview annotations update without a full page reload.
7. **Keep the UI scope limited to preview and replacement** by deferring patient-profile reconsolidation details and downstream data-merge visualization to EP-007.

## Current Project State

```text
app/
  components/
    documents/
    common/
  hooks/
  pages/
    DocumentUploadPage.tsx
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/components/documents/DocumentPreviewDrawer.tsx | SCR-012 preview container for document rendering, fallback text preview, and replacement actions |
| CREATE | app/components/documents/DocumentExtractionOverlay.tsx | Renders region highlights and tooltip interaction for previewable documents |
| CREATE | app/components/documents/InlineExtractionAnnotations.tsx | Renders annotated text fallback when box highlighting is unavailable |
| CREATE | app/hooks/useDocumentPreview.ts | Loads preview content, annotation metadata, and secure preview access details |
| CREATE | app/hooks/useDocumentReplacementUpload.ts | Handles replacement upload requests and reprocessing state |
| MODIFY | app/components/documents/UploadedDocumentList.tsx | Add preview and re-upload affordances on document rows |
| MODIFY | app/pages/DocumentUploadPage.tsx | Mount preview drawer and replacement flow on SCR-012 |
| MODIFY | app/components/common/ToastProvider.tsx | Show preview-load and replacement-processing feedback |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI dialog guidance: https://mui.com/material-ui/react-dialog/
- MUI tooltip guidance: https://mui.com/material-ui/react-tooltip/
- WAI-ARIA dialog pattern: https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [x] Add preview actions for parsed documents on SCR-012
- [x] Render extraction-region highlights for supported preview formats
- [x] Provide inline extracted-value annotations for non-region-capable formats such as TXT
- [x] Show tooltip content containing extracted value and confidence score for highlighted regions
- [x] Support replacement upload from the preview workflow with clear progress and error handling
- [x] Refresh active or superseded document state after replacement or reprocessing events
- [x] Keep the UI limited to preview and replacement without exposing downstream consolidation internals
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete