# Task - task_001_fe_appointment_history_ui

## Requirement Reference

- User Story: US_024
- Story Location: .propel/context/tasks/EP-003/us_024/us_024.md
- Acceptance Criteria:
    - AC-1: Given I navigate to appointment history, When the page loads, Then all my appointments are displayed in a paginated table sorted by date (newest first) with status badges (scheduled, completed, cancelled, no-show).
    - AC-2: Given the history table is displayed, When I click a column header (date), Then the table sorts by that column in ascending/descending order.
    - AC-3: Given I have 50+ appointments, When the table loads, Then pagination shows 10 items per page with Previous/Next navigation.
    - AC-4: Given appointments exist, When the data is fetched, Then each row shows date, time, provider, type, and status badge with consistent status colors per UXR-401.
- Edge Case:
    - EC-1: If the patient has no appointment history, the page must display "No appointments found. Book your first appointment!" with a link to SCR-006.
    - EC-2: Cancelled appointments remain visible with a grey badge and reduced row opacity.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-007-appointment-history.html |
| **Screen Spec** | figma_spec.md#SCR-007 |
| **UXR Requirements** | UXR-001, UXR-003, UXR-401, UXR-502 |
| **Design Tokens** | designsystem.md#Appointment Status Colors, designsystem.md#Breadcrumb, designsystem.md#Pagination, designsystem.md#Table |

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

Implement the patient-facing appointment history experience for SCR-007. This frontend task delivers the UI layer for FR-024 by rendering the patient's appointment history in a sortable, paginated table with consistent status badges, breadcrumb navigation, loading and empty states, and a clear "Book New" recovery path when no appointments exist. The page must honor UXR-401 status colors, UXR-502 loading behavior, and the accessibility and responsive requirements in NFR-046 and NFR-047 while remaining aligned to the history wireframe and existing patient navigation patterns.

## Dependent Tasks

- task_002_be_appointment_history_api (Patient history data contract and pagination metadata must exist before UI wiring)
- US_018 task_001_fe_appointment_booking_confirmation (Booked appointment data patterns and patient navigation context should already exist)
- US_019 task_001_fe_appointment_cancellation_ui (Appointment history page structure and cancelled-state rendering patterns should be reused)
- US_023 task_001_fe_appointment_rescheduling_ui (History page must stay consistent with updated appointment detail refresh behavior)

## Impacted Components

- **NEW** `AppointmentHistoryTable` - Table wrapper for appointment rows, sortable date header, and pagination controls (app/components/appointments/)
- **NEW** `AppointmentStatusBadge` - Shared badge component mapping appointment statuses to design-system colors and muted cancelled styling (app/components/appointments/)
- **NEW** `useAppointmentHistory` - React Query hook that fetches patient history pages and normalizes sort and pagination state (app/hooks/)
- **MODIFY** `AppointmentHistoryPage` - Render breadcrumb, empty/loading/error states, Book New CTA, and paginated table workflow for SCR-007 (app/pages/)
- **MODIFY** `app/routes.tsx` - Ensure SCR-007 route is registered in patient navigation and route configuration

## Implementation Plan

1. **Create `useAppointmentHistory`** to request appointment history with default newest-first ordering, a fixed page size of 10, and explicit page and sort parameters so the UI does not duplicate query construction logic.
2. **Build `AppointmentStatusBadge`** as a shared mapping from scheduled, completed, cancelled, and no-show states to design-system colors, including reduced-opacity styling for cancelled rows.
3. **Implement `AppointmentHistoryTable`** using the SCR-007 wireframe structure with columns for date, time, provider, type, and status, plus a sortable date header that toggles ascending and descending order.
4. **Update `AppointmentHistoryPage`** to compose breadcrumb navigation, loading skeletons, error handling, the empty-state message and link to SCR-006, and the Book New CTA shown in the populated view.
5. **Wire pagination behavior** so the page shows 10 items per page, exposes Previous and Next navigation, preserves sort selection across page changes, and keeps the current page announced accessibly. Treat the user story's 10-item requirement as authoritative over the generic large-data example in `figma_spec.md`.
6. **Keep history data refresh-safe** by invalidating or refetching the query after booking, cancellation, or rescheduling flows update appointment state so the history screen remains consistent with other patient surfaces.
7. **Validate responsive and accessible behavior** across 375px, 768px, and 1440px widths, including keyboard focus on sortable headers and pagination controls, readable badge contrast, and screen-reader labeling for table and empty-state actions.

## Current Project State

```text
app/
  components/
    appointments/
      BookingConfirmationModal.tsx
      BookingSuccessView.tsx
      CancelAppointmentDialog.tsx
      SlotConflictModal.tsx
      TimeSlotGrid.tsx
  hooks/
    useAppointmentSlots.ts
    useBookAppointment.ts
    useCancelAppointment.ts
    useRescheduleAppointment.ts
  pages/
    AppointmentBookingPage.tsx
    AppointmentHistoryPage.tsx
    PatientDashboardPage.tsx
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/components/appointments/AppointmentHistoryTable.tsx | History table layout with date sorting and page navigation controls |
| CREATE | app/components/appointments/AppointmentStatusBadge.tsx | Shared appointment status badge styling for scheduled, completed, cancelled, and no-show states |
| CREATE | app/hooks/useAppointmentHistory.ts | React Query hook for authenticated history retrieval with page and sort parameters |
| MODIFY | app/pages/AppointmentHistoryPage.tsx | Implement SCR-007 populated, loading, error, and empty states |
| MODIFY | app/routes.tsx | Register or update patient route for appointment history page |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 Table API: https://mui.com/material-ui/react-table/
- MUI 5 Pagination API: https://mui.com/material-ui/react-pagination/
- TanStack Query v4 query guide: https://tanstack.com/query/v4/docs/framework/react/guides/queries
- WCAG 2.1 quick reference: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [x] Implement `useAppointmentHistory` with newest-first default sort, page-size 10, and persistent sort state across page navigation
- [x] Build the history table with date, time, provider, type, and status columns plus a sortable date header
- [x] Render scheduled, completed, cancelled, and no-show badges using the design-system status colors and muted cancelled-row styling
- [x] Add loading, empty, and error states that match SCR-007 and include the required "No appointments found. Book your first appointment!" recovery path
- [x] Add Previous and Next pagination controls and keep the active page and sort order accessible to keyboard and screen-reader users
- [x] Refresh history data after booking, cancellation, or rescheduling changes so status tracking remains current across patient flows
- [x] Validate breadcrumb layout, responsive table behavior, and status badge contrast at 375px, 768px, and 1440px
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete