# Task - task_001_fe_appointment_slot_viewing

## Requirement Reference

- User Story: US_017
- Story Location: .propel/context/tasks/EP-002/us_017/us_017.md
- Acceptance Criteria:
    - AC-1: Given I am on the booking page, When I select a date range and optionally a provider, Then the system displays all available slots matching my criteria within 2 seconds.
    - AC-2: Given available slots are displayed, When I view the calendar, Then dates with available slots show visual indicators (dots) and unavailable dates are grayed out.
    - AC-3: Given I select a specific date, When the time slot grid loads, Then slots are shown in 30-minute increments with provider name and appointment type.
- Edge Case:
    - EC-1: No slots available for criteria -> System displays "No available slots" with suggestions to try different dates or providers.
    - EC-2: Booking windows beyond 90 days -> Date picker restricts selection to 90 days from today per FR-013.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-006-appointment-booking.html |
| **Screen Spec** | figma_spec.md#SCR-006 |
| **UXR Requirements** | UXR-101, UXR-301, UXR-401, UXR-502 |
| **Design Tokens** | designsystem.md#colors (appointment-status, primary), designsystem.md#typography, designsystem.md#spacing |

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

Implement the Appointment Slot Viewing & Filtering UI on the SCR-006 Appointment Booking screen. This frontend task builds the patient-facing calendar date picker with 90-day range restriction, the time slot grid displaying 30-minute increments with provider name and appointment type, and the provider filter dropdown. The component must render visual indicators (dots) on dates with availability, gray out unavailable dates, display skeleton loading during data fetches, and show an empty state with actionable suggestions when no slots match the filter criteria. All layouts must be responsive (320px to 2560px) and meet WCAG 2.1 AA accessibility standards.

## Dependent Tasks

- task_003_db_appointment_slot_indexes (Database indexes must exist for performant queries)
- task_002_be_appointment_slot_api (API endpoint must be available for slot data fetching)
- US_008 - Foundational - Requires Appointment entity
- US_004 - Foundational - Requires Redis caching infrastructure

## Impacted Components

- **NEW** `AppointmentBookingPage` - Page component for SCR-006 route (app/pages/)
- **NEW** `SlotCalendar` - Calendar date picker with availability dots and 90-day restriction (app/components/appointments/)
- **NEW** `TimeSlotGrid` - Grid displaying 30-minute slot increments with provider and type (app/components/appointments/)
- **NEW** `ProviderFilter` - MUI Select dropdown for provider filtering (app/components/appointments/)
- **NEW** `useAppointmentSlots` - React Query hook for slot availability data fetching (app/hooks/)

## Implementation Plan

1. **Create the `AppointmentBookingPage` component** as the SCR-006 route container. Wire breadcrumb navigation (UXR-003) and set up the page layout with responsive grid (MUI Grid) for mobile-stacked / desktop side-by-side calendar + slot grid.
2. **Build the `SlotCalendar` component** wrapping MUI DateCalendar. Restrict selectable dates to today through today + 90 days (FR-013). Render a colored dot (`Badge`) on dates that have at least one available slot. Gray out dates with zero availability using `shouldDisableDate`. Fetch date-level availability summary from API on month navigation.
3. **Build the `TimeSlotGrid` component** rendering a list/grid of available 30-minute slot cards for the selected date. Each card shows time range, provider name, and appointment type. Apply appointment status color (scheduled = `#1976D2` Blue). Support keyboard navigation for slot selection (UXR-202).
4. **Build the `ProviderFilter` component** using MUI Select with "All Providers" default. Populate options from the slot API response provider list. On change, re-filter displayed slots (client-side filter on cached data or re-query).
5. **Implement data fetching with `useAppointmentSlots` React Query hook** calling `GET /api/appointments/slots` with date range, time, and provider parameters. Configure `staleTime: 5 * 60 * 1000` (5 minutes) to align with Redis cache TTL. Handle loading, error, and empty states.
6. **Implement skeleton loading (UXR-502)** for calendar and slot grid during initial data fetch (>300ms). Show `Skeleton` placeholders matching final layout dimensions.
7. **Implement empty state (EC-1)** when no slots match criteria: display "No available slots" message with suggestions to try different dates or providers. Include a "Try Different Date" CTA button.

## Current Project State

```
app/
  components/
    appointments/
      ProviderFilter.tsx       ✅ Created
      SlotCalendar.tsx         ✅ Created
      TimeSlotGrid.tsx         ✅ Created
  hooks/
    useAppointmentSlots.ts     ✅ Created
  pages/
    AppointmentBookingPage.tsx ✅ Created
  router.tsx                   ✅ Updated — /patient/appointments/book route added
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/pages/AppointmentBookingPage.tsx | SCR-006 page component with layout, breadcrumb, responsive grid |
| CREATE | app/components/appointments/SlotCalendar.tsx | Calendar date picker with availability dots, 90-day restriction |
| CREATE | app/components/appointments/TimeSlotGrid.tsx | 30-minute slot grid with provider name and appointment type |
| CREATE | app/components/appointments/ProviderFilter.tsx | MUI Select dropdown for provider filtering |
| CREATE | app/hooks/useAppointmentSlots.ts | React Query hook for GET /api/appointments/slots |
| MODIFY | app/routes.tsx | Add route for /appointments/book pointing to AppointmentBookingPage |

## External References

- React 18 documentation: https://react.dev/reference/react
- MUI 5 DateCalendar API: https://mui.com/x/api/date-pickers/date-calendar/
- MUI 5 Select API: https://mui.com/material-ui/api/select/
- React Query v4 documentation: https://tanstack.com/query/v4/docs/framework/react/overview
- WCAG 2.1 AA guidelines: https://www.w3.org/WAI/WCAG21/quickref/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [x] Create `AppointmentBookingPage` component with responsive MUI Grid layout and breadcrumb navigation for SCR-006 route
- [x] Build `SlotCalendar` component with custom 7-column CSS-grid calendar, 90-day date restriction (FR-013), availability dot indicators on dates (AC-2), and grayed-out unavailable dates
- [x] Build `TimeSlotGrid` component displaying 30-minute slot cards with provider name, appointment type, and status color coding (UXR-401, AC-3)
- [x] Build `ProviderFilter` component using MUI Select with "Any Provider" default option and re-filtering on selection change
- [x] Implement `useAppointmentSlots` React Query hook with 5-minute staleTime, error/loading state handling, and query parameter management (AC-1, AC-4)
- [x] Implement skeleton loading placeholders for calendar and slot grid during data fetch (UXR-502)
- [x] Implement empty state display with "No available slots" message and actionable suggestions when zero slots match criteria (EC-1)
- [x] Implement booking confirmation modal (UXR-102) with UXR-503 optimistic close + UXR-602 conflict error with alternative slots
- [x] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- [x] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
- [x] `tsc --noEmit` → exit 0 (no TypeScript errors)
- [x] `eslint` → exit 0 (no lint errors)
