# Task - TASK_001

## Requirement Reference

- User Story: US_046
- Story Location: .propel/context/tasks/EP-007/us_046/us_046.md
- Acceptance Criteria:
  - AC-1: Given AI confidence for a consolidation operation is below 80%, When the result is generated, Then the system presents the data in a manual review form pre-filled with AI suggestions marked as "low-confidence."
  - AC-2: Given the system detects clinical event dates that violate chronological plausibility (e.g., procedure date before diagnosis date), When the validation runs, Then the conflicting dates are flagged with an explanation of the inconsistency.
  - AC-3: Given a staff member is in manual fallback mode, When they edit and confirm data entries, Then each confirmed entry is saved with "manual-verified" status and staff attribution.
  - AC-4: Given the AI service is completely unavailable, When a document is uploaded, Then the system displays a banner "AI unavailable — switch to manual" and provides the manual data entry form.
- Edge Case:
  - Partial date parsing: System saves partial date, flags as "incomplete-date," and presents for staff completion.
  - Timezone: Phase 1 assumes all dates in clinic's local timezone; timezone metadata is ignored.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-013-patient-profile-360.html |
| **Screen Spec** | figma_spec.md#SCR-013 |
| **UXR Requirements** | UXR-104, UXR-105, UXR-605, UXR-404, UXR-505, UXR-003 |
| **Design Tokens** | designsystem.md#colors (semantic: warning, error, success), designsystem.md#typography |

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
| Backend | N/A | N/A |
| Database | N/A | N/A |
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

Implement the frontend UI components for manual fallback workflow and date validation display on the Patient Profile 360 screen (SCR-013). This task delivers the manual review form that pre-fills low-confidence AI suggestions, the AI unavailable banner with manual entry fallback, date conflict flagging UI with explanations, and the edit/confirm workflow that saves entries with "manual-verified" status. The implementation follows the Hi-Fi wireframe for SCR-013 which shows confidence badges, conflict rows, tabs for clinical data categories, and alert banners.

## Dependent Tasks

- US_043 - Requires consolidation pipeline frontend integration (data display for 360 profile)
- US_008 - Requires ExtractedData entity types and interfaces in frontend

## Impacted Components

- `ManualReviewForm` — NEW component for pre-filled low-confidence data review form (app/src/features/clinical/)
- `AiUnavailableBanner` — NEW component for AI service down banner with manual switch CTA (app/src/features/clinical/)
- `DateConflictAlert` — NEW component for chronological plausibility violation display (app/src/features/clinical/)
- `ConfidenceBadge` — UPDATE existing badge component to support low-confidence "Needs Review" variant (app/src/components/)
- `PatientProfile360` — UPDATE to integrate manual fallback mode and date conflict alerts (app/src/pages/)
- `IncompleteDateBadge` — NEW component for partial date display with "incomplete-date" flag (app/src/components/)

## Implementation Plan

1. **Create ManualReviewForm component** — Build a form that receives pre-filled AI extraction data for entries with confidence < 80%. Display each field as editable with a "low-confidence" amber badge next to the pre-filled value. Use MUI `TextField` with `defaultValue` set to AI suggestions. Include a "Confirm" button per entry and a "Confirm All" bulk action.

2. **Add confidence badge variants** — Extend the ConfidenceBadge component to render color-coded indicators per the wireframe: green (>80%), amber (60-80%), red (<60%). Add a "Needs Review" text badge for items below 80% threshold per UXR-105.

3. **Implement date conflict flagging UI** — Create DateConflictAlert component that displays an MUI `Alert` (warning severity) when chronological plausibility violations are detected. Show the conflicting dates, the nature of the inconsistency (e.g., "Procedure date 2023-01-10 is before diagnosis date 2023-06-15"), and a link to the source documents.

4. **Build AI unavailable banner** — Create AiUnavailableBanner using MUI `Alert` (error severity) with text "AI unavailable — switch to manual" and a `Button` CTA that activates the manual data entry form. Render when the backend reports AI service health as unavailable.

5. **Implement manual data entry form** — Build a form with empty fields for medications, diagnoses, procedures, and allergies when AI is fully unavailable. Reuse the tab structure from the wireframe (Medications, Diagnoses, Procedures, Allergies). Each entry captures data_type fields and saves with "manual-verified" status.

6. **Add incomplete-date handling** — Create IncompleteDateBadge showing a warning chip "Incomplete Date" next to partial dates (month/year only). Provide an inline date picker for staff to complete the missing date components.

7. **Integrate manual fallback mode into PatientProfile360** — Add state management (Zustand store) to track `isManualFallbackMode` and `isAiUnavailable`. Toggle between normal view and manual review view based on API response confidence levels and AI health status. Connect React Query hooks to the backend endpoints for fetching low-confidence results and submitting manual verifications.

8. **Implement form validation and screen states** — Add client-side validation for manual entries (required fields, date format validation). Implement all 5 screen states per figma_spec.md: Default (normal profile view), Loading (skeleton loaders during data fetch), Empty (no clinical data message), Error (API failure with retry), Validation (field-level inline validation errors per NFR-048 < 200ms).

## Current Project State

- Project structure is placeholder; to be updated based on completion of dependent tasks (US_043, US_008).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/features/clinical/ManualReviewForm.tsx | Manual review form with pre-filled low-confidence AI suggestions |
| CREATE | app/src/features/clinical/AiUnavailableBanner.tsx | Banner for AI service unavailability with manual switch CTA |
| CREATE | app/src/features/clinical/DateConflictAlert.tsx | Alert component for chronological date plausibility violations |
| CREATE | app/src/components/IncompleteDateBadge.tsx | Badge for partial dates with inline completion picker |
| MODIFY | app/src/components/ConfidenceBadge.tsx | Add low-confidence "Needs Review" variant and color thresholds |
| MODIFY | app/src/pages/PatientProfile360.tsx | Integrate manual fallback mode, AI unavailable state, date conflict alerts |
| CREATE | app/src/stores/manualFallbackStore.ts | Zustand store for manual fallback mode state management |
| CREATE | app/src/hooks/useManualFallback.ts | React Query hooks for fetching low-confidence results and submitting manual verifications |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Alert component: https://mui.com/material-ui/react-alert/
- MUI 5 TextField component: https://mui.com/material-ui/react-text-field/
- MUI 5 Badge component: https://mui.com/material-ui/react-badge/
- MUI 5 Tabs component: https://mui.com/material-ui/react-tabs/
- React Query v4 documentation: https://tanstack.com/query/v4/docs/overview
- Zustand v4 documentation: https://docs.pmnd.rs/zustand/getting-started/introduction
- WCAG 2.1 AA Color Contrast: https://www.w3.org/WAI/WCAG21/Understanding/contrast-minimum.html

## Build Commands

- Refer to applicable technology stack specific build commands in .propel/build/

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] ConfidenceBadge renders green/amber/red based on score thresholds
- [ ] ManualReviewForm pre-fills AI suggestions and marks low-confidence entries
- [ ] AiUnavailableBanner renders when AI health check reports unavailable
- [ ] DateConflictAlert displays chronological inconsistency explanation
- [ ] IncompleteDateBadge renders for partial dates with completion prompt
- [ ] All 5 screen states (Default, Loading, Empty, Error, Validation) implemented
- [ ] Keyboard navigation works for all interactive elements (NFR-049)
- [ ] WCAG 2.1 AA color contrast met for all confidence badges and alerts (NFR-046)

## Implementation Checklist

- [ ] Create ManualReviewForm component with pre-filled AI suggestions and "low-confidence" amber badges
- [ ] Add confidence badge variants: green (>80%), amber (60-80%), red (<60%) with "Needs Review" label
- [ ] Implement DateConflictAlert component with chronological plausibility explanation display
- [ ] Build AiUnavailableBanner with "AI unavailable — switch to manual" text and manual entry CTA
- [ ] Implement manual data entry form with empty fields for all clinical data categories (tabs)
- [ ] Add IncompleteDateBadge with inline date picker for partial date completion
- [ ] Integrate manual fallback mode into PatientProfile360 with Zustand state and React Query hooks
- [ ] Implement form validation and all 5 screen states (Default, Loading, Empty, Error, Validation)
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
