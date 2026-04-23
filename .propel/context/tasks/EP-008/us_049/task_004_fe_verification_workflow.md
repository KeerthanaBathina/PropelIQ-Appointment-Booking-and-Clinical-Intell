# Task - task_004_fe_verification_workflow

## Requirement Reference

- User Story: US_049
- Story Location: .propel/context/tasks/EP-008/us_049/us_049.md
- Acceptance Criteria:
  - AC-1: Given AI has generated ICD-10 and CPT codes, When the staff member views the medical coding screen, Then codes are presented in a verification queue with AI justification, confidence score, and approve/override actions.
  - AC-2: Given a staff member approves an AI-suggested code, When they click "Approve," Then the code status changes to "verified" with staff attribution and timestamp in the audit trail.
  - AC-3: Given a staff member disagrees with an AI suggestion, When they select "Override," Then the system presents a code search interface and requires the staff member to enter a justification for the override.
  - AC-4: Given a code change is made (approval or override), When the change is saved, Then an immutable audit log entry records the old code, new code, justification, user, and timestamp.
- Edge Case:
  - Deprecated code approval: System blocks approval and shows the deprecated notice with suggested replacement codes.
  - Partial verification: Patient coding status shows "partially verified" with a progress indicator (e.g., 3/5 codes verified).

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-014-medical-coding.html |
| **Screen Spec** | figma_spec.md#SCR-014 |
| **UXR Requirements** | UXR-105 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement (UI Tasks Only)**

**IF Wireframe Status = AVAILABLE or EXTERNAL:**

- **MUST** open and reference the wireframe file/URL during UI implementation
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

Implement the code verification workflow UI on the SCR-014 Medical Coding Review screen. This includes the verification queue table with sortable columns showing AI-generated ICD-10 and CPT codes, AI justification text, color-coded confidence scores (UXR-105), approve/override action buttons per row, the override modal with code search and mandatory justification field, per-code audit trail expandable view, verification progress bar with partial verification status, and deprecated code alert blocking. The screen implements all 5 states: Default, Loading, Empty (no pending codes), Error, and Validation.

## Dependent Tasks

- task_003_be_verification_api - Requires verification API endpoints

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `MedicalCodingReviewPage` page | app (Pages) |
| NEW | `VerificationQueueTable` component | app (Components) |
| NEW | `CodeOverrideModal` component | app (Components) |
| NEW | `CodeAuditTrail` component | app (Components) |
| NEW | `VerificationProgressBar` component | app (Components) |
| NEW | `DeprecatedCodeAlert` component | app (Components) |
| NEW | `ConfidenceBadge` component (reusable) | app (Components) |
| NEW | React Query hooks: `useVerificationQueue`, `useApproveCode`, `useOverrideCode`, `useCodeSearch`, `useAuditTrail`, `useVerificationProgress` | app (Hooks) |

## Implementation Plan

1. Implement `MedicalCodingReviewPage` as the SCR-014 entry point with:
   - Page header with patient name and verification progress summary
   - VerificationProgressBar showing "3/5 codes verified" with MUI LinearProgress (EC-2)
   - VerificationQueueTable as the main content
   - All 5 screen states: Default (queue loaded), Loading (skeleton), Empty ("No pending codes for review"), Error (retry button), Validation (inline errors on override form)
2. Implement `VerificationQueueTable` as MUI Table with sortable columns (AC-1):
   - Columns: Code Type (ICD-10/CPT chip), Code Value, Description, AI Justification, Confidence Score (ConfidenceBadge), Status, Actions
   - Each row has "Approve" (green) and "Override" (amber) action buttons
   - Filter dropdown for code_type (All, ICD-10 only, CPT only)
   - Row expansion to show CodeAuditTrail per code (AC-4)
   - Approved rows show green checkmark badge with staff name and timestamp (AC-2)
   - Overridden rows show amber override badge with justification preview
3. Implement `ConfidenceBadge` as reusable MUI Chip component (UXR-105):
   - Green (#2E7D32) for confidence >= 80%
   - Amber (#ED6C02) for confidence 60-79%
   - Red (#D32F2F) for confidence < 60%
   - Display percentage value inside badge
4. Implement `CodeOverrideModal` as MUI Dialog (AC-3):
   - Code search TextField with debounced API call (300ms debounce)
   - Search results displayed in dropdown/list: code_value, description, is_deprecated flag
   - Deprecated codes shown with strikethrough and "Deprecated" chip
   - Justification MUI TextField (required, minLength 10, char counter)
   - "Save Override" and "Cancel" action buttons
   - Loading state while search API runs
5. Implement `CodeAuditTrail` as expandable MUI Accordion per code row (AC-4):
   - MUI Timeline showing each audit entry: action icon, old→new code values, justification, user name, timestamp
   - Entries ordered by timestamp descending (newest first)
   - Read-only (immutable display)
6. Implement `DeprecatedCodeAlert` as MUI Alert (severity="warning") shown when user attempts to approve a deprecated code (EC-1):
   - Alert text: "This code has been deprecated. Please select a replacement."
   - List of replacement code suggestions as clickable chips
   - Clicking a replacement opens CodeOverrideModal pre-filled with that code
7. Create React Query hooks:
   - `useVerificationQueue(patientId, codeTypeFilter)` - Fetches GET /api/patients/{id}/codes/verification-queue
   - `useApproveCode()` - Mutation for PUT /api/codes/{id}/approve with 409 error handling for deprecated codes
   - `useOverrideCode()` - Mutation for PUT /api/codes/{id}/override with cache invalidation
   - `useCodeSearch(query, codeType)` - Debounced search via GET /api/codes/search
   - `useAuditTrail(codeId)` - Fetches GET /api/codes/{id}/audit-trail on row expansion
   - `useVerificationProgress(patientId)` - Fetches verification progress for header bar
8. Implement toast notifications: "Code approved successfully", "Code overridden successfully", "Cannot approve deprecated code" (error toast on 409)

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
app/
  src/
    pages/
      PatientProfile360Page.tsx
    components/
      profile/
        ConfidenceBadge.tsx
    hooks/
      usePatientProfile.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/pages/MedicalCodingReviewPage.tsx | SCR-014 page with verification queue, progress bar, and 5 screen states |
| CREATE | app/src/components/coding/VerificationQueueTable.tsx | MUI Table with sortable columns, approve/override actions, row expansion |
| CREATE | app/src/components/coding/CodeOverrideModal.tsx | MUI Dialog with code search, deprecated code handling, justification field |
| CREATE | app/src/components/coding/CodeAuditTrail.tsx | MUI Accordion with Timeline showing immutable audit entries per code |
| CREATE | app/src/components/coding/VerificationProgressBar.tsx | MUI LinearProgress with "X/Y codes verified" label and status chip |
| CREATE | app/src/components/coding/DeprecatedCodeAlert.tsx | MUI Alert with replacement code suggestions as clickable chips |
| CREATE | app/src/components/shared/ConfidenceBadge.tsx | Reusable MUI Chip with color-coded confidence score (UXR-105) |
| CREATE | app/src/hooks/useVerificationQueue.ts | React Query hook for verification queue with code type filtering |
| CREATE | app/src/hooks/useApproveCode.ts | React Query mutation for code approval with 409 error handling |
| CREATE | app/src/hooks/useOverrideCode.ts | React Query mutation for code override with cache invalidation |
| CREATE | app/src/hooks/useCodeSearch.ts | React Query hook with debounced code search |
| CREATE | app/src/hooks/useAuditTrail.ts | React Query hook for per-code audit trail on expand |
| CREATE | app/src/hooks/useVerificationProgress.ts | React Query hook for verification progress |

## External References

- [MUI 5 Table](https://mui.com/material-ui/react-table/) - Data table with sortable columns and row expansion
- [MUI 5 Dialog](https://mui.com/material-ui/react-dialog/) - Override modal with form fields
- [MUI 5 Timeline](https://mui.com/material-ui/react-timeline/) - Audit trail visualization
- [MUI 5 LinearProgress](https://mui.com/material-ui/react-progress/#linear-determinate) - Verification progress bar
- [React Query v4 Mutations](https://tanstack.com/query/v4/docs/framework/react/guides/mutations) - Approve/override mutation hooks with cache invalidation

## Build Commands

- `cd app && npm install`
- `cd app && npm run build`
- `cd app && npm start`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Verification queue table renders AI codes with justification and confidence badges
- [ ] Approve action updates row to "verified" with green badge and staff name
- [ ] Override modal opens with code search, requires justification min 10 chars
- [ ] Deprecated code approval shows warning alert with replacement suggestions
- [ ] Audit trail expands per row showing immutable history entries
- [ ] Verification progress bar reflects accurate counts and status label

## Implementation Checklist

- [x] Implement MedicalCodingReviewPage with verification progress header and 5 screen states (Default, Loading, Empty, Error, Validation)
- [x] Implement VerificationQueueTable with sortable columns, code type filter, approve/override buttons, and row expansion for audit trail (AC-1)
- [x] Implement ConfidenceBadge as reusable MUI Chip with color-coded confidence thresholds: green >=80%, amber 60-79%, red <60% (UXR-105)
- [x] Implement CodeOverrideModal with debounced code search, deprecated code strikethrough, and justification TextField with min 10 char validation (AC-3)
- [x] Implement CodeAuditTrail as MUI Accordion with Timeline showing immutable audit entries per code (AC-4)
- [x] Implement DeprecatedCodeAlert with MUI Alert and replacement code suggestion chips that pre-fill override modal (EC-1)
- [x] Create React Query hooks (useVerificationQueue, useApproveCode, useOverrideCode, useCodeSearch, useAuditTrail, useVerificationProgress) with cache invalidation on mutations
- [x] Implement VerificationProgressBar with MUI LinearProgress showing "X/Y codes verified" and partial/full status chip (EC-2)
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
