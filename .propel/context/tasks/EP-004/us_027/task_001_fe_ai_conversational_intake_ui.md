# Task - task_001_fe_ai_conversational_intake_ui

## Requirement Reference

- User Story: US_027
- Story Location: .propel/context/tasks/EP-004/us_027/us_027.md
- Acceptance Criteria:
    - AC-1: Given I select "AI-Assisted Intake" from my dashboard, When the chat interface loads, Then the AI greets me and explains the intake process with a progress indicator.
    - AC-2: Given the AI asks a question, When I provide a response, Then the AI validates my answer, asks clarifying follow-ups if needed, and moves to the next question within 1 second.
    - AC-4: Given the intake session is active, When the AI displays a summary, Then I can review all collected information and correct any errors before submission.
    - AC-5: Given the conversation progresses, When I look at the progress bar, Then it accurately reflects the number of fields collected out of total required (e.g., "4/8 fields").
- Edge Case:
    - EC-1: If the patient provides ambiguous medical terminology, the UI must clearly surface AI clarification prompts with example guidance.
    - EC-2: If session timeout occurs, the restored session must resume from the last auto-saved step and present the recovered progress state clearly.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-008-ai-intake.html |
| **Screen Spec** | figma_spec.md#SCR-008 |
| **UXR Requirements** | UXR-004, UXR-504, UXR-605 |
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

Implement the patient-facing AI conversational intake interface for SCR-008. This frontend task delivers the chat experience for FR-026, FR-029, and FR-030 by rendering the greeting, conversational message stream, progress indicator, autosave status, summary review state, and manual-form escape hatch in one guided intake flow. The UI must keep AI responses and follow-up prompts readable and reassuring, respect autosave and timeout recovery requirements from UXR-004, and preserve accessible patient-facing behavior across desktop and mobile breakpoints.

## Dependent Tasks

- task_003_be_ai_intake_session_api (Session lifecycle, message exchange, autosave, and summary APIs must exist before UI wiring)
- US_028 - Same-Epic - Manual intake form route and shared data contract must exist for the switch-without-data-loss path
- US_013 task_001_fe_role_based_dashboard_router (Existing patient-shell routing and dashboard navigation patterns should be reused)

## Impacted Components

- **NEW** `AIIntakePage` - Chat-style intake screen containing greeting, conversation log, progress bar, autosave indicator, and summary state (app/pages/)
- **NEW** `IntakeChatMessageList` - Conversation transcript renderer for AI and patient messages with clarification states (app/components/intake/)
- **NEW** `IntakeProgressHeader` - Progress bar, autosave timestamp, and session status presentation (app/components/intake/)
- **NEW** `useAIIntakeSession` - Hook coordinating session start, resume, message send, and summary/complete actions (app/hooks/)
- **MODIFY** `PatientDashboardPage` - Add or wire the AI-Assisted Intake launch action to SCR-008 (app/pages/)

## Implementation Plan

1. **Create `AIIntakePage`** using the SCR-008 wireframe layout with breadcrumb navigation, chat header, progress header, transcript body, and input composer.
2. **Build a shared message-list component** that renders AI prompts, patient replies, clarification examples, typing/loading states, and resumed-session history.
3. **Add `IntakeProgressHeader`** to display required-field progress, autosave freshness, and active session state without duplicating progress calculations in multiple components.
4. **Implement `useAIIntakeSession`** to initialize or resume the session, send patient responses, receive next-question payloads, and drive the summary-review and completion states.
5. **Support correction and review** by rendering the collected-information summary with inline edit or jump-back affordances before final submission.
6. **Keep the manual-form escape hatch visible** at all times and preserve session data when switching to SCR-009, rather than resetting the intake flow.
7. **Validate timeout recovery, responsiveness, and accessibility** for keyboard input, live-region updates, focus restoration, and loading/error states at 375px, 768px, and 1440px.

## Current Project State

```text
app/
  pages/
    PatientDashboardPage.tsx
  components/
    appointments/
      BookingSuccessView.tsx
  hooks/
    useBookAppointment.ts
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/pages/AIIntakePage.tsx | SCR-008 conversational intake page with progress, transcript, and summary states |
| CREATE | app/components/intake/IntakeChatMessageList.tsx | Chat transcript rendering for AI prompts, patient responses, and clarification states |
| CREATE | app/components/intake/IntakeProgressHeader.tsx | Shared progress and autosave status header for AI intake |
| CREATE | app/hooks/useAIIntakeSession.ts | Session-management hook for start, resume, message send, and complete flows |
| MODIFY | app/pages/PatientDashboardPage.tsx | Add or wire AI-Assisted Intake launch action to SCR-008 |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Progress API: https://mui.com/material-ui/react-progress/
- WAI-ARIA log role guidance: https://www.w3.org/WAI/ARIA/apg/patterns/log/
- WCAG 2.1 quick reference: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [x] Build SCR-008 with greeting, transcript, progress bar, autosave status, and input composer states
- [x] Render AI clarification prompts and patient responses clearly in the conversation log
- [x] Keep the required-field progress indicator accurate as the conversation advances
- [x] Support summary review and correction before final submission
- [x] Preserve data when switching from AI intake to manual intake
- [x] Validate timeout recovery, focus management, responsive layout, and accessible live updates
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation ✅
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete ✅