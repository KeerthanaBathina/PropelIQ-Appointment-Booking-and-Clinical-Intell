# Task - task_001_fe_appointment_rescheduling_ui

## Requirement Reference

- User Story: US_023
- Story Location: .propel/context/tasks/EP-003/us_023/us_023.md
- Acceptance Criteria:
    - AC-1: Given I have a scheduled appointment, When I select "Reschedule" and choose a new slot, Then the old slot is released and the new appointment is created atomically within 2 seconds.
    - AC-2: Given the new appointment time is within 24 hours of the original, When I attempt to reschedule, Then the system rejects with "Cannot reschedule within 24 hours of appointment."
    - AC-3: Given rescheduling succeeds, When the confirmation displays, Then it shows both the original and new appointment times with a success message.
- Edge Case:
    - EC-1: If the new slot becomes unavailable during confirmation, the UI must surface "Slot no longer available" and refresh alternative options.
    - EC-2: Walk-in appointments cannot be rescheduled by patients and must render a disabled or blocked reschedule affordance with backend-driven messaging.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-005-patient-dashboard.html |
| **Screen Spec** | figma_spec.md#SCR-005, figma_spec.md#SCR-006 |
| **UXR Requirements** | UXR-102, UXR-201, UXR-202, UXR-301, UXR-401, UXR-502, UXR-601 |
| **Design Tokens** | designsystem.md#Color Palette (appointment-status, primary, semantic), designsystem.md#Typography, designsystem.md#Spacing |

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

Implement the patient-facing appointment rescheduling experience across the patient dashboard and appointment booking flows. This frontend task delivers the UI layer for FR-023 by adding a reschedule entry point for eligible scheduled appointments, launching the existing slot-selection flow in reschedule mode, enforcing backend-provided 24-hour and walk-in restrictions, and showing a confirmation state with both original and new appointment times after success. The UI must preserve responsive, accessible behavior per NFR-046 and NFR-047, reuse existing booking/conflict patterns where possible, and keep status colors aligned with UXR-401.

## Dependent Tasks

- task_002_be_appointment_rescheduling_api (Atomic reschedule endpoint and policy evaluation must exist)
- US_017 task_001_fe_appointment_slot_viewing (Existing slot browsing UI should be reused for selecting a replacement slot)
- US_018 task_001_fe_appointment_booking_confirmation (Existing slot confirmation and conflict handling patterns should be reused)
- US_019 task_001_fe_appointment_cancellation_ui (Restriction and appointment-action affordance patterns should stay consistent on dashboard surfaces)

## Impacted Components

- **NEW** `RescheduleAppointmentDialog` - Shared dialog or drawer that summarizes original appointment details and launches replacement-slot selection (app/components/appointments/)
- **NEW** `RescheduleSuccessNotice` - Success state showing original and new appointment times plus confirmation copy (app/components/appointments/)
- **NEW** `useRescheduleAppointment` - React Query mutation hook for reschedule requests (app/hooks/)
- **MODIFY** `PatientDashboardPage` - Add reschedule action for eligible appointments and reflect updated appointment details after success (app/pages/)
- **MODIFY** `AppointmentBookingPage` - Support a reschedule mode that preloads the current appointment and reuses slot-selection UX for replacement booking (app/pages/)
- **MODIFY** `BookingConfirmationModal` or conflict modal components - Reuse success/conflict states for reschedule confirmation and slot-unavailable fallback (app/components/appointments/)

## Implementation Plan

1. **Add a reschedule action to eligible appointments** on SCR-005 and any shared patient appointment surfaces, excluding walk-in appointments and appointments blocked by the backend’s 24-hour rule.
2. **Create `RescheduleAppointmentDialog`** to present the current appointment context, explain the rescheduling flow, and transition the user into slot selection without forcing a cancel-and-rebook mental model.
3. **Reuse the existing slot browser in reschedule mode** so the patient can choose a replacement slot with the same availability and provider filters as booking, while keeping the original appointment context visible.
4. **Implement `useRescheduleAppointment`** to submit the old appointment and selected new slot in one request, then normalize success, 24-hour rejection, walk-in restriction, and slot-conflict outcomes for UI display.
5. **Render a reschedule confirmation state** that shows both original and new appointment times, updated success messaging, and refreshed appointment details on the dashboard after a successful atomic swap.
6. **Handle slot conflicts and restricted scenarios** by showing the exact required 24-hour message, surfacing slot-unavailable conflicts with refreshed options, and preventing patient-side rescheduling of walk-in appointments.
7. **Validate accessibility and responsive behavior** for dialog focus management, keyboard navigation, loading states, and state transitions across 375px, 768px, and 1440px breakpoints.

## Current Project State

```text
app/
  components/
    appointments/
      SlotCalendar.tsx
      TimeSlotGrid.tsx
      BookingConfirmationModal.tsx
      SlotConflictModal.tsx
      BookingSuccessView.tsx
      CancelAppointmentDialog.tsx
  hooks/
    useAppointmentSlots.ts
    useBookAppointment.ts
    useCancelAppointment.ts
  pages/
    AppointmentBookingPage.tsx
    PatientDashboardPage.tsx
    AppointmentHistoryPage.tsx
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/components/appointments/RescheduleAppointmentDialog.tsx | Entry dialog for reschedule flow with current appointment context |
| CREATE | app/components/appointments/RescheduleSuccessNotice.tsx | Success state showing original vs new appointment details |
| CREATE | app/hooks/useRescheduleAppointment.ts | Mutation hook for atomic reschedule requests |
| MODIFY | app/pages/PatientDashboardPage.tsx | Add reschedule action and in-place appointment refresh after success |
| MODIFY | app/pages/AppointmentBookingPage.tsx | Support reschedule mode and preserve current appointment context while selecting a new slot |
| MODIFY | app/components/appointments/BookingConfirmationModal.tsx | Reuse confirmation and conflict states for rescheduling |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Dialog API: https://mui.com/material-ui/api/dialog/
- TanStack Query v4 mutations: https://tanstack.com/query/v4/docs/framework/react/guides/mutations
- WAI-ARIA dialog pattern: https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/
- WCAG 2.1 quick reference: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [x] Add a patient-visible reschedule action for eligible scheduled appointments and block ineligible walk-in appointments from that flow
- [x] Implement a reschedule entry dialog that keeps the original appointment context visible while moving into slot selection
- [x] Reuse the existing booking slot-selection UI in reschedule mode and preserve provider/date filters for replacement-slot choice
- [x] Submit atomic reschedule requests and display the exact 24-hour rejection message when blocked
- [x] Show both original and new appointment times in the success state and refresh dashboard details after completion
- [x] Surface slot-conflict errors with refreshed replacement options when the selected new slot is no longer available
- [x] Validate keyboard navigation, focus management, responsive layout, and accessible status messaging for all reschedule states
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete