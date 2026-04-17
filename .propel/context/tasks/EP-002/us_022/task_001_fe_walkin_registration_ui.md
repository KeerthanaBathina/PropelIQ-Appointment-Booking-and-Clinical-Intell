# Task - task_001_fe_walkin_registration_ui

## Requirement Reference

- User Story: US_022
- Story Location: .propel/context/tasks/EP-002/us_022/us_022.md
- Acceptance Criteria:
    - AC-1: Given I am on the staff dashboard, When I click "Walk-in Registration", Then a form opens for patient search or new patient creation with same-day slot selection.
    - AC-2: Given a walk-in patient has an existing record, When I search by name, DOB, or phone, Then matching patient records are displayed for selection.
    - AC-3: Given I select a same-day slot for the walk-in, When I confirm the booking, Then the appointment is created with "walk-in" designation and the patient is added to the arrival queue automatically.
    - AC-4: Given no same-day slots are available, When the walk-in form loads, Then the system displays "No same-day slots available" with the next available date/time.
- Edge Case:
    - EC-1: Patient-role users must not see or access walk-in booking actions; the UI should surface the staff-only restriction message returned by the backend.
    - EC-2: Urgent walk-ins must expose a priority control and supervisor-escalation guidance when same-day slots are full.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-010-staff-dashboard.html |
| **Screen Spec** | figma_spec.md#SCR-010 |
| **UXR Requirements** | UXR-005, UXR-202, UXR-301, UXR-403, UXR-502, UXR-601 |
| **Design Tokens** | designsystem.md#Color Palette (secondary, semantic, appointment-status), designsystem.md#Typography, designsystem.md#Spacing |

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

Implement the staff-facing walk-in registration experience on SCR-010 Staff Dashboard. This frontend task delivers the UI layer for FR-021 and FR-022 by adding the walk-in registration modal, patient lookup by name/DOB/phone, inline fallback for creating a new patient record, same-day slot selection, and urgent-priority capture before booking. The flow must preserve the staff portal visual treatment from UXR-403, keep patient search accessible and fast per UXR-005, and clearly handle empty same-day availability with the required "No same-day slots available" message plus next available appointment guidance.

## Dependent Tasks

- task_002_be_walkin_booking_api (Staff-only walk-in booking, search, and queue-add endpoints must exist)
- US_017 task_001_fe_appointment_slot_viewing (Slot-selection patterns and same-day slot rendering can be reused)
- US_018 task_001_fe_appointment_booking_confirmation (Existing booking confirmation patterns should inform slot confirmation UX)

## Impacted Components

- **NEW** `WalkInRegistrationModal` - Staff modal for patient search/create and same-day slot selection (app/components/staff/)
- **NEW** `WalkInPatientSearchResults` - Search result list for matching patient records by name, DOB, or phone (app/components/staff/)
- **NEW** `useWalkInRegistration` - React Query mutation hook for staff-only walk-in booking requests (app/hooks/)
- **NEW** `useWalkInPatientSearch` - Query hook for matching patient search results (app/hooks/)
- **MODIFY** `StaffDashboardPage` - Add walk-in registration trigger and post-booking queue refresh behavior (app/pages/)
- **MODIFY** shared same-day slot selector components - Reuse appointment slot patterns in staff context with urgent-priority support (app/components/appointments/ or app/components/staff/)

## Implementation Plan

1. **Add the Walk-in Registration launch point** to SCR-010 using the existing wireframe CTA placement, opening a modal rather than navigating away from the staff dashboard.
2. **Create `WalkInRegistrationModal`** with a two-path flow: search existing patient records first, then allow inline creation of a minimal new patient record when no match is selected.
3. **Implement `useWalkInPatientSearch`** to search by name, DOB, or phone with debounced or explicit-submit behavior and return selectable patient matches for staff review.
4. **Integrate same-day slot selection** into the modal so staff can choose from available same-day appointments only, with a clear empty state and next available date/time when none exist.
5. **Capture urgent priority explicitly** when same-day slots are full or the staff member flags urgency, showing supervisor-escalation guidance rather than silently failing the workflow.
6. **Implement `useWalkInRegistration`** so staff can submit the selected patient/new patient plus slot and urgency choice, then update the queue summary and success state on completion.
7. **Validate staff-only UX requirements** including keyboard navigation, focus order, search accessibility, loading states, and clear error handling for unauthorized or unavailable-booking responses.

## Current Project State

```text
app/
  components/
    appointments/
      SlotCalendar.tsx
      TimeSlotGrid.tsx
      BookingConfirmationModal.tsx
    staff/
      (walk-in components to be added here)
  hooks/
    useAppointmentSlots.ts
    useBookAppointment.ts
  pages/
    AppointmentBookingPage.tsx
    StaffDashboardPage.tsx
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/components/staff/WalkInRegistrationModal.tsx | Modal for patient search/create, same-day slot selection, and urgency capture |
| CREATE | app/components/staff/WalkInPatientSearchResults.tsx | Search results list with record selection state |
| CREATE | app/hooks/useWalkInPatientSearch.ts | Staff patient-search query hook by name, DOB, or phone |
| CREATE | app/hooks/useWalkInRegistration.ts | Mutation hook for staff walk-in appointment booking |
| MODIFY | app/pages/StaffDashboardPage.tsx | Add Walk-in Registration CTA wiring and post-booking queue refresh |
| MODIFY | app/components/appointments/TimeSlotGrid.tsx | Reuse same-day slot selection in staff context with staff-oriented copy and restrictions |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Dialog API: https://mui.com/material-ui/api/dialog/
- MUI 5 Autocomplete API: https://mui.com/material-ui/react-autocomplete/
- TanStack Query v4 queries: https://tanstack.com/query/v4/docs/framework/react/guides/queries
- TanStack Query v4 mutations: https://tanstack.com/query/v4/docs/framework/react/guides/mutations
- WCAG 2.1 quick reference: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [ ] Add the Walk-in Registration modal trigger to SCR-010 and keep the workflow on the staff dashboard
- [ ] Implement patient search by name, DOB, or phone with selectable results and minimal inline new-patient fallback
- [ ] Show same-day slots only, and display the exact "No same-day slots available" message with next available date/time when empty
- [ ] Capture urgent priority and supervisor-escalation guidance when same-day capacity is exhausted or urgency is flagged
- [ ] Submit walk-in booking requests and refresh queue-facing staff dashboard data after success
- [ ] Surface staff-only restriction and other recoverable API errors with clear messaging
- [ ] Validate keyboard navigation, modal focus management, loading states, and responsive staff-dashboard behavior
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete