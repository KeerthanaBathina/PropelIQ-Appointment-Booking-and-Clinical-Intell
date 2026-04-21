# Task - task_001_fe_minor_guardian_and_insurance_status_ui

## Requirement Reference

- User Story: US_031
- Story Location: .propel/context/tasks/EP-004/us_031/us_031.md
- Acceptance Criteria:
    - AC-1: Given a patient's DOB indicates they are under 18, When they attempt to book, Then the system requires guardian consent acknowledgment before proceeding.
    - AC-2: Given insurance details are provided during intake, When the soft pre-check runs, Then the system validates against dummy records and displays "Valid" or "Needs Review" status.
    - AC-4: Given the insurance check runs, When it completes, Then the result is displayed inline to the patient with a clear explanation if review is needed.
- Edge Case:
    - EC-1: If a minor's guardian is also a minor, the UI must block completion and clearly indicate that guardian age must be 18 or older.
    - EC-2: If insurance information is missing, the pre-check is skipped and the UI explains that staff will collect insurance during the visit.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-009-manual-intake.html |
| **Screen Spec** | figma_spec.md#SCR-009 |
| **UXR Requirements** | UXR-501 |
| **Design Tokens** | designsystem.md#Typography, designsystem.md#Spacing, designsystem.md#Validation, designsystem.md#colors |

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

Implement the patient-facing intake UI extensions for minor consent and soft insurance validation. This frontend task extends SCR-009 to conditionally collect guardian consent details when the patient is under 18, surface inline insurance pre-check results as "Valid" or "Needs Review," and explain when insurance is missing and will be collected during the visit. The UI should support booking-readiness gating by making incomplete guardian consent visible before the patient proceeds, while keeping validation feedback inline and non-disruptive.

## Dependent Tasks

- US_028 task_001_fe_manual_intake_form_ui (Manual intake sections and validation patterns must exist)
- US_030 task_001_fe_intake_autosave_experience (Autosave state handling should cover the new guardian and insurance fields)
- task_002_be_minor_guardian_and_insurance_precheck_api (Guardian-consent validation and insurance status contract must exist before UI wiring)
- task_003_db_minor_guardian_and_insurance_persistence (Guardian consent and insurance result fields must exist)

## Impacted Components

- **MODIFY** `ManualIntakeForm` - Add conditional guardian consent section, guardian-age validation, and insurance status presentation (app/components/intake/)
- **MODIFY** `ManualIntakePage` - Surface booking-readiness messaging and inline insurance review explanation (app/pages/)
- **MODIFY** `useManualIntakeForm` - Manage guardian consent fields, insurance pre-check trigger state, and result hydration (app/hooks/)
- **NEW** `InsurancePrecheckStatusBadge` - Inline status component for "Valid" and "Needs Review" with explanation text (app/components/intake/)
- **MODIFY** `BookingConfirmationModal` - Block final confirmation when backend reports missing required guardian consent for a minor booking attempt (app/components/appointments/)

## Implementation Plan

1. **Extend the manual intake form** with a conditional guardian consent section that appears when the patient DOB indicates age under 18.
2. **Capture guardian eligibility details** so the UI can enforce guardian age >= 18 and prevent invalid minor-plus-minor consent states inline.
3. **Trigger and render insurance pre-check results** after insurance details are entered or restored, showing "Valid" or "Needs Review" inline with explanatory copy.
4. **Show skipped-precheck messaging** when insurance details are absent, clarifying that staff will collect insurance during the visit.
5. **Propagate booking-readiness state** so the patient sees that minor bookings cannot proceed until guardian consent acknowledgment is complete.
6. **Gate booking confirmation UI** by surfacing the minor-consent blocker if the patient reaches booking without satisfying the guardian requirement.
7. **Keep validation accessible and immediate** by reusing inline error styling and explanatory helper text for guardian and insurance states.

## Current Project State

```text
app/
  pages/
    ManualIntakePage.tsx
  components/
    intake/
      ManualIntakeForm.tsx
      PrefilledFieldIndicator.tsx
    appointments/
      BookingConfirmationModal.tsx
  hooks/
    useManualIntakeForm.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | app/components/intake/ManualIntakeForm.tsx | Add guardian consent fields, guardian-age validation, and insurance status display |
| MODIFY | app/pages/ManualIntakePage.tsx | Surface booking-readiness and insurance review messaging |
| MODIFY | app/hooks/useManualIntakeForm.ts | Load, validate, and submit guardian consent plus insurance pre-check state |
| CREATE | app/components/intake/InsurancePrecheckStatusBadge.tsx | Inline insurance validation status and explanation component |
| MODIFY | app/components/appointments/BookingConfirmationModal.tsx | Show minor booking blocker when guardian consent is missing |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Alert API: https://mui.com/material-ui/react-alert/
- MUI 5 Checkbox API: https://mui.com/material-ui/react-checkbox/
- WCAG 2.1 quick reference: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [x] Show a guardian consent section only when patient DOB indicates age under 18
- [x] Block invalid guardian entries when the guardian is also under 18
- [x] Display inline insurance pre-check status as "Valid" or "Needs Review" with explanation text
- [x] Explain when insurance was skipped and staff will collect it during the visit
- [x] Surface booking-readiness feedback when guardian consent is still required for a minor
- [x] Keep guardian and insurance validation inline and accessible per existing SCR-009 patterns
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete