# Task - task_001_fe_noshow_wait_alerts_ui

## Requirement Reference
- User Story: US_055
- Story Location: .propel/context/tasks/EP-009/us_055/us_055.md
- Acceptance Criteria:
    - AC-2: Given a patient has been waiting, When their wait time exceeds the configurable threshold (default 30 minutes), Then the system displays a visual alert (amber/red badge) on the queue entry and notifies the staff member.
    - AC-3: Given the wait time threshold is configurable, When an admin changes the threshold value, Then the alert behavior updates immediately for all active queue entries.
- Edge Case:
    - Patient arrives at 14 minutes (just before auto no-show): The 15-minute timer resets upon arrival marking; auto no-show is cancelled. UI must reflect the cancelled no-show state immediately.

## Design References (Frontend Tasks Only)
| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-011-arrival-queue.html` |
| **Screen Spec** | figma_spec.md#SCR-011 |
| **UXR Requirements** | UXR-103, UXR-206, UXR-401 |
| **Design Tokens** | designsystem.md#colors (appointment-status, semantic), designsystem.md#typography |

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
| Backend | N/A | N/A |
| Database | N/A | N/A |
| AI/ML | N/A | N/A |

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
Implement the frontend UI components for auto no-show detection visual feedback and configurable wait time threshold alerts on the Arrival Queue Dashboard (SCR-011). This task adds amber/red visual badge indicators for patients waiting beyond the configurable threshold, displays "Auto-marked" no-show badges for auto-detected no-shows, and ensures real-time UI updates when the threshold configuration changes. The queue table must refresh dynamically (polling every 5 seconds per UXR-103) to reflect backend-driven no-show and wait alert state changes.

## Dependent Tasks
- US_052 tasks (EP-009/us_052/) — Arrival Queue Dashboard base UI with queue table, status badges, and real-time polling must be implemented first.
- task_002_be_noshow_detection_service.md — Backend API endpoints for threshold config retrieval and wait alert data must be available.
- task_003_db_noshow_threshold_config.md — Database schema for threshold configuration must exist.

## Impacted Components
- **NEW** `WaitTimeAlertBadge` component — Renders amber (warning) or red (error) badge when wait time exceeds threshold (app/src/components/queue/)
- **NEW** `NoShowAutoBadge` component — Renders "Auto-marked" label with red no-show badge and "delayed-detection" flag indicator (app/src/components/queue/)
- **MODIFY** `QueueTable` component — Integrate WaitTimeAlertBadge and NoShowAutoBadge into queue row rendering (app/src/components/queue/)
- **MODIFY** `QueueDashboard` page — Add threshold-exceeded alert banner (amber warning alert) at top of queue (app/src/pages/staff/)
- **NEW** `useWaitThreshold` hook — Fetches current wait time threshold config from API and provides to queue components (app/src/hooks/)
- **MODIFY** Queue Zustand store — Add threshold config state and alert visibility flags

## Implementation Plan
1. **Create `useWaitThreshold` hook**: Fetch the configurable wait time threshold from `GET /api/queue/config/threshold`. Use React Query with a 30-second refetch interval so admin changes propagate within 30 seconds. Return `{ thresholdMinutes, isLoading, error }`.
2. **Create `WaitTimeAlertBadge` component**: Accept `waitTimeMinutes` and `thresholdMinutes` props. Render MUI `Chip` with:
   - **Amber badge** (warning color `#ED6C02`): when `waitTimeMinutes >= thresholdMinutes` and `waitTimeMinutes < thresholdMinutes * 1.5`
   - **Red badge** (error color `#D32F2F`): when `waitTimeMinutes >= thresholdMinutes * 1.5`
   - No badge when below threshold
   - Include `aria-label` for screen reader: "Patient waiting {X} minutes, exceeds threshold of {Y} minutes"
