# Task - task_001_fe_calendar_sync_ui

## Requirement Reference

- User Story: US_025
- Story Location: .propel/context/tasks/EP-003/us_025/us_025.md
- Acceptance Criteria:
    - AC-1: Given I have a confirmed appointment, When I click "Add to Calendar", Then the system generates an iCal (.ics) file with appointment details (date, time, provider, location).
    - AC-2: Given the iCal file is downloaded, When I open it, Then Google Calendar and Outlook correctly import the event with proper timezone handling.
    - AC-3: Given I reschedule an appointment, When a new iCal is generated, Then it includes the UID of the original event to update (not duplicate) the calendar entry.
- Edge Case:
    - EC-1: If the patient's calendar app does not support iCal, the UI must preserve a manual fallback by keeping appointment details visible after download.
    - EC-2: Timezone-sensitive appointment details must be displayed in the patient's local time while the downloaded iCal payload uses the clinic timezone definition.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-005-patient-dashboard.html |
| **Screen Spec** | figma_spec.md#SCR-005 |
| **UXR Requirements** | N/A |
| **Design Tokens** | designsystem.md#Color Palette (primary, semantic, appointment-status), designsystem.md#Typography, designsystem.md#Spacing |

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

Implement the patient-facing calendar sync affordance on SCR-005. This frontend task delivers the UI layer for FR-025 by adding an "Add to Calendar" action for eligible confirmed appointments on the patient dashboard, triggering download of a backend-generated `.ics` file, and preserving clear appointment details in the UI as a manual fallback when calendar import is not supported. The experience must remain accessible and responsive per NFR-046 and NFR-047, align to the dashboard wireframe, and stay consistent with booking, cancellation, and rescheduling actions already present on patient appointment cards.

## Dependent Tasks

- task_002_be_calendar_sync_ical_api (The downloadable iCal endpoint and timezone-safe file generation must exist before UI wiring)
- US_018 task_001_fe_appointment_booking_confirmation (Confirmed appointment presentation patterns and dashboard appointment actions should already exist)
- US_023 task_001_fe_appointment_rescheduling_ui (Rescheduled appointments must expose the same calendar download affordance without duplicating actions)
- US_019 task_001_fe_appointment_cancellation_ui (Appointment action layout on SCR-005 should remain visually consistent)

## Impacted Components

- **NEW** `useAppointmentCalendarDownload` - Hook that requests the `.ics` file and coordinates browser download/error handling (app/hooks/)
- **NEW** `AppointmentCalendarAction` - Shared action element or button handling loading, disabled, and retry states for calendar downloads (app/components/appointments/)
- **MODIFY** `PatientDashboardPage` - Add the Add to Calendar affordance on eligible confirmed appointment cards (app/pages/)
- **MODIFY** `BookingSuccessView` - Reuse the same download affordance immediately after booking completion so the action pattern remains consistent (app/components/appointments/)

## Implementation Plan

1. **Add a shared calendar download hook** that calls the backend calendar endpoint with the selected appointment identifier, handles the binary file response safely, and normalizes failure states for unsupported or transient download errors.
2. **Create `AppointmentCalendarAction`** as a reusable button or menu action for appointment cards, with loading feedback during download and disabled behavior for ineligible appointments.
3. **Integrate the action into SCR-005 appointment cards** so confirmed appointments expose "Add to Calendar" alongside existing actions without overwhelming the dashboard layout.
4. **Preserve timezone clarity in the UI** by continuing to display the appointment in the patient's local time while relying on backend-generated calendar content for clinic-timezone `.ics` semantics.
5. **Keep a manual fallback visible** by ensuring the appointment card still exposes date, time, provider, and location details even if the patient's calendar application does not import `.ics` files.
6. **Ensure the action remains valid after rescheduling** by reusing the appointment's stable identity when downloading an updated calendar file so the next import updates the prior calendar entry instead of creating a duplicate.
7. **Validate responsive and accessible behavior** for button focus states, download progress indication, and error messaging at 375px, 768px, and 1440px breakpoints.

## Current Project State

```text
app/
  components/
    appointments/
      BookingConfirmationModal.tsx
      BookingSuccessView.tsx
      CancelAppointmentDialog.tsx
  hooks/
    useBookAppointment.ts
    useCancelAppointment.ts
    useRescheduleAppointment.ts
  pages/
    AppointmentBookingPage.tsx
    PatientDashboardPage.tsx
    AppointmentHistoryPage.tsx
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/hooks/useAppointmentCalendarDownload.ts | Hook for invoking the calendar download endpoint and saving the returned `.ics` file |
| CREATE | app/components/appointments/AppointmentCalendarAction.tsx | Reusable Add to Calendar action with loading and error states |
| MODIFY | app/pages/PatientDashboardPage.tsx | Add the calendar download action to eligible confirmed appointment cards on SCR-005 |
| MODIFY | app/components/appointments/BookingSuccessView.tsx | Reuse the same download action on booking confirmation when appropriate |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Button API: https://mui.com/material-ui/react-button/
- TanStack Query v4 mutations: https://tanstack.com/query/v4/docs/framework/react/guides/mutations
- MDN Blob API: https://developer.mozilla.org/en-US/docs/Web/API/Blob
- WCAG 2.1 quick reference: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [ ] Add a reusable calendar download hook that requests the `.ics` payload for a selected appointment and triggers browser save behavior
- [ ] Build an accessible Add to Calendar action with loading, disabled, and retry-safe error states
- [ ] Place the action on eligible confirmed patient dashboard appointment cards without breaking existing cancel or reschedule controls
- [ ] Preserve patient-local appointment details in the UI so manual entry remains possible if calendar import is unsupported
- [ ] Reuse the same appointment identity after reschedule so repeated downloads map to the same calendar event update path
- [ ] Validate focus states, responsive layout, and error feedback for the download interaction across required breakpoints
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete