# Task - TASK_003

## Requirement Reference

- User Story: US_059
- Story Location: .propel/context/tasks/EP-010/us_059/us_059.md
- Acceptance Criteria:
  - AC-1: Given the admin opens slot template configuration, When they select a provider and day, Then the system displays the current slot template with configurable time blocks and appointment types.
  - AC-2: Given the admin modifies a slot template, When they save changes, Then the template applies to all future appointments for that provider/day combination without affecting existing bookings.
  - AC-3: Given the admin opens business hours configuration, When they set hours for each day of the week, Then the system enforces these hours in the appointment booking interface.
  - AC-4: Given the admin adds a holiday, When the holiday date is saved, Then no appointment slots are available for that date and existing bookings for the date are flagged for staff review.
- Edge Case:
  - What happens when a template change conflicts with existing appointments? System shows a list of affected appointments and requires admin confirmation before applying.
  - How does the system handle recurring holidays (e.g., Christmas)? Admin can set annual recurring holidays that automatically block slots each year.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-015-admin-dashboard.html |
| **Screen Spec** | figma_spec.md#SCR-015 |
| **UXR Requirements** | UXR-003, UXR-004, UXR-102, UXR-303, UXR-501 |
| **Design Tokens** | designsystem.md#typography, designsystem.md#colors, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement (UI Tasks Only)**

**IF Wireframe Status = AVAILABLE or EXTERNAL:**

- **MUST** open and reference the wireframe file/URL during UI implementation
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

Implement the admin UI for the "Slot Templates" and "Hours & Holidays" tabs within the SCR-015 Admin Configuration Dashboard. The Slot Templates tab provides a weekly grid view per provider where admins can toggle slot availability by clicking cells, select providers from a dropdown, and save templates. The Hours & Holidays tab displays a table of regular operating hours per day with an edit form, plus a holiday list with add/remove capabilities supporting recurring holidays. All forms implement auto-save on configuration screens (UXR-004), inline validation within 200ms (UXR-501), and destructive action confirmation dialogs (UXR-102). The layout stacks vertically on mobile and uses 2-column on desktop (UXR-303).

## Dependent Tasks

- task_002_be_slot_template_business_hours_api — Requires API endpoints for data fetching and mutations
- US_058 — Requires admin dashboard shell with tab navigation framework

## Impacted Components

- **NEW** — `SlotTemplatePanel` component — Slot template grid with provider selector and save
- **NEW** — `SlotGrid` component — Weekly time-block grid with toggle cells
- **NEW** — `BusinessHoursPanel` component — Hours table and holiday list container
- **NEW** — `BusinessHoursForm` component — Editable hours per day of week
- **NEW** — `HolidayList` component — Holiday cards with add/remove and recurring support
- **NEW** — `AddHolidayDialog` component — MUI Dialog for creating holidays
- **NEW** — `useSlotTemplates` hook — React Query hook for slot template API
- **NEW** — `useBusinessHours` hook — React Query hook for business hours API
- **NEW** — `useHolidays` hook — React Query hook for holiday API
- **MODIFY** — Admin Dashboard page — Integrate SlotTemplatePanel and BusinessHoursPanel into existing tab structure

## Implementation Plan

1. Create React Query hooks (`useSlotTemplates`, `useBusinessHours`, `useHolidays`) wrapping the API endpoints from task_002 with stale-while-revalidate caching
2. Build `SlotGrid` component: render a grid (days as columns, time slots as rows) using MUI Grid; each cell is clickable to toggle available/blocked; use green background for available (success-surface token) and strikethrough neutral for blocked (neutral-100 token) per wireframe
3. Build `SlotTemplatePanel`: MUI Select for provider dropdown, SlotGrid for the selected provider, Save Template button with loading state; implement auto-save debounce (UXR-004) and optimistic updates; show conflict dialog (MUI Dialog) when API returns affected appointments
4. Build `BusinessHoursForm`: MUI Table displaying days Monday–Sunday with open/close time pickers (MUI TimePicker) and is_closed toggle; Edit Hours button reveals inline editing; validate open < close within 200ms (UXR-501)
5. Build `HolidayList`: render holiday cards with name, date, badge (Closed/Half Day), and delete icon button; delete triggers MUI confirmation dialog (UXR-102)
6. Build `AddHolidayDialog`: MUI Dialog with DatePicker, TextField for name, Checkbox for recurring, Checkbox for half-day; inline validation on submit
7. Build `BusinessHoursPanel`: two-column layout on desktop (MUI Grid breakpoints), stacked on mobile (UXR-303); left column = BusinessHoursForm, right column = HolidayList + Add Holiday button
8. Integrate panels into the admin dashboard tab structure (Slot Templates tab → SlotTemplatePanel, Hours & Holidays tab → BusinessHoursPanel)

## Current Project State

- Placeholder — to be updated based on completion of dependent tasks (task_002, US_058).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/features/admin/components/SlotTemplatePanel.tsx | Container for slot template configuration with provider selector and save |
| CREATE | app/src/features/admin/components/SlotGrid.tsx | Weekly time-block grid with toggle cells matching wireframe layout |
| CREATE | app/src/features/admin/components/BusinessHoursPanel.tsx | Container for hours and holidays with 2-column responsive layout |
| CREATE | app/src/features/admin/components/BusinessHoursForm.tsx | Editable business hours table with time pickers and closed toggle |
| CREATE | app/src/features/admin/components/HolidayList.tsx | Holiday cards with badges, delete with confirmation dialog |
| CREATE | app/src/features/admin/components/AddHolidayDialog.tsx | MUI Dialog form for adding holidays with recurring support |
| CREATE | app/src/features/admin/hooks/useSlotTemplates.ts | React Query hook for slot template CRUD API calls |
| CREATE | app/src/features/admin/hooks/useBusinessHours.ts | React Query hook for business hours API calls |
| CREATE | app/src/features/admin/hooks/useHolidays.ts | React Query hook for holiday API calls |
| MODIFY | app/src/features/admin/pages/AdminDashboard.tsx | Wire SlotTemplatePanel into Slot Templates tab and BusinessHoursPanel into Hours & Holidays tab |

## External References

- [MUI 5 — Tabs Component](https://mui.com/material-ui/react-tabs/)
- [MUI 5 — Dialog Component](https://mui.com/material-ui/react-dialog/)
- [MUI 5 — Grid Layout](https://mui.com/material-ui/react-grid/)
- [MUI 5 — TimePicker](https://mui.com/x/react-date-pickers/time-picker/)
- [MUI 5 — DatePicker](https://mui.com/x/react-date-pickers/date-picker/)
- [React Query v4 — Queries and Mutations](https://tanstack.com/query/v4/docs/react/guides/queries)
- [WCAG 2.1 AA — Keyboard Navigation](https://www.w3.org/WAI/WCAG21/Understanding/keyboard)

## Build Commands

- `cd app && npm run build`
- `cd app && npm run lint`
- `cd app && npm test`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Slot grid renders correctly with available (green) and blocked (strikethrough) states
- [ ] Provider dropdown loads provider list and switches grid data
- [ ] Save template triggers PUT API and shows success toast
- [ ] Conflict dialog displays affected appointments when API indicates conflicts
- [ ] Business hours table displays all 7 days with correct time ranges
- [ ] Holiday add dialog validates required fields within 200ms
- [ ] Holiday delete triggers confirmation dialog before API call
- [ ] Responsive layout: 2-column on desktop (>=768px), stacked on mobile (<768px)
- [ ] Keyboard navigation works for all interactive elements (tabs, grid cells, buttons, dialogs)
- [ ] Breadcrumb navigation displays correctly (Home / Admin Dashboard)

## Implementation Checklist

- [ ] Create `useSlotTemplates`, `useBusinessHours`, `useHolidays` React Query hooks with GET queries (staleTime: 5 min) and mutation hooks (PUT/POST/DELETE) with cache invalidation on success
- [ ] Build `SlotGrid` component: MUI Grid with day columns (Mon–Fri from wireframe, extend to 7 days) and time rows; each cell uses `onClick` to toggle `isAvailable`; apply `success-surface` background for available, `neutral-100` + strikethrough for blocked; add `role="grid"` and `aria-label` for accessibility
- [ ] Build `SlotTemplatePanel`: MUI Select for provider dropdown bound to provider list query; render `SlotGrid` for selected provider/day data; Save Template button with `aria-busy` loading state; implement auto-save with 2-second debounce (UXR-004); show MUI Dialog listing affected appointments on 409 conflict response, require admin confirmation to force-apply
- [ ] Build `BusinessHoursForm`: MUI Table with rows for each day (Monday–Sunday); display open_time/close_time with MUI TimePicker in edit mode and text in read mode; Toggle for is_closed; Edit Hours button switches to edit mode; inline validation (open < close) within 200ms on blur (UXR-501); Save triggers PUT with success toast
- [ ] Build `HolidayList`: map holidays to cards (flex row with name, date caption, MUI Badge for Closed/Half Day); delete IconButton triggers MUI confirmation Dialog (UXR-102) before calling DELETE API; Add Holiday button opens AddHolidayDialog
- [ ] Build `AddHolidayDialog`: MUI Dialog with MUI DatePicker (date), TextField (name, required), Checkbox (is_recurring with label "Repeats annually"), Checkbox (is_half_day with label "Half day"); validate required fields on submit; call POST API on confirm; close dialog and invalidate holidays cache on success
- [ ] Build `BusinessHoursPanel`: MUI Grid container with `xs={12} md={6}` for two-column desktop layout (UXR-303); left = BusinessHoursForm, right = HolidayList + Add Holiday button; breadcrumb navigation (UXR-003)
- [ ] Integrate `SlotTemplatePanel` into "Slot Templates" tab panel and `BusinessHoursPanel` into "Hours & Holidays" tab panel in AdminDashboard page; ensure tab ARIA attributes match wireframe (role="tablist", aria-controls, aria-selected)
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