3. **Create `NoShowAutoBadge` component**: Accept `isAutoDetected` and `isDelayedDetection` props. Render MUI `Chip` with error color and label "No-Show (Auto)" or "No-Show (Auto - Delayed)" when `isDelayedDetection` is true. Row should have reduced opacity (0.6) per wireframe pattern.
4. **Modify `QueueTable` component**: In each queue row, integrate `WaitTimeAlertBadge` in the Wait Time column and `NoShowAutoBadge` in the Status column. Apply `error-surface` background (`#FFEBEE`) to rows with wait time exceeding threshold (per wireframe pattern for urgent/alert rows).
5. **Add threshold-exceeded alert banner**: At the top of the queue dashboard (above the table), display MUI `Alert` severity="warning" when any patient exceeds the threshold. Text: "⚠ {N} patient(s) waiting over {threshold} minutes." Use ARIA `role="alert"` for screen reader announcements (UXR-206).
6. **Update Zustand store**: Add `waitThresholdMinutes` field to queue store. Update on threshold fetch. Components subscribe to this state for consistent threshold application.
7. **Ensure real-time updates**: Existing 5-second polling (UXR-103) from US_052 fetches updated queue data including `auto_noshow` flags and computed wait times. No additional polling needed — just ensure new badge components re-render on poll data refresh.

## Current Project State
- [Placeholder — to be updated based on completion of dependent tasks US_052 and US_008]

## Expected Changes
| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/hooks/useWaitThreshold.ts | React Query hook to fetch configurable wait time threshold from backend API |
| CREATE | app/src/components/queue/WaitTimeAlertBadge.tsx | Amber/red badge component for wait time threshold violation display |
| CREATE | app/src/components/queue/NoShowAutoBadge.tsx | Auto-detected no-show badge with delayed-detection flag support |
| MODIFY | app/src/components/queue/QueueTable.tsx | Integrate WaitTimeAlertBadge and NoShowAutoBadge into queue row rendering |
| MODIFY | app/src/pages/staff/QueueDashboard.tsx | Add threshold-exceeded alert banner above queue table |
| MODIFY | app/src/store/queueStore.ts | Add waitThresholdMinutes state field and update logic |

> Only list concrete, verifiable file operations. No speculative directory trees.

## External References
- [MUI 5 Chip component](https://mui.com/material-ui/react-chip/) — Used for status badges
- [MUI 5 Alert component](https://mui.com/material-ui/react-alert/) — Used for threshold warning banner
- [React Query v4 useQuery](https://tanstack.com/query/v4/docs/framework/react/reference/useQuery) — Polling and cache management for threshold config
- [WAI-ARIA Live Regions](https://www.w3.org/WAI/WCAG21/Techniques/aria/ARIA19) — Screen reader announcements for dynamic queue updates (UXR-206)

## Build Commands
- [Refer to applicable technology stack specific build commands](.propel/build/)

## Implementation Validation Strategy
- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] WaitTimeAlertBadge renders amber badge at threshold, red badge at 1.5x threshold
- [ ] NoShowAutoBadge renders correctly with auto-detected and delayed-detection variants
- [ ] Alert banner shows correct count of patients exceeding threshold
- [ ] ARIA labels present on all dynamic alert elements (UXR-206)
- [ ] Appointment status colors match designsystem.md: No-show=Red (#D32F2F), Waiting=Amber (#ED6C02)
- [ ] Auto no-show row has reduced opacity (0.6) per wireframe pattern
- [ ] Threshold config changes reflect within 30 seconds on all active queue views

## Implementation Checklist
- [ ] Create `useWaitThreshold` hook with React Query polling (30s refetch interval)
- [ ] Create `WaitTimeAlertBadge` component with amber/red threshold logic
- [ ] Create `NoShowAutoBadge` component with auto-detected and delayed-detection variants
- [ ] Integrate `WaitTimeAlertBadge` into QueueTable Wait Time column
- [ ] Integrate `NoShowAutoBadge` into QueueTable Status column with row styling
- [ ] Add threshold-exceeded alert banner to QueueDashboard with patient count
- [ ] Update Zustand queue store with `waitThresholdMinutes` state
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete

**Traceability:** US_055 AC-2, AC-3 | FR-079 | UXR-103, UXR-206, UXR-401 | SCR-011
