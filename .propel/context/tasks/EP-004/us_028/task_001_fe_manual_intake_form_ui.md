# Task - task_001_fe_manual_intake_form_ui

## Requirement Reference

- User Story: US_028
- Story Location: .propel/context/tasks/EP-004/us_028/us_028.md
- Acceptance Criteria:
    - AC-1: Given I select "Manual Form" from intake options, When the form loads, Then it displays sections for Personal Information, Medical History, and Insurance with all mandatory and optional fields.
    - AC-2: Given AI intake has pre-filled some data, When the manual form loads, Then the pre-filled fields display the AI-collected data with visual indication of pre-filled status.
    - AC-3: Given I am filling out the form, When I leave a mandatory field empty and attempt to submit, Then inline validation highlights the missing field within 200ms with a descriptive error.
    - AC-4: Given I complete and submit the form, When submission succeeds, Then intake status is marked "completed" and staff is notified for review.
- Edge Case:
    - EC-1: If the patient navigates away mid-completion, the form must restore the last auto-saved draft on return.
    - EC-2: Browser back navigation during submission must not trigger duplicate submit or data loss.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-009-manual-intake.html |
| **Screen Spec** | figma_spec.md#SCR-009 |
| **UXR Requirements** | UXR-004, UXR-303, UXR-501 |
| **Design Tokens** | designsystem.md#Typography, designsystem.md#Spacing, designsystem.md#Validation, designsystem.md#Empty |

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

Implement the patient-facing manual intake form for SCR-009. This frontend task delivers FR-027, FR-029, FR-030, and the UI portion of FR-031 by rendering the structured intake sections, prefilled AI-carried values, autosave status, inline validation feedback, and submission flow in a traditional form-first experience. The form must remain responsive per UXR-303, autosave-safe per UXR-004, and provide inline validation within 200ms per UXR-501 while preserving a clear path back to AI intake when patients want to switch modes.

## Dependent Tasks

- task_002_be_manual_intake_api (Draft load, autosave, submit, and update APIs must exist before UI wiring)
- US_027 task_001_fe_ai_conversational_intake_ui (Switch-back navigation and prefilled AI handoff pattern should remain consistent)
- US_027 task_004_db_ai_intake_session_persistence (Shared `IntakeData` draft persistence and carryover fields must exist)
- US_013 task_001_fe_role_based_dashboard_router (Existing patient-shell routing and navigation patterns should be reused)

## Impacted Components

- **NEW** `ManualIntakePage` - Form-based intake page containing sectioned fields, autosave status, and submit flow (app/pages/)
- **NEW** `ManualIntakeForm` - Shared structured form component for personal info, medical history, and insurance sections (app/components/intake/)
- **NEW** `PrefilledFieldIndicator` - Visual badge or helper for AI-carried values that were prefilled into the manual form (app/components/intake/)
- **NEW** `useManualIntakeForm` - Hook for loading draft data, autosaving changes, submit/update behavior, and prefill handling (app/hooks/)
- **MODIFY** `AIIntakePage` - Keep the switch-to-manual handoff path consistent with the manual form route and autosave expectations (app/pages/)

## Implementation Plan

1. **Create `ManualIntakePage`** using the SCR-009 wireframe layout with breadcrumb navigation, autosave notice, sectioned form content, and primary submit action.
2. **Build `ManualIntakeForm`** with personal information, medical history, and insurance sections, keeping required and optional fields clearly separated.
3. **Add prefilled-field presentation** so AI-carried values are visually identified without blocking patient edits or implying that the values are locked.
4. **Implement `useManualIntakeForm`** to load the current draft, debounce autosave, handle draft restore on return, and manage submit or post-submit update flows.
5. **Enforce inline validation timing** so missing or malformed required fields are highlighted within 200ms on blur/change and again on submit.
6. **Prevent duplicate submission and back-button loss** by disabling repeat submit while the request is in flight and keeping the latest draft synchronized.
7. **Validate responsive and accessible behavior** across mobile and desktop layouts, including stacked-to-two-column transitions, keyboard navigation, and descriptive field errors.

## Current Project State

```text
app/
  pages/
    PatientDashboardPage.tsx
    AIIntakePage.tsx
  components/
    intake/
      IntakeChatMessageList.tsx
      IntakeProgressHeader.tsx
  hooks/
    useAIIntakeSession.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/pages/ManualIntakePage.tsx | SCR-009 manual form page with autosave, prefill, and submit states |
| CREATE | app/components/intake/ManualIntakeForm.tsx | Structured form sections for personal info, medical history, and insurance |
| CREATE | app/components/intake/PrefilledFieldIndicator.tsx | Visual indicator for AI-prefilled values in the manual form |
| CREATE | app/hooks/useManualIntakeForm.ts | Draft load, autosave, validation, and submit/update hook |
| MODIFY | app/pages/AIIntakePage.tsx | Preserve switch-to-manual navigation and carried-over draft expectations |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 TextField API: https://mui.com/material-ui/react-text-field/
- MUI 5 Select API: https://mui.com/material-ui/react-select/
- WCAG 2.1 quick reference: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [x] Build SCR-009 with personal information, medical history, and insurance sections matching the wireframe structure
- [x] Display AI-prefilled values with a clear visual indicator while keeping fields editable
- [x] Deliver inline validation feedback for required fields within 200ms and again on submit
- [x] Restore the last auto-saved draft when the patient returns after navigating away or timing out
- [x] Prevent duplicate submission and preserve draft state during browser back navigation or in-flight submission
- [x] Validate responsive layout, keyboard navigation, and descriptive accessible error messaging
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete