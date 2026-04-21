# Task - task_001_fe_no_show_risk_display

## Requirement Reference

- User Story: US_026
- Story Location: .propel/context/tasks/EP-003/us_026/us_026.md
- Acceptance Criteria:
    - AC-2: Given risk scores are available, When staff views the appointment list, Then each appointment displays its risk score with color coding (green <30, amber 30-69, red >=70).
    - AC-3: Given insufficient historical data (new patient, <3 appointments), When the risk score is calculated, Then the system uses rule-based defaults and displays "Estimated" label.
- Edge Case:
    - EC-1: When a patient's risk score changes after a completed visit, the updated score must appear on the next staff refresh cycle without manual recalculation in the browser.
    - EC-2: A patient with a capped score of 100 must render the highest-risk visual state and supporting outreach indicator without overflowing the layout.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-010-staff-dashboard.html; .propel/context/wireframes/Hi-Fi/wireframe-SCR-011-arrival-queue.html |
| **Screen Spec** | figma_spec.md#SCR-010, figma_spec.md#SCR-011 |
| **UXR Requirements** | UXR-105, UXR-403 |
| **Design Tokens** | designsystem.md#Secondary Colors, designsystem.md#AI Confidence Colors, designsystem.md#Badge, designsystem.md#Table |

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

Implement staff-facing no-show risk score presentation on the appointment list surfaces for SCR-010 and SCR-011. This frontend task delivers the display portion of FR-014 by adding a color-coded risk indicator, score value, and "Estimated" state to schedule and queue rows so staff can quickly identify high-risk appointments. The UI must preserve the staff portal visual treatment from UXR-403, reuse the confidence-style color semantics from UXR-105 where appropriate, and remain accessible and responsive per NFR-046 and NFR-047.

## Dependent Tasks

- task_004_be_no_show_risk_integration_api (Staff-facing endpoints must supply score, band, and estimated-state metadata)
- US_057 - Cross-Epic - Staff dashboard schedule surface must exist for SCR-010 row integration
- US_053 - Cross-Epic - Arrival queue dashboard surface must exist for SCR-011 row integration
- US_022 task_001_fe_walkin_registration_ui (Staff dashboard shell and secondary-accent visual treatment should be reused)

## Impacted Components

- **NEW** `NoShowRiskBadge` - Shared badge or pill component for score value, color band, and estimated label (app/components/staff/)
- **NEW** `useNoShowRiskLegend` or equivalent helper - Shared mapping between score ranges and staff-facing visual treatment (app/hooks/ or app/utils/)
- **MODIFY** `StaffDashboardPage` - Show no-show risk score in the daily schedule list on SCR-010 (app/pages/)
- **MODIFY** `ArrivalQueuePage` - Show no-show risk score and high-risk emphasis in the queue table on SCR-011 (app/pages/)
- **MODIFY** shared staff appointment row or table components - Reuse score rendering across schedule and queue surfaces (app/components/staff/)

## Implementation Plan

1. **Create `NoShowRiskBadge`** to render the numeric score, green/amber/red banding, and an explicit "Estimated" label when historical data is insufficient.
2. **Add the score indicator to SCR-010** so daily schedule rows include risk visibility without disrupting existing patient, time, type, and status columns.
3. **Add the score indicator to SCR-011** so queue rows and high-risk appointments are scannable during arrival management workflows.
4. **Reuse a shared range-to-style helper** so score colors stay consistent across staff surfaces and align with the design-system confidence palette.
5. **Support loading and empty states** by rendering placeholder or hidden score states until backend data arrives, rather than fabricating client-side values.
6. **Surface outreach cues for extreme risk** by visually distinguishing capped high-risk scores and estimated results without introducing alert fatigue.
7. **Validate responsive and accessible behavior** for table density, badge contrast, screen-reader labels, and secondary-accent staff styling across required breakpoints.

## Current Project State

```text
app/
  components/
    staff/
      WalkInRegistrationModal.tsx
  pages/
    StaffDashboardPage.tsx
    ArrivalQueuePage.tsx
  hooks/
    useWalkInRegistration.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/components/staff/NoShowRiskBadge.tsx | Shared staff risk score indicator with color band and estimated state |
| CREATE | app/utils/noShowRiskDisplay.ts | Shared score-range mapping and label helpers for staff surfaces |
| MODIFY | app/pages/StaffDashboardPage.tsx | Add no-show risk score column or inline indicator to daily schedule rows |
| MODIFY | app/pages/ArrivalQueuePage.tsx | Add no-show risk score indicator to queue rows and urgent scanning states |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Chip API: https://mui.com/material-ui/react-chip/
- MUI 5 Table API: https://mui.com/material-ui/react-table/
- WCAG 2.1 quick reference: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [x] Build a shared no-show risk badge that renders numeric score, color band, and estimated state consistently
- [x] Show the risk score on SCR-010 daily schedule rows without breaking existing staff dashboard layout
- [x] Show the risk score on SCR-011 queue rows so staff can identify high-risk appointments during queue management
- [x] Reuse one shared score-to-style mapping across all staff appointment list surfaces
- [x] Distinguish estimated scores and capped high-risk scores accessibly for staff outreach decisions
- [x] Validate contrast, responsive table behavior, and screen-reader labels for all score states
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete