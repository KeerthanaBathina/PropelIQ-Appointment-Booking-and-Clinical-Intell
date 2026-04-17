# Task - task_001_fe_appointment_booking_confirmation

## Requirement Reference

- User Story: US_018
- Story Location: .propel/context/tasks/EP-002/us_018/us_018.md
- Acceptance Criteria:
    - AC-1: Given I select an available slot, When I confirm the booking, Then the system creates the appointment using optimistic locking and returns confirmation within 2 seconds.
    - AC-2: Given another patient selects the same slot simultaneously, When both confirm, Then only the first transaction succeeds; the second receives "Slot no longer available" with refreshed availability.
    - AC-3: Given I select a slot but do not confirm, When 1 minute passes, Then the hold is released and the slot returns to available inventory.
    - AC-4: Given booking succeeds, When the confirmation is displayed, Then it shows appointment date, time, provider, and appointment type with a booking reference number.
- Edge Case:
    - EC-1: Database temporarily unavailable during booking -> System retries once, then displays "Service temporarily unavailable. Please try again."

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-006-appointment-booking.html |
| **Screen Spec** | figma_spec.md#SCR-006 |
| **UXR Requirements** | UXR-102, UXR-503, UXR-602 |
| **Design Tokens** | designsystem.md#colors (appointment-status, primary, semantic), designsystem.md#typography, designsystem.md#spacing |

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

Implement the appointment booking confirmation flow on the SCR-006 Appointment Booking screen. This frontend task builds the booking confirmation modal with appointment details review (UXR-102), optimistic UI updates that visually mark a slot as "held" immediately upon selection with automatic rollback on conflict (UXR-503), a slot conflict error modal displaying "Slot no longer available" with the next 3 alternative available slots (UXR-602), a 1-minute hold countdown timer with auto-release feedback, and the success confirmation view showing appointment date, time, provider, appointment type, and booking reference number. The flow also handles service unavailability errors with a retry prompt.

## Dependent Tasks

- task_002_be_appointment_booking_api (POST /api/appointments endpoint and hold API must be available)
- US_017 task_001_fe_appointment_slot_viewing (Slot viewing UI must exist for slot selection interaction)
- US_017 task_002_be_appointment_slot_api (Slot availability API used by conflict modal for alternative slots)

## Impacted Components

- **NEW** `BookingConfirmationModal` - Modal displaying slot details with confirm/cancel actions (app/components/appointments/)
- **NEW** `SlotConflictModal` - Error modal for 409 conflict showing 3 alternative slots (app/components/appointments/)
- **NEW** `BookingSuccessView` - Success confirmation with date, time, provider, type, reference number (app/components/appointments/)
- **NEW** `useBookAppointment` - React Query mutation hook for POST /api/appointments (app/hooks/)
- **NEW** `useSlotHold` - Hook managing slot hold state, countdown timer, and auto-release (app/hooks/)
- **MODIFY** `TimeSlotGrid` (from US_017) - Add slot selection handler triggering hold and confirmation flow
- **MODIFY** `AppointmentBookingPage` (from US_017) - Integrate booking confirmation modal and conflict handling

## Implementation Plan

1. **Build `BookingConfirmationModal` component** using MUI Dialog. Display selected slot details (date, time, provider name, appointment type) for patient review. Include "Confirm Booking" primary button and "Cancel" secondary button. Show loading spinner on confirm click while API processes. Implement the confirmation dialog pattern per UXR-102 to prevent accidental bookings.
2. **Implement optimistic UI slot reservation** in the `TimeSlotGrid` component (from US_017). When patient selects a slot, immediately mark it visually as "held" (dimmed with "Reserved" badge, using `primary.300` color). Call the hold API to reserve the slot in Redis. If hold fails (slot already held by another user), show inline error and revert visual state per UXR-503 rollback pattern.
3. **Build `SlotConflictModal` component** using MUI Dialog for 409 Conflict responses. Display "Slot no longer available" message with empathetic tone per figma_spec.md content guidelines ("That slot was just booked. Here are 3 similar options."). Render 3 alternative available slots returned by the API as selectable cards. Allow patient to select an alternative or return to calendar. Per UXR-602.
4. **Implement `useSlotHold` hook** managing the 1-minute hold lifecycle. Start a 60-second countdown timer on slot selection. Display remaining time in the confirmation modal. On timeout, call the release endpoint, revert the slot visual state, and show "Hold expired — slot released" toast notification. Clear timer on successful booking or manual cancellation.
5. **Build `BookingSuccessView` component** displayed after 201 Created response. Show appointment date (MM/DD/YYYY), time (12-hour AM/PM), provider name, appointment type, and booking reference number. Include "View in Dashboard" and "Add to Calendar" CTA buttons. Use success semantic color (`#2E7D32`) and confirmation icon.
6. **Implement error handling for service unavailability** (EC-1). On 503 Service Unavailable, display "Service temporarily unavailable. Please try again." with a "Retry" button. On retry failure, maintain the error message. Use MUI Alert component with error severity.
7. **Add ARIA live region announcements** for booking status transitions (hold acquired, countdown warning at 15s, hold expired, booking confirmed, booking failed) and ensure keyboard navigation works for all modal interactions (Tab order, Escape to close, Enter to confirm).

## Current Project State

```text
[Placeholder - to be updated based on dependent task completion]
app/
  components/
    appointments/
      SlotCalendar.tsx (from US_017)
      TimeSlotGrid.tsx (from US_017 - to modify)
      ProviderFilter.tsx (from US_017)
      (new modal and view files to be created here)
  hooks/
    useAppointmentSlots.ts (from US_017)
    (new hook files to be created here)
  pages/
    AppointmentBookingPage.tsx (from US_017 - to modify)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/components/appointments/BookingConfirmationModal.tsx | Confirmation modal with slot details, confirm/cancel, loading state |
| CREATE | app/components/appointments/SlotConflictModal.tsx | 409 conflict modal with 3 alternative slot suggestions |
| CREATE | app/components/appointments/BookingSuccessView.tsx | Success view with date, time, provider, type, reference number |
| CREATE | app/hooks/useBookAppointment.ts | React Query mutation for POST /api/appointments |
| CREATE | app/hooks/useSlotHold.ts | Hold state management with 60-second countdown timer |
| MODIFY | app/components/appointments/TimeSlotGrid.tsx | Add slot selection handler, optimistic "held" visual state |
| MODIFY | app/pages/AppointmentBookingPage.tsx | Integrate booking flow: hold -> confirm modal -> success/conflict |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Dialog API: https://mui.com/material-ui/api/dialog/
- MUI 5 Alert API: https://mui.com/material-ui/api/alert/
- React Query mutations: https://tanstack.com/query/v4/docs/framework/react/guides/mutations
- WAI-ARIA Dialog pattern: https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/
- WCAG 2.1 AA guidelines: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [ ] Build `BookingConfirmationModal` with MUI Dialog displaying slot details (date, time, provider, type), confirm/cancel buttons, and loading state on confirm (UXR-102, AC-1)
- [ ] Implement optimistic UI slot reservation in `TimeSlotGrid` — visually mark slot as "held" on selection with automatic rollback on hold failure (UXR-503, AC-3)
- [ ] Build `SlotConflictModal` displaying "Slot no longer available" message with 3 alternative available slots as selectable cards on 409 Conflict response (UXR-602, AC-2)
- [ ] Implement `useSlotHold` hook with 60-second countdown timer, visual countdown in confirmation modal, auto-release on timeout with toast notification (AC-3)
- [ ] Build `BookingSuccessView` showing appointment date, time, provider, appointment type, and booking reference number with "View in Dashboard" CTA (AC-4)
- [ ] Implement service unavailability error handling with "Service temporarily unavailable" message and retry button on 503 responses (EC-1)
- [ ] Add ARIA live region announcements for booking state transitions and keyboard navigation for all modal interactions (WCAG 2.1 AA)
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
