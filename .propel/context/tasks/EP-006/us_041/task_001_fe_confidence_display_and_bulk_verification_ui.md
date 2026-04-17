# Task - task_001_fe_confidence_display_and_bulk_verification_ui

## Requirement Reference

- User Story: US_041
- Story Location: .propel/context/tasks/EP-006/us_041/us_041.md
- Acceptance Criteria:
    - AC-2: Given an extracted data point has confidence below 0.80, When the results are displayed, Then the data point is visually flagged with an amber/red indicator and marked for mandatory manual verification.
    - AC-3: Given a staff member views the extraction results, When they see confidence scores, Then scores are color-coded: green (≥0.90), amber (0.80-0.89), red (<0.80).
    - AC-4: Given a data point is flagged for verification, When the staff member confirms or corrects it, Then the verification status updates to "verified" with staff attribution and timestamp.
- Edge Case:
    - EC-1: If the AI cannot assign a confidence score, display the item as `confidence-unavailable`, show a 0.00-equivalent red state, and require manual review before it can be treated as verified.
    - EC-2: If multiple flagged items are selected, allow the staff user to confirm them in bulk with a single confirmation step and clear success feedback.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-012-document-upload.html, .propel/context/wireframes/Hi-Fi/wireframe-SCR-013-patient-profile-360.html |
| **Screen Spec** | figma_spec.md#SCR-012, figma_spec.md#SCR-013 |
| **UXR Requirements** | UXR-105, UXR-202, UXR-203, UXR-205, UXR-301, UXR-505 |
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

Implement the confidence-review experience for extracted clinical data on SCR-012 and SCR-013. This frontend task renders color-coded confidence badges, makes low-confidence and `confidence-unavailable` items visually distinct, adds single-item verification and bulk verification interactions for flagged rows, and surfaces verification outcomes with accessible confirmation feedback. The UI must stay within the existing extraction-results tables and upload-to-profile flow, without expanding into later preview or conflict-resolution stories.

## Dependent Tasks

- task_002_be_extracted_data_verification_api (Verification endpoints and updated extraction-review payloads must exist)
- task_003_ai_confidence_score_assignment_orchestration (Confidence scores, review flags, and `confidence-unavailable` metadata must be emitted consistently)
- task_004_db_extracted_data_confidence_and_verification_support (Verification status and review-reason persistence must exist)

## Impacted Components

- **NEW** `ExtractedDataConfidenceBadge` - Reusable confidence badge that maps thresholds and unavailable states to wireframe-aligned colors and accessible labels (app/components/documents/)
- **NEW** `BulkVerificationDialog` - Confirmation dialog for verifying multiple flagged extraction rows in one action (app/components/documents/)
- **NEW** `useExtractedDataVerification` - React Query mutations for single-item verify or correct flows and bulk verification submission (app/hooks/)
- **MODIFY** extracted-data table components on SCR-012 and SCR-013 - Add confidence badge, flagged-row state, selection checkboxes, and verify actions within medication, diagnosis, procedure, and allergy tables (app/components/documents/ or app/components/patient/)
- **MODIFY** `DocumentUploadPage` and `PatientProfilePage` - Refresh extraction rows and verification counts after confirm or bulk verify operations (app/pages/)
- **MODIFY** shared notification or toast path - Show verification-complete and bulk-review success feedback per UXR-505 (app/components/common/ or app/store/)

## Implementation Plan

1. **Render reusable confidence badges** that convert numeric scores into green, amber, or red presentation and expose an explicit `confidence-unavailable` visual treatment when the backend marks the score as unavailable.
2. **Highlight mandatory-review rows clearly** by pairing low-confidence badges with a pending-review status, row emphasis, and action affordances that distinguish flagged items from already verified items.
3. **Add single-item verification and correction interactions** so staff can confirm a row as-is or open the existing editable row pathway to correct data before submitting verification.
4. **Support bulk verification for flagged rows only** with row selection, a single confirmation dialog, disabled states when nothing eligible is selected, and optimistic UI refresh after success.
5. **Preserve accessibility and responsive behavior** by keeping all actions keyboard reachable, labeling confidence values for screen readers, and maintaining usable table or stacked-card layouts at mobile and tablet breakpoints.
6. **Update both SCR-012 and SCR-013 views consistently** so upload-screen review tables and patient-profile extraction panels show the same confidence semantics and verification status language.
7. **Keep later workflows out of scope** by avoiding source-document preview, conflict comparison, or re-upload logic beyond the verification feedback required by this story.

## Current Project State

```text
app/
  components/
    documents/
    common/
    patient/
  hooks/
  pages/
    DocumentUploadPage.tsx
    PatientProfilePage.tsx
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/components/documents/ExtractedDataConfidenceBadge.tsx | Reusable confidence indicator for score thresholds and unavailable states |
| CREATE | app/components/documents/BulkVerificationDialog.tsx | Single-confirmation dialog for bulk verification of flagged extraction rows |
| CREATE | app/hooks/useExtractedDataVerification.ts | Query and mutation hooks for single and bulk verification actions |
| MODIFY | app/components/documents/UploadedDocumentList.tsx | Show confidence-review state or navigation affordances after extraction completes on SCR-012 |
| MODIFY | app/components/patient/PatientProfileExtractedDataTabs.tsx | Add confidence badges, row selection, and verify actions to profile extraction tables |
| MODIFY | app/pages/DocumentUploadPage.tsx | Bind confidence-review state and verification actions to SCR-012 |
| MODIFY | app/pages/PatientProfilePage.tsx | Refresh extracted-data views and counts after verification actions |
| MODIFY | app/components/common/ToastProvider.tsx | Display single-item and bulk verification success or failure feedback |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI table guidance: https://mui.com/material-ui/react-table/
- TanStack Query v4 mutations: https://tanstack.com/query/v4/docs/framework/react/guides/mutations
- WAI-ARIA dialog pattern: https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [ ] Render green, amber, red, and `confidence-unavailable` confidence states consistently on SCR-012 and SCR-013
- [ ] Mark all `<0.80` and unavailable items as pending mandatory review in the extraction-results UI
- [ ] Support single-item verification and correction actions for flagged extraction rows
- [ ] Support bulk verification with selection, confirmation, disabled, loading, and success states
- [ ] Refresh visible verification status and counts immediately after successful actions
- [ ] Keep confidence indicators accessible with clear labels, focus states, and keyboard operation
- [ ] Stay aligned to the supplied wireframes without introducing later preview or conflict-review features
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete