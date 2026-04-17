# Task - TASK_001

## Requirement Reference

- User Story: US_053
- Story Location: .propel/context/tasks/EP-009/us_053/us_053.md
- Acceptance Criteria:
    - AC-1: **Given** the staff member opens the arrival queue, **When** the dashboard loads, **Then** it displays all arrived patients sorted by appointment time and priority with sub-second response time.
    - AC-2: **Given** the queue is displayed, **When** a patient's status changes (new arrival, no-show, cancelled), **Then** the dashboard refreshes within 5 seconds without requiring manual reload.
    - AC-3: **Given** the queue dashboard is open, **When** the staff member views it, **Then** each entry shows patient name, appointment time, provider, arrival time, current wait time, and status badge (color-coded).
    - AC-4: **Given** the queue contains entries, **When** the staff member calculates average wait time, **Then** the system displays the average wait time for all currently waiting patients.
- Edge Case:
    - EC-1: When the dashboard has 100+ queue entries, system paginates with 25 entries per page and maintains sort order across pages.
    - EC-2: System uses optimistic UI updates with a "Last updated" timestamp; manual refresh button available for network latency.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-011-arrival-queue.html |
| **Screen Spec** | figma_spec.md#SCR-011 |
| **UXR Requirements** | UXR-103, UXR-401, UXR-502 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing, designsystem.md#table, designsystem.md#badge, designsystem.md#pagination |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path
> - **PENDING**: UI-impacting task awaiting wireframe (provide file or URL)
> - **EXTERNAL**: Wireframe provided via external URL
> - **N/A**: Task has no UI impact

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
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |

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

Implement the real-time Arrival Queue Dashboard UI (SCR-011) for staff members. This React component displays all arrived patients for the current day in a sortable, filterable table with color-coded status badges, live wait time timers, average wait time summary, and 5-second auto-refresh polling. The page supports pagination (25 entries/page) and provides optimistic UI updates with a "Last updated" timestamp and manual refresh button. The layout, colors, and component structure must match the Hi-Fi wireframe at `wireframe-SCR-011-arrival-queue.html`.

## Dependent Tasks

- US_052 tasks (arrival status data — provides the backend arrival marking functionality consumed by this dashboard)
- US_004 tasks (Redis caching foundation — provides the cached queue view infrastructure)
- task_003_db_queue_queries_caching (provides optimized DB queries and Redis cache layer for sub-second response)
- task_002_be_arrival_queue_api (provides the GET /queue/today API endpoint consumed by this component)

## Impacted Components

| Component | Type | Project |
|-----------|------|---------|
| ArrivalQueuePage | New | app (Frontend) |
| ArrivalQueueTable | New | app (Frontend) |
| QueueStatusBadge | New | app (Frontend) |
| WaitTimeTimer | New | app (Frontend) |
| QueueFilters | New | app (Frontend) |
| AverageWaitTimeSummary | New | app (Frontend) |
| useQueueData (hook) | New | app (Frontend) |

## Implementation Plan

1. **Create the `useQueueData` custom hook** using React Query (`useQuery`) to fetch `GET /api/queue/today` with a 5-second `refetchInterval`. Include query parameters for provider filter, status filter, and page number. Expose `data`, `isLoading`, `isError`, `dataUpdatedAt`, and a `refetch` function for manual refresh.

2. **Create the `QueueStatusBadge` component** using MUI `Chip` component. Map queue statuses to design token colors per UXR-401:
   - Waiting → `warning.main` (#ED6C02)
   - Arrived → `success.main` (#2E7D32)
   - In Visit → purple (#7B1FA2)
   - Scheduled → `info.main` (#0288D1)
   - No-Show → `error.main` (#D32F2F)
   - Urgent (priority) → `error.main` (#D32F2F)
   - Normal (priority) → `neutral.200` (#EEEEEE) with `neutral.800` text

3. **Create the `WaitTimeTimer` component** that accepts `arrivalTimestamp` and displays a live-updating `MM:SS` timer. Apply `timer-alert` styling (error surface background) when wait time exceeds 30 minutes. Use `useEffect` with a 1-second interval to update the displayed time. Show `—` dash for patients without an arrival timestamp.

4. **Create the `QueueFilters` component** with two MUI `Select` dropdowns: Provider (populated from queue data) and Status (Waiting, In Visit, No-Show, All Statuses). Emit filter changes upward via callback props.

5. **Create the `AverageWaitTimeSummary` component** that computes and displays the average wait time for all currently waiting patients from the queue data. Display as part of the page header area.

6. **Create the `ArrivalQueueTable` component** using MUI `Table` with sortable column headers (Patient, Appt Time, Wait Time). Render each row with: row number, patient name (link to SCR-013 Patient Profile 360), appointment time, provider, appointment type, wait time timer, priority badge, status badge, and action buttons (In Visit, Urgent, Arrived depending on current status). Highlight rows with wait time > 30 min using `error.surface` background. Dim no-show rows at 60% opacity.

7. **Create the `ArrivalQueuePage` container component** that composes all sub-components: breadcrumb navigation (Staff Dashboard > Arrival Queue), page header with title + queue count + "Last updated" timestamp, QueueFilters card, 30-minute threshold alert (MUI `Alert` warning variant), ArrivalQueueTable, and MUI `Pagination` at 25 items per page. Implement skeleton loading screens per UXR-502 (show skeleton placeholders for loads > 300ms).

8. **Add route configuration** for the Arrival Queue page at the staff portal navigation path. Add sidebar navigation entry matching wireframe structure (Queue item with active state using `secondary-500` color).

## Current Project State

- Project structure is placeholder — to be updated based on completion of dependent tasks.

```
app/
├── src/
│   ├── components/
│   ├── hooks/
│   ├── pages/
│   └── routes/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/hooks/useQueueData.ts | React Query hook for fetching and auto-refreshing queue data |
| CREATE | app/src/components/queue/QueueStatusBadge.tsx | Color-coded MUI Chip for queue entry statuses |
| CREATE | app/src/components/queue/WaitTimeTimer.tsx | Live-updating wait time display with 30-min alert |
| CREATE | app/src/components/queue/QueueFilters.tsx | Provider and Status filter dropdowns |
| CREATE | app/src/components/queue/AverageWaitTimeSummary.tsx | Average wait time display for waiting patients |
| CREATE | app/src/components/queue/ArrivalQueueTable.tsx | Sortable MUI Table with queue entry rows and action buttons |
| CREATE | app/src/pages/staff/ArrivalQueuePage.tsx | Container page composing all queue components |
| MODIFY | app/src/routes/staffRoutes.tsx | Add route for /staff/queue pointing to ArrivalQueuePage |
| MODIFY | app/src/components/layout/StaffSidebar.tsx | Add Queue navigation item with active state |

## External References

- [React Query v4 — useQuery with refetchInterval](https://tanstack.com/query/v4/docs/react/reference/useQuery)
- [MUI 5 Table component](https://mui.com/material-ui/react-table/)
- [MUI 5 Chip component (for badges)](https://mui.com/material-ui/react-chip/)
- [MUI 5 Select component](https://mui.com/material-ui/react-select/)
- [MUI 5 Pagination component](https://mui.com/material-ui/react-pagination/)
- [MUI 5 Alert component](https://mui.com/material-ui/react-alert/)
- [MUI 5 Skeleton component](https://mui.com/material-ui/react-skeleton/)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Queue table renders with correct sort order (appointment time, then priority)
- [ ] Status badges display correct colors per UXR-401 (Scheduled=Blue, Completed=Green, Cancelled=Gray, No-Show=Red)
- [ ] Auto-refresh triggers every 5 seconds per UXR-103
- [ ] Skeleton loading placeholders display for loads > 300ms per UXR-502
- [ ] Pagination works with 25 items/page and preserves sort order
- [ ] Wait time timer updates live every second
- [ ] 30-minute threshold alert displays when applicable
- [ ] Average wait time calculates correctly for waiting patients only

## Implementation Checklist

- [ ] Create `useQueueData` hook with React Query `useQuery`, 5-second `refetchInterval`, filter/pagination query params, and manual `refetch` function
- [ ] Create `QueueStatusBadge` component with MUI Chip mapped to appointment-status design tokens (UXR-401 color coding)
- [ ] Create `WaitTimeTimer` component with 1-second interval update, `MM:SS` format, and 30-min alert styling (error surface background)
- [ ] Create `QueueFilters` component with Provider and Status MUI Select dropdowns
- [ ] Create `AverageWaitTimeSummary` component computing mean wait time for waiting patients
- [ ] Create `ArrivalQueueTable` with sortable columns, patient name links to SCR-013, action buttons, row highlighting for wait > 30 min, and dimmed no-show rows
- [ ] Create `ArrivalQueuePage` container with breadcrumb, header, filters, alert, table, pagination, skeleton loading (UXR-502), and "Last updated" timestamp with manual refresh
- [ ] Add route `/staff/queue` and sidebar navigation entry with active state styling
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
