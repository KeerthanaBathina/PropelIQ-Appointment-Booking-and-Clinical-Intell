# Task - TASK_001

## Requirement Reference

- User Story: us_057
- Story Location: .propel/context/tasks/EP-010/us_057/us_057.md
- Acceptance Criteria:
    - AC-1: Given the staff member logs in, When the staff dashboard loads, Then it displays today's appointment schedule, arrival queue count, and pending tasks (unverified codes, flagged conflicts, intake reviews).
    - AC-2: Given the daily schedule is displayed, When appointments are listed, Then each shows patient name, time, type, status badge (color-coded), and no-show risk score.
    - AC-3: Given pending tasks are displayed, When the staff member clicks a task, Then they are navigated to the relevant screen (SCR-014 for coding, SCR-013 for conflicts, SCR-012 for parsing).
    - AC-4: Given the dashboard is loaded, When data changes occur (new arrival, status change), Then the dashboard updates within 5 seconds without manual refresh.
- Edge Cases:
    - No appointments scheduled: Display empty schedule with "No appointments today" message and link to appointment management view.
    - Multiple staff viewing same patient changes: Real-time updates via cached views ensure all staff see consistent data within 5 seconds.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-010-staff-dashboard.html |
| **Screen Spec** | figma_spec.md#SCR-010 |
| **UXR Requirements** | UXR-003, UXR-005, UXR-401, UXR-403, UXR-502 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#appointment-status-colors |

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
| Backend | N/A (consumed via API) | - |
| Database | N/A | - |
| AI/ML | N/A | - |

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

Implement the Staff Dashboard UI (SCR-010) as a React component using MUI 5. The dashboard provides a single-view operational hub for staff members showing today's appointment schedule with color-coded status badges and no-show risk scores, summary statistic cards (appointments count, queue count, pending reviews, completed count), quick-action buttons (Walk-in Registration, View Queue), a pending tasks panel with navigation to coding/conflicts/parsing screens, and a patient search bar. Real-time data refresh must occur within 5 seconds via React Query polling. All 5 screen states (Default, Loading, Empty, Error, Validation) must be implemented per figma_spec.md.

## Dependent Tasks

- US_008 (EP-DATA) — All domain entities must exist for dashboard API queries
- US_004 (EP-TECH) — Redis caching layer must be configured for real-time cached views
- task_002_be_staff_dashboard_api (US_057) — Backend API endpoints must be available to consume

## Impacted Components

- **NEW** `app/src/pages/StaffDashboard/StaffDashboard.tsx` — Main dashboard page component
- **NEW** `app/src/pages/StaffDashboard/components/ScheduleTable.tsx` — Today's appointments table with status badges and no-show risk
- **NEW** `app/src/pages/StaffDashboard/components/StatCards.tsx` — Summary stat cards (appointments, queue, pending, completed)
- **NEW** `app/src/pages/StaffDashboard/components/PendingTasksPanel.tsx` — Pending tasks list with navigation links
- **NEW** `app/src/pages/StaffDashboard/components/QuickActions.tsx` — Walk-in Registration and View Queue buttons
- **NEW** `app/src/hooks/useStaffDashboard.ts` — React Query hook for dashboard data fetching with 5-second polling
- **NEW** `app/src/types/staffDashboard.ts` — TypeScript interfaces for dashboard API response
- **MODIFY** `app/src/routes/index.tsx` — Add staff dashboard route `/staff/dashboard`

## Implementation Plan

1. **Define TypeScript interfaces** for dashboard API response payload: `StaffDashboardData`, `ScheduleAppointment`, `PendingTask`, `DashboardStats`.
2. **Create React Query hook** (`useStaffDashboard`) calling `GET /api/staff/dashboard` with `refetchInterval: 5000` for real-time polling (AC-4).
3. **Build StatCards component** rendering 4 MUI Cards in a responsive grid: Today's Appointments, In Queue, Pending Reviews, Completed Today. Use `secondary-500`, `warning-main`, `info-main`, `success-main` colors per wireframe.
4. **Build QuickActions component** with MUI Button for "Walk-in Registration" (primary) triggering a modal placeholder and "View Queue" (secondary) linking to SCR-011 route.
5. **Build ScheduleTable component** using MUI Table. Each row: appointment time, patient name (linked to SCR-013), appointment type, color-coded status Badge (using `appointment-status` design tokens: Scheduled=Blue `#1976D2`, Completed=Green `#388E3C`, In Visit=Purple `#7B1FA2`, Waiting=Info `#0288D1`, No-show=Red `#D32F2F`, Cancelled=Gray `#757575`), and no-show risk score (AC-2).
6. **Build PendingTasksPanel component** rendering task items. Each item: task category (Document Review, Code Approval, Conflict Resolution), patient name, description. Click navigates to SCR-014 (`/staff/coding`) for code tasks, SCR-013 (`/staff/patient/:id`) for conflicts/documents, SCR-012 (`/staff/documents`) for parsing (AC-3).
7. **Compose StaffDashboard page** with MUI-based layout: sidebar navigation (active on Dashboard), header with breadcrumb (UXR-003), patient search bar (UXR-005), user avatar. Main content: stat cards grid → quick actions → two-column grid (schedule table | pending tasks).
8. **Implement all 5 screen states**: Default (data loaded), Loading (MUI Skeleton placeholders per UXR-502, shown for >300ms), Empty (no appointments message with CTA per edge case), Error (MUI Alert with retry button), Validation (N/A for read-only dashboard).

**Focus on how to implement:**
- Use MUI `<Skeleton>` variants for loading state to match UXR-502 (skeleton placeholders for >300ms loads)
- Use `useNavigate` from React Router for task click navigation (AC-3)
- Staff screens use secondary color accent (`#7B1FA2`) per UXR-403
- Implement breadcrumb navigation per UXR-003
- Patient search bar always visible in header per UXR-005
- Use responsive `Grid` from MUI: single-column at 375px, two-column at 768px+, full layout at 1440px

## Current Project State

```
[Placeholder — to be updated based on dependent task completion]
app/
├── src/
│   ├── pages/
│   │   └── StaffDashboard/         (NEW)
│   │       ├── StaffDashboard.tsx
│   │       └── components/
│   │           ├── ScheduleTable.tsx
│   │           ├── StatCards.tsx
│   │           ├── PendingTasksPanel.tsx
│   │           └── QuickActions.tsx
│   ├── hooks/
│   │   └── useStaffDashboard.ts    (NEW)
│   ├── types/
│   │   └── staffDashboard.ts       (NEW)
│   └── routes/
│       └── index.tsx               (MODIFY)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/pages/StaffDashboard/StaffDashboard.tsx | Main dashboard page composing all sub-components, layout, header, sidebar |
| CREATE | app/src/pages/StaffDashboard/components/ScheduleTable.tsx | Today's appointments table with status badges and no-show risk score |
| CREATE | app/src/pages/StaffDashboard/components/StatCards.tsx | 4 summary statistic cards in responsive grid |
| CREATE | app/src/pages/StaffDashboard/components/PendingTasksPanel.tsx | Pending tasks list with click-to-navigate to SCR-012/013/014 |
| CREATE | app/src/pages/StaffDashboard/components/QuickActions.tsx | Walk-in Registration and View Queue action buttons |
| CREATE | app/src/hooks/useStaffDashboard.ts | React Query hook with 5s polling for dashboard data |
| CREATE | app/src/types/staffDashboard.ts | TypeScript interfaces for API response types |
| MODIFY | app/src/routes/index.tsx | Add `/staff/dashboard` route with role guard (Staff only) |

## External References

- [React Query v4 — Polling/Refetching](https://tanstack.com/query/v4/docs/react/guides/important-defaults)
- [MUI 5 — Table component](https://mui.com/material-ui/react-table/)
- [MUI 5 — Skeleton component](https://mui.com/material-ui/react-skeleton/)
- [MUI 5 — Card component](https://mui.com/material-ui/react-card/)
- [MUI 5 — Grid layout](https://mui.com/material-ui/react-grid/)
- [MUI 5 — Badge/Chip component](https://mui.com/material-ui/react-chip/)

## Build Commands

- `cd app && npm install` — Install dependencies
- `cd app && npm run build` — Build production bundle
- `cd app && npm run dev` — Start development server

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] All 5 screen states render correctly (Default, Loading, Empty, Error, Validation)
- [ ] React Query polling fires every 5 seconds (AC-4)
- [ ] Task click navigation routes to correct screens (AC-3)
- [ ] Status badges use correct appointment-status colors from designsystem.md
- [ ] Breadcrumb navigation renders on dashboard (UXR-003)
- [ ] Patient search bar visible in header (UXR-005)
- [ ] Staff secondary accent color applied (UXR-403)
- [ ] Skeleton loading shown for >300ms loads (UXR-502)
- [ ] Responsive layout verified: 375px (single-col), 768px (two-col), 1440px (full)
- [ ] Empty state shows "No appointments today" with CTA link
- [ ] WCAG 2.1 AA: keyboard navigation, ARIA labels on table, search, buttons (NFR-046)

## Implementation Checklist

- [ ] Create `staffDashboard.ts` TypeScript interfaces (`StaffDashboardData`, `ScheduleAppointment`, `PendingTask`, `DashboardStats`)
- [ ] Create `useStaffDashboard.ts` React Query hook with `GET /api/staff/dashboard` and `refetchInterval: 5000`
- [ ] Implement `StatCards.tsx` — 4 MUI Cards with stat values and labels in responsive grid
- [ ] Implement `QuickActions.tsx` — Walk-in Registration (primary btn) and View Queue (secondary btn, links to SCR-011)
- [ ] Implement `ScheduleTable.tsx` — MUI Table with columns: Time, Patient (linked), Type, Status (color-coded Chip), No-Show Risk
- [ ] Implement `PendingTasksPanel.tsx` — Task list items with category, patient, description, and navigate-on-click to SCR-012/013/014
- [ ] Compose `StaffDashboard.tsx` — Sidebar nav, header (breadcrumb + search + avatar), main content grid
- [ ] Implement Loading state with MUI Skeleton placeholders (cards, table rows, task items)
- [ ] Implement Empty state ("No appointments today" message with link to appointment management)
- [ ] Implement Error state (MUI Alert with retry action)
- [ ] Add route `/staff/dashboard` with Staff role guard in routes/index.tsx
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
