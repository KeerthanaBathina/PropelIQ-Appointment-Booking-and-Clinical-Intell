# Task - task_003_fe_agreement_rate_dashboard

## Requirement Reference

- User Story: US_050
- Story Location: .propel/context/tasks/EP-008/us_050/us_050.md
- Acceptance Criteria:
  - AC-2: Given the agreement rate is calculated daily, When an admin views the metrics dashboard, Then the daily and rolling 30-day agreement rate is displayed.
  - AC-3: Given a coding discrepancy exists (multiple codes for single diagnosis/procedure), When the system detects it, Then the discrepancy is flagged with a breakdown of AI suggestion vs. staff selection.
  - AC-4: Given the agreement rate drops below 98%, When the daily calculation runs, Then the system generates an alert for admin review with a summary of disagreement patterns.
- Edge Case:
  - Insufficient data: Display "Not enough data" with minimum threshold indicator when fewer than 50 verified codes per period.
  - Partial overrides displayed as disagreements in the discrepancy breakdown.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-014-medical-coding.html` |
| **Screen Spec** | figma_spec.md#SCR-014 |
| **UXR Requirements** | UXR-105, UXR-003, UXR-502, UXR-601, UXR-605 |
| **Design Tokens** | designsystem.md#colors (AI Confidence Colors), designsystem.md#typography, designsystem.md#spacing |

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

Implement the frontend UI components for the AI-human agreement rate tracking on the Medical Coding Review screen (SCR-014). This includes the agreement rate summary cards (daily and 30-day rolling), the agreement meter visualization, a coding discrepancy breakdown table showing AI suggestion vs. staff selection, and an alert banner when the rate drops below the 98% target. The UI must also handle the "Not enough data" edge case when insufficient verified codes exist. All components integrate with the backend API endpoints created in task_002.

## Dependent Tasks

- task_002_be_agreement_rate_service — Backend API endpoints must be available for data fetching.

## Impacted Components

- **NEW** — `AgreementRateSummary` component (app/components/coding/)
- **NEW** — `AgreementMeter` component (app/components/coding/)
- **NEW** — `CodingDiscrepancyTable` component (app/components/coding/)
- **NEW** — `AgreementRateAlert` component (app/components/coding/)
- **NEW** — `useAgreementRate` React Query hook (app/hooks/)
- **NEW** — `agreementRateApi` API client functions (app/api/)
- **MODIFY** — Medical Coding Review page (SCR-014) — integrate new components

## Implementation Plan

1. Create API client functions for agreement rate endpoints using axios/fetch.
2. Create `useAgreementRate` React Query hooks for data fetching with caching (5-minute TTL per NFR-030).
3. Implement `AgreementRateSummary` component — displays stat cards (agreement rate, total codes, approved, pending, overridden) matching wireframe layout.
4. Implement `AgreementMeter` component — horizontal progress bar with color-coded fill (green >= 98%, amber 90-97%, red < 90%).
5. Implement `CodingDiscrepancyTable` component — MUI Table listing flagged discrepancies with sortable columns (AI suggestion, staff selection, code type, discrepancy type).
6. Implement `AgreementRateAlert` component — MUI Alert banner displayed when rate < 98% with disagreement pattern summary.
7. Handle "Not enough data" state — display informational message with minimum threshold indicator when `meetsMinimumThreshold` is false.
8. Integrate all components into the Medical Coding Review page (SCR-014).

## Current Project State

- Placeholder — to be updated based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/api/agreementRateApi.ts | API client for agreement rate endpoints |
| CREATE | app/hooks/useAgreementRate.ts | React Query hooks for agreement rate data |
| CREATE | app/components/coding/AgreementRateSummary.tsx | Summary stat cards component |
| CREATE | app/components/coding/AgreementMeter.tsx | Agreement rate progress bar component |
| CREATE | app/components/coding/CodingDiscrepancyTable.tsx | Discrepancy breakdown table component |
| CREATE | app/components/coding/AgreementRateAlert.tsx | Below-threshold alert banner component |
| MODIFY | app/pages/MedicalCodingReview.tsx | Integrate agreement rate components into SCR-014 |

## External References

- [MUI 5 Data Grid / Table — Material UI Docs](https://mui.com/material-ui/react-table/)
- [MUI 5 Alert Component — Material UI Docs](https://mui.com/material-ui/react-alert/)
- [MUI 5 Card Component — Material UI Docs](https://mui.com/material-ui/react-card/)
- [MUI 5 Linear Progress — Material UI Docs](https://mui.com/material-ui/react-progress/#linear)
- [React Query v4 Queries — TanStack Docs](https://tanstack.com/query/v4/docs/framework/react/guides/queries)

## Build Commands

- `npm run build` (app directory)
- `npm run lint` (app directory)
- `npm run test` (app directory)

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Skeleton loading states render during data fetch (UXR-502)
- [ ] "Not enough data" state renders when `meetsMinimumThreshold` is false
- [ ] Alert banner appears when daily rate < 98%
- [ ] Discrepancy table sorts correctly by all columns
- [ ] Keyboard navigation works for all interactive elements (UXR-202, NFR-049)
- [ ] ARIA labels present on all interactive elements (UXR-203)

## Implementation Checklist

- [ ] Create `agreementRateApi.ts` with functions: `getLatestMetrics()` → GET `/api/coding/agreement-rate`, `getMetricsHistory(from, to)` → GET `/api/coding/agreement-rate/history`, `getDiscrepancies(from, to)` → GET `/api/coding/discrepancies`, `getAlerts()` → GET `/api/coding/agreement-rate/alerts`. Include typed response interfaces matching backend DTOs.
- [ ] Create `useAgreementRate.ts` with React Query hooks: `useLatestAgreementRate()` (staleTime: 5 minutes per NFR-030), `useAgreementRateHistory(from, to)`, `useDiscrepancies(from, to)`, `useAgreementAlerts()`. Include error handling for network failures and loading states.
- [ ] Implement `AgreementRateSummary` component: Display 5 stat cards in responsive grid matching wireframe layout (`coding-summary` class). Cards: AI-Human Agreement (with color — green if >= 98%, amber if 90-97%, red if < 90%), Total Codes, Approved (green), Pending Review (amber), Overridden (red). Show daily rate prominently with rolling 30-day rate as secondary text. Use MUI Card with `text-align: center`, stat value at `2rem/700 weight`, label at `0.75rem` in `neutral-600`.
- [ ] Implement `AgreementMeter` component: Horizontal progress bar (8px height) with `neutral-200` background and color-coded fill (success-500 `#2E7D32` if >= 98%, warning-500 `#ED6C02` if 90-97%, error-500 `#D32F2F` if < 90%). Use `border-radius: var(--rad-sm)`. Display percentage label above bar. Render below agreement rate stat card.
- [ ] Implement `CodingDiscrepancyTable` component: MUI Table with sortable columns — Code, AI Suggested, Staff Selected, Code Type (ICD-10/CPT), Discrepancy Type (Full Override / Partial Override / Multiple Codes), Justification (truncated with tooltip), Detected Date. Use confidence badge styling for code type. Paginate with 10 rows per page. Display empty state "No discrepancies found" when list is empty.
- [ ] Implement `AgreementRateAlert` component: MUI Alert with severity `warning` displayed when `dailyAgreementRate < 98.0` AND `meetsMinimumThreshold` is true. Content: "Agreement rate ({rate}%) is below the 98% target. Top disagreement patterns: {patterns list}." Include link to full discrepancy details. Implement "Not enough data" variant: MUI Alert with severity `info` displaying "Not enough data for agreement rate calculation. Minimum 50 verified codes required. Current: {count} codes." when `meetsMinimumThreshold` is false.
- [ ] Implement all 5 screen states for SCR-014 agreement rate section: **Default** — metrics displayed with current data; **Loading** — MUI Skeleton placeholders for all stat cards and table (UXR-502); **Empty** — "No agreement rate data available" with informational message; **Error** — actionable error message with retry button (UXR-601); **Validation** — "Not enough data" threshold indicator.
- [ ] Handle AI service unavailability (UXR-605): Display info alert "AI code suggestions are temporarily unavailable. Agreement rate reflects historical data only." when API returns service unavailable status. Ensure breadcrumb navigation (UXR-003) renders correctly: Staff Dashboard > Patient Name > Medical Coding.
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation. Validate UI matches wireframe before marking task complete. Ensure responsive behavior at 375px (stack cards vertically, table horizontal scroll), 768px (2-column card grid), 1440px (5-column card grid matching wireframe).
