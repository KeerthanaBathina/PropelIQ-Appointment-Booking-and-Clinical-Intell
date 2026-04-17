# Task - TASK_001

## Requirement Reference

- User Story: US_058
- Story Location: .propel/context/tasks/EP-010/us_058/us_058.md
- Acceptance Criteria:
    - AC-1: **Given** the admin logs in, **When** the admin dashboard loads, **Then** it displays system metrics (active users, daily appointments, no-show rate, AI agreement rate, uptime percentage).
    - AC-2: **Given** the metrics section is displayed, **When** the admin views trends, **Then** the system shows rolling 7-day and 30-day trend charts for key metrics.
- Edge Case:
    - What happens when system metrics data is temporarily unavailable? Dashboard shows cached values with a "Data as of [timestamp]" indicator.
    - How does the system handle the admin dashboard on mobile screens? Dashboard collapses metrics into collapsible cards and stacks configuration sections vertically.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-015-admin-dashboard.html` |
| **Screen Spec** | figma_spec.md#SCR-015 |
| **UXR Requirements** | UXR-003, UXR-403, UXR-502 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

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
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Database | PostgreSQL | 16.x |

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

Implement the system metrics overview and trend charts section of the admin dashboard (SCR-015). This task covers the top-level metrics display showing five key performance indicators (active users, daily appointments, no-show rate, AI agreement rate, uptime percentage) and the rolling 7-day/30-day trend chart visualizations. Includes skeleton loading states (UXR-502), breadcrumb navigation (UXR-003), admin visual treatment using error/secondary color accent (UXR-403), stale data indicators for unavailable metrics, and responsive collapsible card layout for mobile viewports.

## Dependent Tasks

- US_001 — Foundational — Requires backend API scaffold (React project setup, routing, auth context)
- US_008 — Foundational — Requires all domain entities for metrics computation
- task_005_db_admin_metrics_config_schema — Requires system_metrics and related schema
- task_003_be_admin_metrics_trends_api — Requires GET /admin/metrics and GET /admin/metrics/trends endpoints

## Impacted Components

- **NEW** `app/src/pages/AdminDashboard/AdminDashboardPage.tsx` — Main admin dashboard page component
- **NEW** `app/src/components/admin/MetricsOverview.tsx` — Metrics cards grid component
- **NEW** `app/src/components/admin/MetricCard.tsx` — Individual metric card with value, label, trend indicator
- **NEW** `app/src/components/admin/TrendChart.tsx` — Line chart component with 7-day/30-day toggle
- **NEW** `app/src/hooks/useAdminMetrics.ts` — React Query hook for fetching metrics and trends data
- **NEW** `app/src/services/adminMetricsService.ts` — API service layer for admin metrics endpoints
- **MODIFY** `app/src/routes/index.tsx` — Add admin dashboard route with role guard

## Implementation Plan

1. **Create AdminDashboardPage layout** — Set up the page shell with breadcrumb navigation (Home > Admin Dashboard per UXR-003), admin header, and two main sections: metrics overview and trend charts. Apply admin visual treatment using `error` palette from MUI theme as accent (UXR-403, matching wireframe `--error-500` sidebar accent).

2. **Build MetricsOverview grid component** — Create a responsive grid of 5 MetricCard components for: Active Users, Daily Appointments, No-Show Rate, AI Agreement Rate, and Uptime Percentage. Use MUI `Grid` with `xs=12 sm=6 md=4 lg=2.4` breakpoints. Each card displays the metric value, label, and a directional trend indicator (up/down arrow with percentage change).

3. **Implement MetricCard with skeleton loading** — Create a reusable card component using MUI `Card` that supports three states: loading (MUI `Skeleton` placeholders per UXR-502 for loads >300ms), data-available (value + label + trend), and stale-data (value displayed with "Data as of [timestamp]" caption in `warning.main` color per edge case). Use MUI `Typography` variants: `h4` for value, `body2` for label, `caption` for timestamp.

4. **Build TrendChart component** — Integrate a lightweight chart library compatible with React 18 (e.g., Recharts 2.x or MUI X Charts) to render line charts. Include a `ToggleButtonGroup` for switching between 7-day and 30-day views. Chart displays trend lines for each of the 5 key metrics. Apply design tokens: primary-500 for main line, neutral-300 for grid, neutral-700 for axis labels.

5. **Create useAdminMetrics React Query hook** — Implement `useQuery` hooks for `GET /admin/metrics` (current snapshot) and `GET /admin/metrics/trends?period=7d|30d` (historical data). Configure 5-minute stale time matching backend Redis cache TTL. Handle error state by displaying last cached value with stale indicator.

6. **Implement responsive mobile layout** — On viewports <600px (MUI `sm` breakpoint), collapse metrics cards into vertically stacked collapsible `Accordion` components (per edge case). Trend chart section stacks below metrics. Use MUI `useMediaQuery` hook for breakpoint detection.

7. **Add admin route guard** — Register `/admin/dashboard` route in the application router. Wrap with role-based guard checking for `admin` role from auth context. Redirect unauthorized users to appropriate dashboard.

## Current Project State

- Project is in planning phase. No `app/` or `Server/` folders exist yet.
- Frontend scaffold will be established by US_001 (dependency).
- Placeholder to be updated during task execution based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/src/pages/AdminDashboard/AdminDashboardPage.tsx` | Main admin dashboard page with metrics overview and trend chart sections |
| CREATE | `app/src/components/admin/MetricsOverview.tsx` | Responsive grid of 5 metric cards |
| CREATE | `app/src/components/admin/MetricCard.tsx` | Reusable metric card with loading, data, and stale states |
| CREATE | `app/src/components/admin/TrendChart.tsx` | Line chart with 7-day/30-day toggle |
| CREATE | `app/src/hooks/useAdminMetrics.ts` | React Query hooks for metrics and trends API calls |
| CREATE | `app/src/services/adminMetricsService.ts` | API service functions for admin metrics endpoints |
| MODIFY | `app/src/routes/index.tsx` | Add /admin/dashboard route with admin role guard |

## External References

- [React 18 Documentation](https://react.dev/)
- [MUI 5 Card Component](https://mui.com/material-ui/react-card/)
- [MUI 5 Skeleton Component](https://mui.com/material-ui/react-skeleton/)
- [MUI 5 Grid2 Layout](https://mui.com/material-ui/react-grid2/)
- [MUI 5 Accordion Component](https://mui.com/material-ui/react-accordion/)
- [React Query v4 Documentation](https://tanstack.com/query/v4/docs/)
- [Recharts 2.x Documentation](https://recharts.org/en-US/)
- [MUI 5 useMediaQuery Hook](https://mui.com/material-ui/react-use-media-query/)

## Build Commands

- Refer to applicable technology stack specific build commands in `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] MetricsOverview renders 5 metric cards with correct labels and values
- [ ] Skeleton loading appears for >300ms load times (UXR-502)
- [ ] Stale data indicator displays "Data as of [timestamp]" when metrics unavailable
- [ ] TrendChart toggles between 7-day and 30-day views correctly
- [ ] Mobile layout collapses metrics into collapsible accordion cards below 600px
- [ ] Breadcrumb navigation renders correctly (UXR-003)
- [ ] Admin role guard redirects non-admin users

## Implementation Checklist

- [ ] Create `AdminDashboardPage` page component with breadcrumb navigation and admin accent styling
- [ ] Build `MetricsOverview` grid displaying 5 `MetricCard` components for key metrics
- [ ] Implement `MetricCard` with skeleton loading (UXR-502) and stale-data indicator states
- [ ] Build `TrendChart` component with line chart and 7-day/30-day `ToggleButtonGroup`
- [ ] Create `useAdminMetrics` React Query hook with 5-minute stale time and error fallback
- [ ] Implement responsive mobile layout with collapsible `Accordion` cards below `sm` breakpoint
- [ ] Add `/admin/dashboard` route with admin role authorization guard
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
