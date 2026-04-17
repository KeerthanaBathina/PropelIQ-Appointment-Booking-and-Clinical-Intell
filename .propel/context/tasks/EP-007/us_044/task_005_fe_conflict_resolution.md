# Task - task_005_fe_conflict_resolution

## Requirement Reference

- User Story: US_044
- Story Location: .propel/context/tasks/EP-007/us_044/us_044.md
- Acceptance Criteria:
  - AC-2: Given duplicate diagnoses with different dates are found, When the conflict is detected, Then the system flags the conflict with source citations from both documents.
  - AC-3: Given a medication contraindication is detected, When the conflict is flagged, Then the system escalates it with an "URGENT" indicator and moves it to the top of the review queue.
- Edge Case:
  - 3+ document conflicts: System shows all conflicting sources in the comparison view, not just pairs.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-013-patient-profile-360.html |
| **Screen Spec** | figma_spec.md#SCR-013 |
| **UXR Requirements** | UXR-104, UXR-105 |
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

Implement the conflict resolution UI components on the Patient Profile 360 screen (SCR-013). This includes the side-by-side comparison modal (UXR-104) for reviewing conflicting data from multiple documents, urgent conflict indicators with URGENT badge styling, conflict resolution/dismissal forms with staff notes, multi-document comparison layout supporting 3+ sources, and integration with the conflict API endpoints. Confidence scores are displayed with color-coded indicators (UXR-105). The conflict UI extends the existing PatientProfile360Page from US_043/task_005.

## Dependent Tasks

- task_003_be_conflict_api - Requires conflict API endpoints
- US_043/task_005_fe_patient_profile_360 - Requires PatientProfile360Page base component and ConflictAlertBanner

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `ConflictResolutionModal` component | app (Components) |
| NEW | `ConflictComparisonView` component | app (Components) |
| NEW | `ConflictSourceCard` component | app (Components) |
| NEW | `UrgentBadge` component | app (Components) |
| NEW | `ConflictResolutionForm` component | app (Components) |
| NEW | React Query hooks: `useConflicts`, `useConflictDetail`, `useResolveConflict` | app (Hooks) |
| MODIFY | `ConflictAlertBanner` - Add click handler to open ConflictResolutionModal | app (Components) |
| MODIFY | `PatientProfile360Page` - Integrate conflict modal and conflict count refresh | app (Pages) |

## Implementation Plan

1. Implement `ConflictResolutionModal` as MUI Dialog (fullWidth, maxWidth="lg") triggered from ConflictAlertBanner click. Modal displays the conflict list for the patient with filtering by type and severity. Each conflict row shows: type icon, description, severity chip, urgency badge, source document count, confidence score, and action buttons
2. Implement `ConflictComparisonView` as side-by-side layout (UXR-104) using MUI Grid:
   - For 2-source conflicts: two columns showing source data side by side
   - For 3+ source conflicts: horizontal scroll with one column per source document (Edge Case)
   - Each column header shows document name, category icon (UXR-404 from US_043), upload date
   - Conflicting values highlighted with warning-surface background (#FFF3E0)
   - Confidence badges per data point (green >=80%, amber 60-79%, red <60% per UXR-105)
3. Implement `ConflictSourceCard` as MUI Card showing a single source document's conflicting data: extracted value, confidence score, source attribution text, link to original document
4. Implement `UrgentBadge` component: MUI Chip with error color (#D32F2F), "URGENT" label, pulsing animation for medication contraindications (AC-3). Urgent conflicts render at the top of the conflict list
5. Implement `ConflictResolutionForm` with MUI TextField for resolution notes (required), two action buttons: "Resolve" (marks conflict resolved) and "Dismiss" (marks as false-positive). Calls PUT /resolve or PUT /dismiss endpoint
6. Create React Query hooks:
   - `useConflicts(patientId, filters)` - Fetches GET /api/patients/{id}/conflicts
   - `useConflictDetail(patientId, conflictId)` - Fetches conflict detail with all source citations
   - `useResolveConflict()` - Mutation for resolve/dismiss with cache invalidation
7. Modify `ConflictAlertBanner` (from US_043/task_005) to add onClick handler opening the ConflictResolutionModal. Update badge count from conflict summary endpoint
8. Implement loading, empty (no conflicts), and error states for the modal. Add toast notification on successful resolution/dismissal (UXR-505 from US_043)

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
app/
  src/
    pages/
      PatientProfile360Page.tsx
    components/
      profile/
        ProfileHeader.tsx
        ClinicalDataTabs.tsx
        DataPointTable.tsx
        ConfidenceBadge.tsx
        SourceCitationPanel.tsx
        VersionHistoryPanel.tsx
        ConflictAlertBanner.tsx
    hooks/
      usePatientProfile.ts
      useVersionHistory.ts
      useSourceCitation.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/components/conflict/ConflictResolutionModal.tsx | MUI Dialog with conflict list, filtering, and detail view orchestration |
| CREATE | app/src/components/conflict/ConflictComparisonView.tsx | Side-by-side layout for 2+ source documents with highlighted differences (UXR-104) |
| CREATE | app/src/components/conflict/ConflictSourceCard.tsx | MUI Card for individual source document data in comparison view |
| CREATE | app/src/components/conflict/UrgentBadge.tsx | MUI Chip with error styling and URGENT label for contraindications |
| CREATE | app/src/components/conflict/ConflictResolutionForm.tsx | Resolution/dismissal form with notes field and action buttons |
| CREATE | app/src/hooks/useConflicts.ts | React Query hook for conflict list with filtering |
| CREATE | app/src/hooks/useConflictDetail.ts | React Query hook for conflict detail with source citations |
| CREATE | app/src/hooks/useResolveConflict.ts | React Query mutation for resolve/dismiss with cache invalidation |
| MODIFY | app/src/components/profile/ConflictAlertBanner.tsx | Add onClick to open ConflictResolutionModal, update count from summary API |
| MODIFY | app/src/pages/PatientProfile360Page.tsx | Integrate ConflictResolutionModal state and conflict count refresh |

## External References

- [MUI 5 Dialog](https://mui.com/material-ui/react-dialog/) - Full-width modal for conflict resolution
- [MUI 5 Grid](https://mui.com/material-ui/react-grid/) - Side-by-side layout for comparison view
- [MUI 5 Chip](https://mui.com/material-ui/react-chip/) - Badge component for urgent indicator
- [React Query v4 Mutations](https://tanstack.com/query/v4/docs/framework/react/guides/mutations) - Mutation hooks for resolve/dismiss
- [CSS Scroll Snap](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_scroll_snap) - Horizontal scroll for 3+ source comparison

## Build Commands

- `cd app && npm install`
- `cd app && npm run build`
- `cd app && npm start`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Side-by-side comparison renders correctly for 2-source conflicts
- [ ] 3+ source conflicts display with horizontal scroll layout
- [ ] URGENT badge renders with error color for medication contraindictions
- [ ] Resolve/dismiss actions update conflict status and refresh UI
- [ ] Confidence badges display correct colors for all 3 thresholds

## Implementation Checklist

- [ ] Implement `ConflictResolutionModal` as MUI Dialog with conflict list, type/severity filtering, and detail view navigation
- [ ] Implement `ConflictComparisonView` with side-by-side layout for 2 sources and horizontal scroll for 3+ sources (UXR-104, Edge Case)
- [ ] Implement `ConflictSourceCard` displaying source document data, confidence badge, and attribution text
- [ ] Implement `UrgentBadge` with MUI Chip error styling and URGENT label for medication contraindications (AC-3)
- [ ] Implement `ConflictResolutionForm` with resolution notes field and resolve/dismiss action buttons
- [ ] Create React Query hooks (useConflicts, useConflictDetail, useResolveConflict) with cache invalidation on mutations
- [ ] Modify `ConflictAlertBanner` to open modal on click and update conflict count from summary endpoint
- [ ] Implement loading, empty, and error states for conflict modal with toast notifications on resolution
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
