# Task - task_001_fe_intake_autosave_experience

## Requirement Reference

- User Story: US_030
- Story Location: .propel/context/tasks/EP-004/us_030/us_030.md
- Acceptance Criteria:
    - AC-1: Given I am filling intake (AI or manual), When 30 seconds elapse since the last change, Then the system saves current progress to the server and displays "Auto-saved" indicator.
    - AC-2: Given my session is interrupted (timeout, network loss), When I return to the intake, Then the system restores the last auto-saved state with all fields populated.
    - AC-3: Given the auto-save triggers, When the save completes, Then the auto-save indicator briefly appears and fades, confirming the save without disrupting the user flow.
- Edge Case:
    - EC-1: If auto-save fails due to network error, retry once after 5 seconds; if the retry also fails, display "Save failed - your responses are cached locally."
    - EC-2: If rapid changes happen within 30 seconds, only the state present at the 30-second save boundary should be persisted to the server.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-008-ai-intake.html; .propel/context/wireframes/Hi-Fi/wireframe-SCR-009-manual-intake.html |
| **Screen Spec** | figma_spec.md#SCR-008, figma_spec.md#SCR-009 |
| **UXR Requirements** | UXR-004 |
| **Design Tokens** | designsystem.md#Typography, designsystem.md#Spacing, designsystem.md#Progress, designsystem.md#Validation |

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

Implement the shared autosave experience for both intake surfaces. This frontend task delivers the UI portion of FR-035 by adding a common autosave timer, success indicator, retry feedback, and local-cache fallback behavior to the conversational intake and manual form flows. The experience must preserve the current user interaction, avoid persisting every intermediate keystroke, and restore the last server-saved or locally cached state when the patient returns after interruption.

## Dependent Tasks

- US_027 task_001_fe_ai_conversational_intake_ui (AI intake UI and progress header must exist)
- US_028 task_001_fe_manual_intake_form_ui (Manual intake UI and draft form must exist)
- task_002_be_intake_autosave_restore_api (Autosave and restore contract must exist before UI wiring)
- US_027 task_004_db_ai_intake_session_persistence (Shared autosave persistence must exist)

## Impacted Components

- **MODIFY** `useAIIntakeSession` - Add 30-second autosave scheduling, retry behavior, restore hydration, and transient autosave status state (app/hooks/)
- **MODIFY** `useManualIntakeForm` - Add shared autosave cadence, retry-once behavior, local-cache fallback, and restore hydration (app/hooks/)
- **MODIFY** `IntakeProgressHeader` - Present transient "Auto-saved" confirmation and last-save freshness without disrupting progress visibility (app/components/intake/)
- **MODIFY** `ManualIntakePage` - Surface autosave success or failure messaging consistently with the wireframe alert pattern (app/pages/)
- **NEW** `useIntakeAutosave` - Shared hook for debounced change tracking, 30-second boundary saves, single retry, and local cache fallback (app/hooks/)

## Implementation Plan

1. **Extract shared autosave behavior** into a reusable hook so both AI and manual intake use the same 30-second scheduling, retry, and restore rules.
2. **Track dirty state and save boundaries** so only the latest state at each 30-second mark is sent to the server rather than every intermediate field change.
3. **Show transient success feedback** by surfacing an "Auto-saved" indicator that briefly appears and fades after a successful save while preserving the surrounding intake layout.
4. **Retry one failed save after 5 seconds** and, on a second failure, cache the current draft locally and display the exact fallback message required by the story.
5. **Restore state on return** by preferring the last server-saved draft and then reconciling any newer local-cache draft when the interruption was caused by network loss.
6. **Integrate the shared status UI** into both SCR-008 and SCR-009 without duplicating timers or save state logic in page components.
7. **Validate non-disruptive behavior** across typing, network failure, and timeout-return scenarios so autosave never steals focus or interrupts the patient flow.

## Current Project State

```text
app/
  pages/
    AIIntakePage.tsx
    ManualIntakePage.tsx
  components/
    intake/
      IntakeProgressHeader.tsx
      ManualIntakeForm.tsx
  hooks/
    useAIIntakeSession.ts
    useManualIntakeForm.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/hooks/useIntakeAutosave.ts | Shared 30-second autosave, retry, restore, and local-cache fallback hook |
| MODIFY | app/hooks/useAIIntakeSession.ts | Reuse shared autosave behavior for conversational intake drafts |
| MODIFY | app/hooks/useManualIntakeForm.ts | Reuse shared autosave behavior for manual intake drafts |
| MODIFY | app/components/intake/IntakeProgressHeader.tsx | Show transient autosave confirmation and last-save freshness state |
| MODIFY | app/pages/ManualIntakePage.tsx | Show autosave failure fallback messaging and restore feedback consistently |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Alert API: https://mui.com/material-ui/react-alert/
- MUI 5 Snackbar API: https://mui.com/material-ui/react-snackbar/
- Web Storage API: https://developer.mozilla.org/en-US/docs/Web/API/Web_Storage_API

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [x] Save the latest intake state at each 30-second boundary for both AI and manual modes
- [x] Show a brief "Auto-saved" confirmation without interrupting typing or navigation
- [x] Retry one failed autosave after 5 seconds and then fall back to local cache with the required error message
- [x] Restore the last saved intake state after timeout, reload, or network interruption
- [x] Avoid persisting every intermediate edit between autosave boundaries
- [x] Keep focus, typing flow, and responsive layout stable while autosave runs in the background
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete