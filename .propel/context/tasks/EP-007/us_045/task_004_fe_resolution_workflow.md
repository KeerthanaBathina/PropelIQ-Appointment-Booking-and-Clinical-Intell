# Task - task_004_fe_resolution_workflow

## Requirement Reference

- User Story: US_045
- Story Location: .propel/context/tasks/EP-007/us_045/us_045.md
- Acceptance Criteria:
  - AC-1: Given a conflict is detected in patient data, When the staff member opens the patient profile, Then the system displays conflicting data side-by-side with source document citations.
  - AC-2: Given the staff member reviews a conflict, When they select the correct data value, Then the chosen value is saved to the consolidated profile and the conflict is marked "resolved" with staff attribution.
  - AC-3: Given a patient has data from multiple sources, When a data field has conflicting values, Then each source is displayed with its document name, upload date, and confidence score.
  - AC-4: Given a staff member resolves all conflicts for a patient, When the last conflict is resolved, Then the profile status updates to "verified" and an audit log entry is created.
- Edge Cases:
  - EC-1: If the staff partially resolves conflicts and navigates away, the progress is saved. Unresolved conflicts remain flagged for the next session.
  - EC-2: Both values correct — staff can select "Both Valid — Different Dates" which preserves both entries with distinct date attribution.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | Hi-Fi |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-013-patient-profile-360.html |
| **Screen Spec** | SCR-013 Patient Profile 360 |
| **UXR Requirements** | UXR-104 (side-by-side comparison), UXR-105 (confidence score indicators), UXR-003 (breadcrumb navigation) |
| **Design Tokens** | Radio: primary.500 (checked), neutral.400 (unchecked), 24×24 icon, 44px touch target. ConfidenceBadge: success.main (≥80%), warning.main (60–79%), error.main (<60%), white text. Alert: success type (green) for verification banner. LinearProgress: primary.500 for resolution progress. |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | React | 18.x |
| UI Library | MUI (Material UI) | 5.x |
| State Management | Zustand | 4.x |
| Data Fetching | React Query (TanStack Query) | 4.x |
| Language | TypeScript | 5.x |

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

Extend the ConflictResolutionModal and ConflictComparisonView (from US_044/task_005) with the staff data value selection workflow. Adds the ability for staff to select a specific source value as correct via radio buttons (AC-2), choose "Both Valid — Different Dates" when both values are correct (EC-2), view resolution progress across all patient conflicts (EC-1), auto-save partial progress on navigation away (EC-1), and see a verification status banner when all conflicts are resolved (AC-4). Each source card displays document name, upload date, and confidence score (AC-3) within the existing side-by-side layout (AC-1, UXR-104).

## Dependent Tasks

- task_003_be_resolution_api - Requires select-value, both-valid, resolution-progress, and verification-status API endpoints
- US_044/task_005_fe_conflict_resolution - Requires ConflictResolutionModal, ConflictComparisonView, ConflictSourceCard, ConflictResolutionForm, React Query hooks (useConflicts, useConflictDetail)

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| MODIFY | `ConflictComparisonView` - Add radio button value selection per source card | Client (Components) |
| MODIFY | `ConflictSourceCard` - Ensure document name, upload date, confidence score display (AC-3) | Client (Components) |
| MODIFY | `ConflictResolutionForm` - Replace generic resolve with value selection submit | Client (Components) |
| NEW | `ConflictValueSelector` - Radio group for selecting correct value + "Both Valid" option | Client (Components) |
| NEW | `BothValidDialog` - MUI Dialog for staff to provide explanation when "Both Valid" selected | Client (Components) |
| NEW | `VerificationStatusBanner` - Alert banner showing profile verified status | Client (Components) |
| NEW | `ResolutionProgressIndicator` - Progress bar with resolved/total counts | Client (Components) |
| MODIFY | `PatientProfile360Page` - Integrate VerificationStatusBanner and ResolutionProgressIndicator | Client (Pages) |
| NEW | React Query hooks: `useSelectValue`, `useBothValid`, `useResolutionProgress`, `useVerificationStatus` | Client (Hooks) |

## Implementation Plan

1. **ConflictValueSelector component** (NEW):
   - MUI `RadioGroup` rendering one `Radio` per conflict source with label showing the source data value
   - Additional radio option at bottom: "Both Valid — Different Dates" with `info.main` styling to distinguish it
   - On selection change, update local state with selectedExtractedDataId or "both-valid" flag
   - Radio design tokens: primary.500 (checked), neutral.400 (unchecked), 24×24 icon, 44px touch target
   - ARIA: `role="radiogroup"`, `aria-label="Select the correct data value"`, each option `role="radio"`
2. **BothValidDialog component** (NEW):
   - MUI `Dialog` opened when staff selects "Both Valid — Different Dates" radio and clicks confirm
   - Contains MUI `TextField` (multiline) for explanation text (min 10 chars)
   - "Confirm Both Valid" primary button and "Cancel" text button
   - On confirm, calls `useBothValid` mutation
   - On success, toast notification "Conflict resolved — both values preserved"
3. **Modify ConflictComparisonView**:
   - Wrap existing side-by-side `ConflictSourceCard` layout with `ConflictValueSelector`
   - Each source card gets a radio button overlay/integrated selector at the top
   - Maintain existing side-by-side layout per UXR-104
4. **Modify ConflictSourceCard** (AC-3 refinement):
   - Verify display includes: document name (bold), upload date (typography.caption), confidence score via `ConfidenceBadge` (success.main ≥80%, warning.main 60–79%, error.main <60%)
   - Add `aria-label` for each source card: "Source: {docName}, uploaded {date}, confidence {score}%"
5. **Modify ConflictResolutionForm**:
   - Replace existing "Resolve" / "Dismiss" buttons with:
     - "Save Selected Value" primary button (enabled when a radio option is selected) → calls `useSelectValue` mutation
     - "Both Valid — Different Dates" option triggers `BothValidDialog`
     - "Dismiss" secondary button remains for false-positive scenarios
   - Resolution notes `TextField` remains, passed as `resolutionNotes` to API
6. **ResolutionProgressIndicator component** (NEW):
   - MUI `LinearProgress` variant="determinate" showing percentage of conflicts resolved
   - Text below: "3 of 5 conflicts resolved" using typography.body2
   - Placed at top of ConflictResolutionModal, below modal title
   - Uses `useResolutionProgress` hook for data
   - Refreshes on each resolve/dismiss action via React Query invalidation
7. **VerificationStatusBanner component** (NEW):
   - MUI `Alert` severity="success" displayed at top of PatientProfile360Page when profile status is "Verified"
   - Content: "All conflicts resolved — Profile verified ✓" with verified-by name and timestamp
   - Hidden when verification_status is Unverified or PartiallyVerified
   - Uses `useVerificationStatus` hook for data
8. **Modify PatientProfile360Page**:
   - Add `VerificationStatusBanner` above existing content, conditionally rendered
   - Add `ResolutionProgressIndicator` inside conflict section when conflicts exist and not all resolved
   - Invalidate verification status query when conflict resolution completes
9. **React Query hooks** (NEW):
   - `useSelectValue`: `useMutation` calling `PUT /api/patients/{patientId}/conflicts/{conflictId}/select-value`. On success: invalidate `['conflicts', patientId]`, `['resolution-progress', patientId]`, `['verification-status', patientId]`
   - `useBothValid`: `useMutation` calling `PUT /api/patients/{patientId}/conflicts/{conflictId}/both-valid`. Same invalidation
   - `useResolutionProgress`: `useQuery` calling `GET /api/patients/{patientId}/conflicts/resolution-progress`
   - `useVerificationStatus`: `useQuery` calling `GET /api/patients/{patientId}/profile/verification-status`
10. **Auto-save on navigate away** (EC-1):
    - Use React Router `useBlocker` or `beforeunload` event to detect navigation away during active resolution
    - If a value is selected but not submitted, auto-submit the selection via `useSelectValue` before navigating
    - Show brief MUI `Snackbar` toast: "Progress saved — unresolved conflicts remain flagged"
    - Unresolved conflicts persist server-side (no special save needed — each resolve is atomic)

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
client/
  src/
    pages/
      PatientProfile360Page.tsx
    components/
      conflict/
        ConflictResolutionModal.tsx
        ConflictComparisonView.tsx
        ConflictSourceCard.tsx
        ConflictResolutionForm.tsx
        UrgentBadge.tsx
    hooks/
      conflict/
        useConflicts.ts
        useConflictDetail.ts
        useResolveConflict.ts
    api/
      conflictApi.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | client/src/components/conflict/ConflictValueSelector.tsx | RadioGroup for source value selection + "Both Valid" option with ARIA roles |
| CREATE | client/src/components/conflict/BothValidDialog.tsx | MUI Dialog for explanation text when both values are valid |
| CREATE | client/src/components/conflict/VerificationStatusBanner.tsx | MUI Alert showing profile verified status |
| CREATE | client/src/components/conflict/ResolutionProgressIndicator.tsx | MUI LinearProgress with resolved/total count text |
| CREATE | client/src/hooks/conflict/useSelectValue.ts | React Query useMutation for PUT select-value with query invalidation |
| CREATE | client/src/hooks/conflict/useBothValid.ts | React Query useMutation for PUT both-valid with query invalidation |
| CREATE | client/src/hooks/conflict/useResolutionProgress.ts | React Query useQuery for GET resolution-progress |
| CREATE | client/src/hooks/conflict/useVerificationStatus.ts | React Query useQuery for GET verification-status |
| MODIFY | client/src/components/conflict/ConflictComparisonView.tsx | Integrate ConflictValueSelector radio buttons within side-by-side layout |
| MODIFY | client/src/components/conflict/ConflictSourceCard.tsx | Verify document name, upload date, confidence score (AC-3) with ARIA labels |
| MODIFY | client/src/components/conflict/ConflictResolutionForm.tsx | Replace resolve/dismiss with "Save Selected Value" + "Both Valid" + dismiss |
| MODIFY | client/src/pages/PatientProfile360Page.tsx | Add VerificationStatusBanner, ResolutionProgressIndicator, auto-save on navigate away |
| MODIFY | client/src/api/conflictApi.ts | Add selectValue, bothValid, getResolutionProgress, getVerificationStatus API functions |

## External References

- [MUI RadioGroup](https://mui.com/material-ui/react-radio-button/) - Radio button group component
- [MUI Dialog](https://mui.com/material-ui/react-dialog/) - Modal dialog for Both Valid confirmation
- [MUI Alert](https://mui.com/material-ui/react-alert/) - Success alert for verification banner
- [MUI LinearProgress](https://mui.com/material-ui/react-progress/#linear-determinate) - Determinate progress bar
- [React Router useBlocker](https://reactrouter.com/en/main/hooks/use-blocker) - Navigation blocking for auto-save
- [TanStack Query Mutations](https://tanstack.com/query/v4/docs/react/guides/mutations) - useMutation for resolve operations

## Build Commands

- `cd client && npm run build`
- `cd client && npm run test -- --watch=false`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Visual regression tests pass (if applicable)
- [ ] Staff can select a specific source value via radio button and submit (AC-2)
- [ ] "Both Valid — Different Dates" radio option opens explanation dialog and preserves both values (EC-2)
- [ ] Side-by-side comparison displays source document name, upload date, and confidence score for each source (AC-1, AC-3)
- [ ] Resolution progress shows correct resolved/total counts and updates on each resolution (EC-1)
- [ ] Verification status banner appears when all conflicts are resolved (AC-4)
- [ ] Auto-save triggers on navigation away with unsaved selection (EC-1)
- [ ] All interactive elements meet 44px touch target and have ARIA labels
- [ ] Keyboard navigation works through radio group and form controls

## Implementation Checklist

- [X] Create ConflictValueSelector component with MUI RadioGroup, one radio per source value plus "Both Valid — Different Dates" option, ARIA radiogroup role, primary.500 checked styling (AC-2, EC-2)
- [X] Create BothValidDialog with MUI Dialog, explanation TextField (min 10 chars), confirm/cancel actions calling useBothValid mutation (EC-2)
- [X] Modify ConflictComparisonView to integrate ConflictValueSelector within the side-by-side layout, maintaining UXR-104 comparison flow (AC-1, AC-2)
- [X] Modify ConflictSourceCard to display document name, upload date, and ConfidenceBadge with ARIA labels for each source (AC-3)
- [X] Modify ConflictResolutionForm to replace resolve/dismiss with "Save Selected Value" primary button calling useSelectValue mutation, plus "Both Valid" trigger and dismiss option (AC-2, EC-2)
- [X] Create ResolutionProgressIndicator with MUI LinearProgress and "X of Y conflicts resolved" text, refresh on resolution via React Query invalidation (EC-1, AC-4)
- [X] Create VerificationStatusBanner with MUI Alert severity="success" showing verified-by name and timestamp, conditionally rendered on PatientProfile360Page (AC-4)
- [X] Create React Query hooks (useSelectValue, useBothValid, useResolutionProgress, useVerificationStatus) with proper query key invalidation on mutations
