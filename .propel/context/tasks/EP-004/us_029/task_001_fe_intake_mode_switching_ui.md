# Task - task_001_fe_intake_mode_switching_ui

## Requirement Reference

- User Story: US_029
- Story Location: .propel/context/tasks/EP-004/us_029/us_029.md
- Acceptance Criteria:
    - AC-1: Given I am in AI intake mode, When I click "Switch to Manual Form", Then the manual form loads with all previously collected data pre-filled.
    - AC-2: Given I am in manual form mode, When I click "Switch to AI Intake", Then the AI chat resumes with context of previously entered data and continues from the next uncollected field.
    - AC-3: Given I switch modes, When the transition completes, Then no data is lost and all previously provided answers are preserved.
    - AC-4: Given I switch modes multiple times, When I eventually submit, Then the final intake contains the merged data from all modes with correct source attribution.
- Edge Case:
    - EC-1: If conflicting data exists between AI and manual entries, the UI must show that the most recent entry is active while preserving a visible conflict note in the intake review state.
    - EC-2: If AI service is unavailable, the switch-to-AI action must be disabled and display the alert "AI intake temporarily unavailable."

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-008-ai-intake.html; .propel/context/wireframes/Hi-Fi/wireframe-SCR-009-manual-intake.html |
| **Screen Spec** | figma_spec.md#SCR-008, figma_spec.md#SCR-009 |
| **UXR Requirements** | UXR-004, UXR-303, UXR-605 |
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

Implement the patient-facing mode-switching experience across SCR-008 and SCR-009. This frontend task delivers FR-028 and the UI portion of AIR-008 by allowing patients to move between conversational intake and the manual form without losing draft data, preserving progress state, and presenting the currently authoritative answer when manual and AI values conflict. The UI must keep switch actions visible, transfer patients into the correct next step after each switch, and clearly communicate when AI intake is unavailable so the manual path remains the safe fallback.

## Dependent Tasks

- US_027 task_001_fe_ai_conversational_intake_ui (AI intake page and chat-state UI must exist)
- US_028 task_001_fe_manual_intake_form_ui (Manual intake form and prefill rendering must exist)
- task_002_be_intake_mode_switching_api (Switch orchestration, merge metadata, and availability contract must exist before UI wiring)
- task_003_db_intake_mode_switching_attribution (Mode-switch provenance and conflict metadata persistence must exist)

## Impacted Components

- **MODIFY** `AIIntakePage` - Surface switch-to-manual and resume-from-manual behavior with preserved progress, disabled-AI fallback, and conflict-aware summary state (app/pages/)
- **MODIFY** `ManualIntakePage` - Surface switch-to-AI behavior, preserved draft state, and resume metadata for the next uncollected field (app/pages/)
- **NEW** `IntakeModeSwitchBanner` - Shared UI banner for switch status, AI unavailability messaging, and recovered draft context (app/components/intake/)
- **NEW** `IntakeConflictNotice` - Review-state indicator showing most-recent-value precedence with preserved source attribution note (app/components/intake/)
- **NEW** `useIntakeModeSwitch` - Shared hook for loading mode-switch availability, executing switches, hydrating returned state, and preventing duplicate navigation (app/hooks/)

## Implementation Plan

1. **Add a shared switch hook** that invokes the mode-switch API, hydrates the returned draft state, and routes patients to the target intake surface without resetting progress.
2. **Update `AIIntakePage`** so the switch-to-manual action remains visible, preserves current progress, and transitions into the manual form with all collected values prefilled.
3. **Update `ManualIntakePage`** so the switch-to-AI action resumes the chat with carried-over manual values and highlights the next uncollected field in the progress state.
4. **Render an AI-unavailable state** that disables switch-to-AI and shows the exact fallback alert text required by the story and UXR-605.
5. **Add conflict-aware review UI** so when AI and manual values differ, the most recent value is shown as active while the alternate source remains visible in a non-blocking note.
6. **Prevent duplicate switches and state loss** by disabling repeat switch actions during in-flight transitions and preserving autosave indicators across route changes.
7. **Validate accessibility and responsiveness** for both intake surfaces, including focus restoration after switch, keyboard access to toggle actions, and mobile-to-desktop layout integrity.

## Current Project State

```text
app/
  pages/
    AIIntakePage.tsx
    ManualIntakePage.tsx
  components/
    intake/
      IntakeChatMessageList.tsx
      IntakeProgressHeader.tsx
      ManualIntakeForm.tsx
      PrefilledFieldIndicator.tsx
  hooks/
    useAIIntakeSession.ts
    useManualIntakeForm.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | app/pages/AIIntakePage.tsx | Add switch-to-manual flow, disabled-AI handling, and conflict-aware resume behavior |
| MODIFY | app/pages/ManualIntakePage.tsx | Add switch-to-AI flow, resume metadata rendering, and conflict notice integration |
| CREATE | app/components/intake/IntakeModeSwitchBanner.tsx | Shared switch-status and AI-unavailable messaging component |
| CREATE | app/components/intake/IntakeConflictNotice.tsx | UI indicator for most-recent-value precedence and source attribution note |
| CREATE | app/hooks/useIntakeModeSwitch.ts | Shared hook for executing mode transitions and hydrating returned intake state |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Alert API: https://mui.com/material-ui/react-alert/
- MUI 5 Progress API: https://mui.com/material-ui/react-progress/
- WCAG 2.1 quick reference: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [ ] Keep switch actions available on both intake surfaces and preserve draft state across transitions
- [ ] Load manual intake with AI-collected values prefilled after switching from SCR-008
- [ ] Resume AI intake at the next uncollected field after switching from SCR-009
- [ ] Disable switch-to-AI when the service is unavailable and display the required fallback alert text
- [ ] Show most-recent-value precedence with source attribution when AI and manual values conflict
- [ ] Prevent duplicate switches and restore focus predictably after each route transition
- [ ] Validate responsive layout, keyboard navigation, and wireframe alignment for both intake screens
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete