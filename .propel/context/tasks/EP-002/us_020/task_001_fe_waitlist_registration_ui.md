# Task - task_001_fe_waitlist_registration_ui

## Requirement Reference

- User Story: US_020
- Story Location: .propel/context/tasks/EP-002/us_020/us_020.md
- Acceptance Criteria:
    - AC-1: Given all slots for my preferred time are booked, When I select "Join Waitlist", Then I am registered on the waitlist with my preferred criteria (date, time, provider).
    - AC-3: Given I receive a waitlist notification, When I click the booking link, Then the slot is held for me for 1 minute to complete booking.
- Edge Case:
    - EC-1: If the patient cancels a different appointment, the waitlist entry remains active unless explicitly removed.
    - EC-2: If a waitlist slot opens within 24 hours, the UI must still surface the offer while clearly noting the upcoming time.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-006-appointment-booking.html |
| **Screen Spec** | figma_spec.md#SCR-006 |
| **UXR Requirements** | UXR-101, UXR-201, UXR-202, UXR-301, UXR-502, UXR-601 |
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

Implement the patient-facing waitlist experience on SCR-006 Appointment Booking. This frontend task delivers the UI portion of FR-017 and FR-018 by adding a "Join Waitlist" path when no slots match the patient's preferred criteria, a confirmation state showing the saved date, time, and provider preferences, and a waitlist-offer landing flow that consumes the notification link and presents the held slot with a 60-second countdown. The UI must remain accessible and responsive per NFR-046 and NFR-047, align with the existing booking layout from US_017 and US_018, and clearly communicate when an offered slot is less than 24 hours away.

## Dependent Tasks

- task_002_be_waitlist_registration_orchestration (Waitlist registration, offer link generation, and claim-hold API must exist)
- US_017 task_001_fe_appointment_slot_viewing (No-slots state and slot criteria selection already established on SCR-006)
- US_018 task_001_fe_appointment_booking_confirmation (Existing booking confirmation and 1-minute hold UX should be reused for claimed waitlist offers)

## Impacted Components

- **NEW** `JoinWaitlistDialog` - Collects and confirms preferred waitlist criteria from the no-slots state (app/components/appointments/)
- **NEW** `WaitlistConfirmationNotice` - Displays successful registration and active waitlist criteria summary (app/components/appointments/)
- **NEW** `useJoinWaitlist` - React Query mutation hook for waitlist registration requests (app/hooks/)
- **NEW** `useWaitlistOfferClaim` - React Query hook for claim-link validation and held-slot retrieval (app/hooks/)
- **MODIFY** `AppointmentBookingPage` - Add waitlist branch for empty results and notification-link landing state (app/pages/)
- **MODIFY** `TimeSlotGrid` / empty-state components - Surface Join Waitlist CTA when fully booked for current criteria (app/components/appointments/)
- **MODIFY** `BookingConfirmationModal` - Accept claimed waitlist hold context and countdown copy reuse (app/components/appointments/)

## Implementation Plan

1. **Add a waitlist CTA to the no-slots state** on SCR-006 so fully booked criteria expose a clear "Join Waitlist" action instead of a dead-end empty state.
2. **Create `JoinWaitlistDialog`** to confirm the preferred date, time range, and provider criteria already selected on the booking page, minimizing duplicate input while preserving editability before submission.
3. **Implement `useJoinWaitlist`** to submit waitlist registration, handle validation and duplicate-entry responses, and return a normalized success payload for UI confirmation.
4. **Render a persistent confirmation state** after registration showing that the waitlist is active and summarizing the saved criteria, including the rule that the entry remains active until explicitly removed.
5. **Handle notification-link entry into SCR-006** by reading the claim token from the booking URL, requesting the held slot from the backend, and transitioning directly into the existing booking confirmation flow.
6. **Display the 1-minute waitlist-offer hold countdown** using the existing hold UX patterns from booking, including expiry handling, inline error feedback, and a clear message when the offered slot is within 24 hours.
7. **Validate responsive and accessible behavior** for dialog focus management, keyboard navigation, live countdown announcements, and empty-state clarity across 375px, 768px, and 1440px breakpoints.

## Current Project State

```text
app/
  components/
    appointments/
      SlotCalendar.tsx
      TimeSlotGrid.tsx
      ProviderFilter.tsx
      BookingConfirmationModal.tsx
      SlotConflictModal.tsx
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
| CREATE | app/components/appointments/JoinWaitlistDialog.tsx | Dialog for confirming fully-booked criteria and submitting waitlist registration |
| CREATE | app/components/appointments/WaitlistConfirmationNotice.tsx | Success state showing active waitlist criteria and next-step guidance |
| CREATE | app/hooks/useJoinWaitlist.ts | React Query mutation for waitlist registration |
| CREATE | app/hooks/useWaitlistOfferClaim.ts | Query/mutation logic for claim-link validation and held-slot retrieval |
| MODIFY | app/pages/AppointmentBookingPage.tsx | Add empty-state waitlist flow and notification-link landing behavior |
| MODIFY | app/components/appointments/TimeSlotGrid.tsx | Surface Join Waitlist CTA when selected criteria are fully booked |
| MODIFY | app/components/appointments/BookingConfirmationModal.tsx | Reuse hold countdown and copy for claimed waitlist offers |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Dialog API: https://mui.com/material-ui/api/dialog/
- TanStack Query v4 mutations: https://tanstack.com/query/v4/docs/framework/react/guides/mutations
- TanStack Query v4 queries: https://tanstack.com/query/v4/docs/framework/react/guides/queries
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

- [ ] Add a Join Waitlist CTA to the SCR-006 fully-booked empty state and open a confirmation dialog with the selected criteria
- [ ] Implement `JoinWaitlistDialog` and `useJoinWaitlist` for registration success, duplicate registration handling, and recoverable error states
- [ ] Show a post-registration confirmation state that summarizes the active waitlist criteria and clarifies persistence of the entry
- [ ] Support notification-link entry on the booking page and exchange the claim token for a held slot before showing booking confirmation
- [ ] Reuse the existing 60-second hold UX for waitlist offers, including countdown expiry and actionable error messaging
- [ ] Highlight when an offered slot is within 24 hours without blocking booking from the notification link
- [ ] Validate keyboard navigation, focus order, and accessible announcements for the dialog and countdown states
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete