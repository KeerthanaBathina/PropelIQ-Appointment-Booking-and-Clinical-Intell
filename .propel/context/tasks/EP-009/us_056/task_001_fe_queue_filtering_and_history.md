# Task - TASK_001

## Requirement Reference

- User Story: US_056
- Story Location: .propel/context/tasks/EP-009/us_056/us_056.md
- Acceptance Criteria:
    - AC-1: Given the queue dashboard is displayed, When the staff member selects a provider filter, Then the queue shows only entries for the selected provider.
    - AC-2: Given the queue dashboard is displayed, When the staff member applies multiple filters (provider + appointment type + status), Then filters are combined with AND logic and results update instantly.
    - AC-3: Given the staff member navigates to queue history, When they select a date range, Then the system displays historical queue data including average wait times, no-show counts, and patient throughput.
    - AC-4: Given queue history data is available, When the staff member requests an export, Then the system generates a CSV report with queue metrics for the selected period.
- Edge Cases:
    - When no queue entries match the applied filters, the system displays an empty state message "No patients match the selected filters" with a clear-all-filters button.
    - When queue history is requested for dates before system deployment, the system shows "No data available for the selected period" with available date range indicated.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-011-arrival-queue.html` |
| **Screen Spec** | figma_spec.md#SCR-011 |
| **UXR Requirements** | UXR-103, UXR-003, UXR-005, UXR-206, UXR-401 |
| **Design Tokens** | designsystem.md#colors (appointment-status), designsystem.md#typography, designsystem.md#spacing |

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
| Backend | N/A | - |
| Database | N/A | - |
| AI/ML | N/A | - |
| Vector Store | N/A | - |
| AI Gateway | N/A | - |
| Mobile | N/A | - |

**Note**: All code and libraries MUST be compatible with versions above.

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

Implement queue filtering controls and queue history view on the Arrival Queue Dashboard (SCR-011). This task adds provider, appointment type, and arrival status filter dropdowns with AND logic combination, a "Queue History" tab/view with date range picker displaying historical analytics (average wait times, no-show counts, patient throughput), and a CSV export button. The empty state for no matching filters and unavailable date ranges must also be handled. All filters should update the queue table instantly on the client using React Query cached data.

## Dependent Tasks

- US_053 tasks (Real-Time Arrival Queue Dashboard) — Requires the base queue dashboard UI, queue table component, and React Query hooks for fetching queue data.
- US_008 tasks (Core Domain Entity Models) — Requires QueueEntry entity model for TypeScript type definitions.

## Impacted Components

- **New**: `QueueFilterBar` component — Filter controls for provider, appointment type, and status (app/src/features/queue/)
- **New**: `QueueHistoryView` component — Historical analytics view with date range picker, metrics cards, and data table (app/src/features/queue/)
- **New**: `QueueHistoryExportButton` component — CSV export trigger button (app/src/features/queue/)
- **New**: `useQueueFilters` hook — Filter state management and query parameter synchronization (app/src/features/queue/hooks/)
- **New**: `useQueueHistory` hook — React Query hook for fetching queue history data (app/src/features/queue/hooks/)
- **Modify**: Queue dashboard page — Add filter bar integration and history tab/navigation (app/src/features/queue/)
- **New**: `queueHistory.api.ts` — API client functions for queue history and export endpoints (app/src/features/queue/api/)
- **New**: TypeScript types for QueueFilterParams, QueueHistoryResponse, QueueMetrics (app/src/features/queue/types/)

## Implementation Plan

1. **Define TypeScript types** for filter parameters (`QueueFilterParams`: provider, appointmentType, status), queue history response (`QueueHistoryResponse`: date range, metrics array), and queue metrics (`QueueMetrics`: avgWaitTime, noShowCount, throughput).
2. **Create `QueueFilterBar` component** with three MUI `Select` dropdowns (Provider, Appointment Type, Status) matching the wireframe layout. Populate provider options from existing queue data. Apply AND logic by passing all selected filter values to the parent query.
3. **Create `useQueueFilters` custom hook** using Zustand for local filter state. Expose `filters`, `setFilter`, `clearAllFilters` methods. Synchronize filter values with URL search params for shareable filter state.
4. **Integrate `QueueFilterBar` into queue dashboard** above the queue table (matching wireframe card layout). Wire filter changes to React Query's `useQuery` refetch with updated filter parameters via query key invalidation.
5. **Create `QueueHistoryView` component** with MUI `DatePicker` (start/end date range), three summary metric cards (Average Wait Time, No-Show Count, Patient Throughput), and a data table showing daily historical queue entries.
6. **Create `useQueueHistory` hook** using React Query to call `GET /api/queue/history?startDate=&endDate=`. Handle loading, error, and empty states.
7. **Create `QueueHistoryExportButton` component** that triggers `GET /api/queue/history/export?startDate=&endDate=` and initiates CSV file download via browser blob download.
8. **Implement empty states**: "No patients match the selected filters" with "Clear All Filters" button when filtered queue returns zero results; "No data available for the selected period" with available date range hint when history returns empty.

**Focus on how to implement:**

- Use MUI `Select` with `FormControl`/`InputLabel` for filter dropdowns, matching the wireframe's `.queue-filters` layout.
- Use React Query `useQuery` with filter params in the query key to auto-refetch on filter change.
- Use MUI `DatePicker` from `@mui/x-date-pickers` for date range selection in history view.
- Use MUI `Card`, `Table`, `Badge`, `Alert` components matching designsystem.md specifications.
- Implement CSV download using `Blob` + `URL.createObjectURL` + `<a>` click pattern for browser-native download.
- Apply appointment status color coding per designsystem.md: Scheduled=#1976D2, Arrived=#2E7D32, In-Visit=#7B1FA2, Cancelled=#757575, No-Show=#D32F2F.
- Use `aria-label` attributes and `role="status"` live regions for screen reader announcements (UXR-206).

## Current Project State

- [Placeholder — to be updated based on completion of dependent US_053 tasks]

```text
app/
├── src/
│   ├── features/
│   │   └── queue/          # Queue feature module (from US_053)
│   │       ├── components/
│   │       ├── hooks/
│   │       ├── api/
│   │       └── types/
│   ├── shared/
│   └── ...
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/features/queue/types/queueFilters.types.ts | TypeScript interfaces for QueueFilterParams, QueueHistoryResponse, QueueMetrics |
| CREATE | app/src/features/queue/hooks/useQueueFilters.ts | Zustand-based filter state hook with URL param sync |
| CREATE | app/src/features/queue/hooks/useQueueHistory.ts | React Query hook for queue history API |
| CREATE | app/src/features/queue/components/QueueFilterBar.tsx | Filter dropdowns (Provider, Appointment Type, Status) |
| CREATE | app/src/features/queue/components/QueueHistoryView.tsx | History analytics view with date range, metrics, table |
| CREATE | app/src/features/queue/components/QueueHistoryExportButton.tsx | CSV export button with blob download |
| CREATE | app/src/features/queue/api/queueHistory.api.ts | API client for history and export endpoints |
| MODIFY | app/src/features/queue/pages/QueueDashboardPage.tsx | Integrate QueueFilterBar above table; add History tab navigation |

## External References

- [MUI Select API (v5)](https://mui.com/material-ui/api/select/)
- [MUI DatePicker (v5)](https://mui.com/x/react-date-pickers/date-picker/)
- [React Query useQuery docs (v4)](https://tanstack.com/query/v4/docs/react/reference/useQuery)
- [Zustand v4 documentation](https://docs.pmnd.rs/zustand/getting-started/introduction)
- [MDN Blob download pattern](https://developer.mozilla.org/en-US/docs/Web/API/Blob)
- [WCAG 2.1 AA Live Regions](https://www.w3.org/WAI/WCAG21/Understanding/status-messages.html)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Filter by single provider shows only that provider's queue entries
- [ ] Multiple combined filters (provider + type + status) apply AND logic correctly
- [ ] Queue history date range picker loads and displays historical metrics
- [ ] CSV export triggers browser download with correct data
- [ ] Empty state displays "No patients match the selected filters" with clear-all button
- [ ] Empty history state displays "No data available for the selected period"
- [ ] Screen reader announcements fire on filter result changes (UXR-206)
- [ ] Status badge colors match designsystem.md appointment-status palette (UXR-401)

## Implementation Checklist

- [ ] Define TypeScript types for QueueFilterParams, QueueHistoryResponse, QueueMetrics
- [ ] Create QueueFilterBar component with Provider, Appointment Type, Status MUI Select dropdowns
- [ ] Create useQueueFilters Zustand hook with filter state and URL param sync
- [ ] Integrate QueueFilterBar into QueueDashboardPage above queue table
- [ ] Create QueueHistoryView component with DatePicker range, metric cards, and history table
- [ ] Create useQueueHistory React Query hook for GET /api/queue/history endpoint
- [ ] Create QueueHistoryExportButton with CSV blob download for GET /api/queue/history/export
- [ ] Implement empty states for no filter matches and unavailable history date ranges
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
