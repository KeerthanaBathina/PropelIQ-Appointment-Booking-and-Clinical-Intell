# Task - task_001_fe_appointment_cancellation_ui

## Requirement Reference

- User Story: US_019
- Story Location: .propel/context/tasks/EP-002/us_019/us_019.md
- Acceptance Criteria:
    - AC-1: Given I have a scheduled appointment, When I cancel more than 24 hours before the appointment, Then the appointment status changes to "cancelled" and the slot is released within 1 minute.
    - AC-2: Given I attempt to cancel within 24 hours of the appointment, When I submit the cancellation, Then the system displays "Cancellations within 24 hours are not permitted. Please contact the clinic."
- Edge Case:
    - EC-1: Already-cancelled appointment requests must display "This appointment has already been cancelled."
    - EC-2: Appointment times are stored and evaluated in UTC while displayed in the patient's local timezone.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-005-patient-dashboard.html; .propel/context/wireframes/Hi-Fi/wireframe-SCR-007-appointment-history.html |
| **Screen Spec** | figma_spec.md#SCR-005, figma_spec.md#SCR-007 |
| **UXR Requirements** | UXR-102, UXR-201, UXR-202, UXR-301, UXR-401, UXR-502, UXR-601 |
| **Design Tokens** | designsystem.md#Color Palette (appointment-status), designsystem.md#Typography, designsystem.md#Spacing |

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

Implement the patient-facing appointment cancellation experience across the patient dashboard and appointment history screens. This frontend task delivers the user interaction layer for FR-015 and FR-016 by adding a shared cancellation confirmation dialog, eligible and ineligible cancellation states, and immediate status refresh after cancellation so patients can cancel scheduled appointments more than 24 hours before the visit while receiving the exact policy message for blocked requests. The UI must satisfy NFR-046 and NFR-047 through accessible, responsive behavior, display appointment times in the patient's local timezone, preserve the UTC-based policy semantics supplied by the backend, and render cancelled status badges using the design-system appointment colors defined by UXR-401.

## Dependent Tasks

- task_002_be_appointment_cancellation_api (Cancellation endpoint and policy evaluation must exist before UI wiring)
- US_018 task_001_fe_appointment_booking_confirmation (Scheduled appointments must already be visible in patient-facing flows)
- US_018 task_002_be_appointment_booking_api (Booked appointments must exist before cancellation can be initiated)

## Impacted Components

- **NEW** `CancelAppointmentDialog` - Shared confirmation dialog for destructive cancellation action (app/components/appointments/)
- **NEW** `useCancelAppointment` - React Query mutation hook for appointment cancellation requests (app/hooks/)
- **MODIFY** `PatientDashboardPage` - Add cancel action to upcoming appointment presentation and refresh cancelled status (app/pages/)
- **MODIFY** `AppointmentHistoryPage` - Add cancellation entry point and cancelled badge rendering for eligible scheduled appointments (app/pages/)
- **MODIFY** shared appointment list/card components - Surface eligibility, disabled states, and policy messaging consistently (app/components/appointments/)

## Implementation Plan

1. **Create `CancelAppointmentDialog`** using MUI Dialog and Alert components for the shared destructive-action pattern on SCR-005 and SCR-007. Show appointment date, time, provider, and the policy reminder that cancellations are only allowed before the 24-hour UTC cutoff.
2. **Add `useCancelAppointment` mutation** that submits the cancellation request, handles loading and retry-safe error states, and normalizes API responses for success, within-24-hours rejection, and already-cancelled responses.
3. **Integrate cancellation entry points into patient appointment surfaces** by adding a cancel action only for scheduled appointments that the backend marks as cancellable. Keep cancelled appointments non-interactive and render the existing cancelled badge color from the design system.
4. **Implement success and policy feedback states** so successful cancellations update the appointment status to `cancelled` immediately in dashboard and history views, blocked requests show the exact required message, and already-cancelled requests show the required idempotent message.
5. **Preserve timezone clarity in the UI** by displaying the patient-local appointment time while using backend-provided UTC eligibility data for all cancellation affordances and copy. Do not calculate the 24-hour rule solely in the browser.
6. **Validate accessibility and responsive behavior** across 375px, 768px, and 1440px breakpoints, including keyboard navigation, visible focus states, ARIA labeling for the dialog, and actionable inline or toast feedback for error states.

## Current Project State

```text
app/
  components/
    appointments/
      SlotCalendar.tsx
      TimeSlotGrid.tsx
      ProviderFilter.tsx
      BookingConfirmationModal.tsx
      BookingSuccessView.tsx
  hooks/
    useAppointmentSlots.ts
    useBookAppointment.ts
    useSlotHold.ts
  pages/
    AppointmentBookingPage.tsx
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/components/appointments/CancelAppointmentDialog.tsx | Shared confirmation dialog with loading, blocked-policy, and already-cancelled states |
| CREATE | app/hooks/useCancelAppointment.ts | React Query mutation wrapper for cancellation endpoint and response normalization |
| MODIFY | app/pages/PatientDashboardPage.tsx | Add cancellation action and immediate cancelled-state refresh for upcoming appointments |
| MODIFY | app/pages/AppointmentHistoryPage.tsx | Add cancel affordance for eligible appointments and consistent cancelled badge rendering |
| MODIFY | app/components/appointments/AppointmentCard.tsx | Surface cancellable state, disabled action state, and status badge updates |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Dialog API: https://mui.com/material-ui/api/dialog/
- MUI 5 Alert API: https://mui.com/material-ui/api/alert/
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

- [x] Create `CancelAppointmentDialog` with appointment summary, destructive confirmation action, and accessible focus management for SCR-005 and SCR-007
- [x] Implement `useCancelAppointment` to handle success, within-24-hours rejection, and already-cancelled responses without duplicating request logic in pages
- [x] Add cancellation actions to patient dashboard and appointment history surfaces only for scheduled appointments that remain cancellable
- [x] Refresh appointment status in-place after success so the UI immediately shows the cancelled badge and removes repeat cancel affordances
- [x] Display the exact policy message for blocked cancellations and the exact idempotent message for already-cancelled appointments
- [x] Preserve UTC-based cancellation eligibility while displaying appointment details in the patient's local timezone
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete