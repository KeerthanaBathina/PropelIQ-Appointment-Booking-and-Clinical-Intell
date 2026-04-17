# Task - task_001_fe_document_parsing_status_and_toast_notifications

## Requirement Reference

- User Story: US_039
- Story Location: .propel/context/tasks/EP-006/us_039/us_039.md
- Acceptance Criteria:
    - AC-2: Given a parsing job is picked up from the queue, When the AI parser begins processing, Then the status updates to `parsing` and a toast notification is shown on SCR-012.
    - AC-3: Given the AI parser completes extraction, When results are ready, Then the status updates to `parsed` and the staff member sees a toast notification with a link to review results.
    - AC-5: Given all retry attempts fail, When the final retry fails, Then the status is set to `failed` and the staff member is notified with a manual review option.
- Edge Case:
    - EC-1: If Redis is unavailable and the backend falls back to synchronous processing, show a warning that processing may take longer without breaking the upload flow.
    - EC-2: When parsing failures persist after retries, surface a manual-review path instead of leaving the staff member with an unrecoverable status-only message.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-012-document-upload.html |
| **Screen Spec** | figma_spec.md#SCR-012 |
| **UXR Requirements** | UXR-206, UXR-505, UXR-605 |
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

Implement the SCR-012 frontend behavior for asynchronous parsing progress and outcome messaging. This task extends the document upload screen so staff can see `queued`, `parsing`, `parsed`, and `failed` states as background work advances, receive toast notifications for parsing start and completion, open a review-results link when parsing succeeds, and access a manual-review option when automated parsing fails permanently. The UI must also communicate degraded behavior when queue infrastructure is unavailable and the system falls back to slower synchronous processing.

## Dependent Tasks

- US_038 task_001_fe_document_upload_validation_ui (Base SCR-012 upload interactions and file list must exist)
- task_002_be_document_parsing_queue_orchestration (Status transitions and event payloads must exist)
- task_003_ai_document_parser_worker_and_retry_orchestration (Parsing start, completion, and failure outcomes must exist)

## Impacted Components

- **MODIFY** `DocumentUploadPanel` - Surface parsing-state badges, degraded-processing warning, and result-link actions on SCR-012 (app/components/documents/)
- **MODIFY** `UploadedDocumentList` - Render `queued`, `parsing`, `parsed`, and `failed` states with review and manual-review actions (app/components/documents/)
- **NEW** `useClinicalDocumentParsingStatus` - Query or subscription hook for document parsing status refresh on SCR-012 (app/hooks/)
- **MODIFY** `ToastProvider` - Show background parsing start, success, and permanent-failure notifications with actionable links (app/components/common/)
- **MODIFY** `DocumentUploadPage` - Wire polling or event refresh for parsing status changes after upload success (app/pages/)

## Implementation Plan

1. **Extend the SCR-012 document list state model** so uploaded documents can transition visibly through `queued`, `parsing`, `parsed`, and `failed` without being confused with raw upload progress.
2. **Show background-operation toasts** when parsing starts and completes, using UXR-505 messaging patterns and including a review-results action once parsed output is ready.
3. **Expose a manual-review option for permanent failures** that aligns with the screen’s error state and UXR-605 fallback guidance rather than leaving the user with passive failure text.
4. **Add degraded-mode warning UI** for the Redis-unavailable fallback path so staff understand why processing may take longer when synchronous parsing is used.
5. **Refresh document status after upload** using a status query or lightweight polling strategy that remains efficient during queue bursts and does not require a full page reload.
6. **Announce state changes accessibly** through live regions so screen readers receive parsing start, completion, and failure updates on the same screen.
7. **Keep link actions future-compatible** so later EP-006 review screens can attach to the parsed-result navigation target without redesigning SCR-012 notifications.

## Current Project State

```text
app/
  components/
    documents/
      DocumentUploadPanel.tsx
      UploadedDocumentList.tsx
    common/
      ToastProvider.tsx
  hooks/
    useClinicalDocumentUpload.ts
  pages/
    DocumentUploadPage.tsx
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/hooks/useClinicalDocumentParsingStatus.ts | Fetch or subscribe to parsing-status updates for uploaded documents |
| MODIFY | app/components/documents/DocumentUploadPanel.tsx | Show queue fallback warning and parsing status context on SCR-012 |
| MODIFY | app/components/documents/UploadedDocumentList.tsx | Render queued/parsing/parsed/failed states with review or manual-review actions |
| MODIFY | app/components/common/ToastProvider.tsx | Add toasts for parsing start, completion, and permanent failure |
| MODIFY | app/pages/DocumentUploadPage.tsx | Wire status refresh and actionable notification behavior after upload |

## External References

- React 18 documentation: https://react.dev/reference/react
- TanStack Query v4 polling and refetching: https://tanstack.com/query/v4/docs/framework/react/guides/window-focus-refetching
- MUI Snackbar API: https://mui.com/material-ui/react-snackbar/
- WAI-ARIA live regions: https://www.w3.org/WAI/WCAG21/Techniques/aria/ARIA19

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [ ] Display `queued`, `parsing`, `parsed`, and `failed` states distinctly from file-upload progress on SCR-012
- [ ] Show toasts when parsing begins, completes, or permanently fails
- [ ] Include a review-results link when parsing succeeds
- [ ] Include a manual-review action when parsing fails after all retries
- [ ] Warn staff when Redis fallback causes slower synchronous parsing
- [ ] Announce parsing-state changes accessibly for dynamic updates on the screen
- [ ] Keep the status UI and notifications aligned to the SCR-012 wireframe and error-state guidance
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete