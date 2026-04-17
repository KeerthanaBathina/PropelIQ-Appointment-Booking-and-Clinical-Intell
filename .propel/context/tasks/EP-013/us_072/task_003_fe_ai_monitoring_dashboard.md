# Task - task_003_fe_ai_monitoring_dashboard

## Requirement Reference

- User Story: us_072
- Story Location: .propel/context/tasks/EP-013/us_072/us_072.md
- Acceptance Criteria:
  - AC-1: Given the admin opens the AI monitoring dashboard, When it loads, Then it displays current AI-human agreement rate for medical coding with a target indicator (>98%).
  - AC-2: Given data extraction results are tracked, When the dashboard renders metrics, Then it shows precision and recall rates for data extraction with a target indicator (>95%).
  - AC-3: Given AI latency is monitored, When the dashboard displays latency, Then it shows P50 and P95 latencies for intake (<1s), document parsing (<30s), and medical coding (<5s).
  - AC-4: Given any metric drops below its target threshold, When the daily calculation runs, Then the system generates an alert with the metric name, current value, target value, and trend direction.
- Edge Case:
  - What happens when insufficient data exists for meaningful accuracy calculations? Dashboard shows "Insufficient data" with minimum sample size requirement displayed.
  - How does the system handle metrics from different time periods? Dashboard supports daily, weekly, and monthly views with date range selectors.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-015-admin-dashboard.html` |
| **Screen Spec** | figma_spec.md#SCR-015 |
| **UXR Requirements** | UXR-105 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

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
| State Management | React Query | 4.x |
| State Management | Zustand | 4.x |
| Charting | MUI X Charts or Recharts | Latest compatible |

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

Implement the frontend React dashboard for AI accuracy monitoring within the Admin Dashboard (SCR-015). The dashboard integrates as a tab/section within the existing admin configuration screen, displaying: (1) metric cards with gauge-style indicators for AI-human agreement rate (>98% target), extraction precision (>95% target), and extraction recall (>95% target); (2) latency metrics table showing P50/P95 values for intake, document parsing, and medical coding with color-coded status against targets; (3) time-series charts for trend visualization with daily/weekly/monthly granularity selectors; (4) active alerts panel listing threshold breaches with acknowledge action. All data fetched from the `api/admin/ai-metrics` REST API endpoints created in task_002.

## Dependent Tasks

- US_072 task_002_be_ai_metrics_service_api — Requires REST API endpoints returning metrics summary, time-series, and alerts data.

## Impacted Components

- **NEW** `app/src/features/admin/ai-monitoring/AiMonitoringDashboard.tsx` — Main dashboard container component with tab integration
- **NEW** `app/src/features/admin/ai-monitoring/components/AccuracyMetricCard.tsx` — Reusable metric card with gauge, value, target indicator, trend arrow
- **NEW** `app/src/features/admin/ai-monitoring/components/LatencyMetricsTable.tsx` — Table displaying P50/P95 latencies per operation type with status colors
- **NEW** `app/src/features/admin/ai-monitoring/components/MetricsTrendChart.tsx` — Time-series line chart with granularity selector (daily/weekly/monthly)
- **NEW** `app/src/features/admin/ai-monitoring/components/ActiveAlertsPanel.tsx` — Alert list with metric details and acknowledge button
- **NEW** `app/src/features/admin/ai-monitoring/hooks/useAiMetrics.ts` — React Query hooks for API data fetching
- **NEW** `app/src/features/admin/ai-monitoring/types.ts` — TypeScript interfaces matching API DTOs
- **MODIFY** `app/src/features/admin/AdminDashboard.tsx` — Add AI Monitoring tab to admin dashboard tabs

## Implementation Plan

1. **Define TypeScript interfaces**: Create types matching `AiMetricsSummaryDto`, `AiMetricsTimeSeriesDto`, `AiMetricAlertDto` from the backend API. Include `AiMetricsSummary`, `LatencyMetric`, `TimeSeriesDataPoint`, `AiMetricAlert` interfaces.

2. **Create React Query hooks**: Implement `useAiMetricsSummary()` hook calling `GET /api/admin/ai-metrics/summary` with 60-second `staleTime` for auto-refresh. Implement `useAiMetricsTimeSeries(startDate, endDate, granularity)` hook for chart data. Implement `useAiMetricAlerts()` and `useAcknowledgeAlert()` mutation hook.

3. **Build `AccuracyMetricCard` component**: MUI `Card` displaying metric name, current value as large text, circular progress gauge (0-100%), target line indicator, and trend direction arrow (↑ green, ↓ red, → gray). Color-code the value: green if >= target, amber if within 2% of target, red if below target. Show "Insufficient data" with `Typography variant="body2"` and minimum sample size when `sampleSize < 30` per edge case. Maps to UXR-105 (color-coded confidence indicators).

4. **Build `LatencyMetricsTable` component**: MUI `Table` with columns: Operation Type, P50 (ms), P95 (ms), Target P95 (ms), Status. Rows for Intake (<1000ms), Document Parsing (<30000ms), Medical Coding (<5000ms). Status column uses color-coded `Chip`: green "Within Target" when P95 <= target, red "Above Target" when P95 > target. Format milliseconds with appropriate units (ms/s).

5. **Build `MetricsTrendChart` component**: Line chart using Recharts or MUI X Charts. X-axis: dates. Y-axis: metric value. Multiple series for each metric type. Include `ToggleButtonGroup` for granularity selection (Daily/Weekly/Monthly) and `DateRangePicker` for custom date ranges per edge case. Show target threshold as horizontal dashed reference line.

6. **Build `ActiveAlertsPanel` component**: MUI `List` of `ListItem` entries. Each alert shows: metric name, current value vs target value, trend direction icon, generated timestamp. Include `Button` to acknowledge each alert (calls `PUT /api/admin/ai-metrics/alerts/{id}/acknowledge`). Show `Alert severity="info"` empty state when no active alerts.

7. **Compose `AiMonitoringDashboard` container**: Layout using MUI `Grid` with responsive breakpoints. Top row: 3 `AccuracyMetricCard` components (Coding Agreement, Extraction Precision, Extraction Recall). Middle row: `LatencyMetricsTable` spanning full width. Bottom section: `MetricsTrendChart` and `ActiveAlertsPanel` side by side on desktop, stacked on mobile. Handle loading state with `Skeleton` placeholders per UXR-502. Handle error state with `Alert` component and retry button per UXR-601.

8. **Integrate into Admin Dashboard**: Add "AI Monitoring" tab to the existing `AdminDashboard` tab navigation (SCR-015 uses `Tab` components). Render `AiMonitoringDashboard` when the tab is active. Ensure breadcrumb shows "Admin > AI Monitoring" per UXR-003.

## Current Project State

```text
UPACIP/
├── app/
│   ├── package.json
│   ├── src/
│   │   ├── App.tsx
│   │   ├── features/
│   │   │   ├── admin/
│   │   │   │   └── AdminDashboard.tsx
│   │   │   ├── auth/
│   │   │   └── patient/
│   │   ├── components/
│   │   │   └── shared/
│   │   └── hooks/
├── src/
│   ├── UPACIP.Api/
│   │   └── Controllers/Admin/
│   │       └── AiMetricsController.cs  ← from task_002
│   └── UPACIP.Service/
│       └── AiMetrics/                  ← from task_002
└── scripts/
```

> Assumes task_002 (backend API) is completed and API endpoints are available.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/features/admin/ai-monitoring/types.ts | TypeScript interfaces for AiMetricsSummary, LatencyMetric, TimeSeriesDataPoint, AiMetricAlert |
| CREATE | app/src/features/admin/ai-monitoring/hooks/useAiMetrics.ts | React Query hooks: useAiMetricsSummary, useAiMetricsTimeSeries, useAiMetricAlerts, useAcknowledgeAlert |
| CREATE | app/src/features/admin/ai-monitoring/components/AccuracyMetricCard.tsx | Metric card with gauge, value, target, trend, insufficient-data state |
| CREATE | app/src/features/admin/ai-monitoring/components/LatencyMetricsTable.tsx | Table with P50/P95 latencies per operation, color-coded status chips |
| CREATE | app/src/features/admin/ai-monitoring/components/MetricsTrendChart.tsx | Time-series chart with granularity selector and date range picker |
| CREATE | app/src/features/admin/ai-monitoring/components/ActiveAlertsPanel.tsx | Alert list with acknowledge action and empty state |
| CREATE | app/src/features/admin/ai-monitoring/AiMonitoringDashboard.tsx | Container layout with responsive grid, loading/error states |
| MODIFY | app/src/features/admin/AdminDashboard.tsx | Add "AI Monitoring" tab to existing admin tab navigation |

## External References

- [MUI Tabs API](https://mui.com/material-ui/react-tabs/)
- [MUI Card API](https://mui.com/material-ui/react-card/)
- [MUI Table API](https://mui.com/material-ui/react-table/)
- [React Query useQuery](https://tanstack.com/query/v4/docs/framework/react/reference/useQuery)
- [Recharts Line Chart](https://recharts.org/en-US/api/LineChart)
- [MUI Grid v2](https://mui.com/material-ui/react-grid2/)
- [WCAG 2.1 AA Color Contrast](https://www.w3.org/WAI/WCAG21/Understanding/contrast-minimum.html)

## Build Commands

```powershell
# Install dependencies
cd app; npm install

# Build frontend
npm run build

# Run development server
npm run dev

# Run lint checks
npm run lint
```

## Implementation Validation Strategy

- [ ] `npm run build` completes with zero errors
- [ ] `npm run lint` passes with zero violations
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] AccuracyMetricCard shows green/amber/red color coding based on threshold proximity
- [ ] AccuracyMetricCard shows "Insufficient data" when sample size < 30
- [ ] LatencyMetricsTable renders all 3 operation types with correct target values
- [ ] MetricsTrendChart supports Daily/Weekly/Monthly granularity toggle
- [ ] ActiveAlertsPanel acknowledge button calls API and removes alert from list
- [ ] Dashboard renders responsive layout: side-by-side on desktop, stacked on mobile
- [ ] Loading state shows Skeleton placeholders per UXR-502
- [ ] Error state shows Alert with retry button per UXR-601

## Implementation Checklist

- [ ] Define TypeScript interfaces in `types.ts` matching backend DTOs: `AiMetricsSummary`, `LatencyMetric`, `TimeSeriesDataPoint`, `AiMetricAlert`
- [ ] Create React Query hooks in `useAiMetrics.ts`: `useAiMetricsSummary` (60s staleTime), `useAiMetricsTimeSeries`, `useAiMetricAlerts`, `useAcknowledgeAlert` mutation
- [ ] Build `AccuracyMetricCard` with gauge, value display, target indicator, trend arrow, color-coding (green/amber/red), and "Insufficient data" state
- [ ] Build `LatencyMetricsTable` with P50/P95 columns, operation type rows, color-coded status `Chip` components
- [ ] Build `MetricsTrendChart` with line chart, granularity `ToggleButtonGroup`, date range selector, and target threshold reference line
- [ ] Build `ActiveAlertsPanel` with alert list, acknowledge button, empty state, and metric details display
- [ ] Compose `AiMonitoringDashboard` container with responsive MUI `Grid`, loading `Skeleton`, and error `Alert` states
- [ ] Integrate "AI Monitoring" tab into `AdminDashboard.tsx` with breadcrumb support per UXR-003
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
