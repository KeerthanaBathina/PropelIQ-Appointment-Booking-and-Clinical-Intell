# Task - task_001_fe_arrival_queue_dashboard

## Task ID

- ID: task_001_fe_arrival_queue_dashboard

## Task Title

- Implement Arrival Queue Dashboard UI (SCR-011)

## Parent User Story

- **User Story**: US_052 — Patient Arrival Status Marking
- **Epic**: EP-009

## Description

Implement the Arrival Queue Dashboard (SCR-011) as a React page component using MUI 5.x. The dashboard displays a real-time sorted queue table with patient arrival status actions (mark arrived, cancel), live wait time timers, color-coded status badges, auto-refresh polling, no-show override workflow, and duplicate arrival prevention. Supports all five required screen states: Default, Loading, Empty, Error, and Validation.

## Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Framework | React | 18.x |
| UI Library | Material-UI (MUI) | 5.x |
| Server State | React Query | 4.x |
| Client State | Zustand | 4.x |

## Acceptance Criteria Mapping

| AC | Description | Coverage |
|----|-------------|----------|
| AC-1 | Staff marks patient as "arrived" → system records arrival timestamp and begins wait time calculation | Mark Arrived button + API call + timer start |
| AC-3 | Staff marks patient as "cancelled" → status updates immediately and slot released | Cancel button + immediate UI update via cache invalidation |
| AC-4 | Wait time displayed in real-time from arrival timestamp to current time | Timer component with per-second re-render |

## Edge Cases

| Edge Case | Implementation |
|-----------|----------------|
| No-show override to arrived-late | Override action button on no-show rows opens dialog with reason input; calls override API endpoint |
| Duplicate arrival marking | Frontend disables "Mark Arrived" button for patients already in waiting status; displays Snackbar error from API 409 response |
| High queue volume (50+ patients) | MUI Table with virtualized scrolling (per figma_spec edge case) |
| Multiple active queue alerts | Stacked notification badges with expandable list |

## Implementation Checklist

- [ ] Create `ArrivalQueueDashboard` page component with MUI `Table` displaying sorted queue (appointment time + priority) with columns: Patient Name, Appointment Time, Arrival Time, Wait Time, Priority, Status, Actions
- [ ] Implement status action buttons (Mark Arrived, Mark Cancelled) with MUI `Button` components, confirmation `Dialog`, and loading states (`aria-busy` when processing)
- [ ] Build `WaitTimeTimer` component calculating real-time elapsed time from `arrival_timestamp` to `Date.now()` with color-coded thresholds: `success.main` (<15 min), `warning.main` (15–30 min), `error.main` (>30 min) using `h4` typography
- [ ] Add status `Badge` components using appointment-status color tokens: waiting=`#2E7D32`, no-show=`#D32F2F`, cancelled=`#757575`, in-visit=`#7B1FA2`, arrived-late=`#ED6C02` with `overline` typography and pill shape
- [ ] Implement auto-refresh polling at 5-second intervals using React Query `useQuery` with `refetchInterval: 5000` for `GET /queue/today` endpoint (per UXR-103 requirement)
- [ ] Add no-show override flow: "Override to Arrived-Late" action button on no-show rows, opens `Dialog` with required reason `TextField`, calls `PUT /queue/{queueId}/override` and invalidates query cache
- [ ] Implement duplicate arrival prevention: disable "Mark Arrived" button when `QueueEntry.status === 'waiting'`; display MUI `Snackbar` with error message from API 409 response ("Patient already marked as arrived at [timestamp]")
- [ ] Add queue filtering via MUI `Select` (filter by status) and implement all five SCR-011 states: Default (queue table), Loading (skeleton), Empty (illustration + "No patients in queue" message), Error (retry action), Validation (inline form errors)

## Effort Estimate

- **Estimated Hours**: 7
- **Complexity**: Medium-High

## Dependencies

| Dependency | Type | Description |
|------------|------|-------------|
| task_002_be_arrival_status_api | Internal | Backend API endpoints required for data fetching and status mutations |
| task_003_db_arrival_status_schema | Internal | Database schema must be migrated before backend can serve data |
| US_008 | External | QueueEntry and Appointment entities must exist |
| US_004 | External | Redis caching infrastructure for real-time updates |

## API Contracts

### GET /queue/today

**Response** (200):

```json
{
  "data": [
    {
      "queueId": "uuid",
      "appointmentId": "uuid",
      "patientName": "string",
      "appointmentTime": "ISO-8601",
      "arrivalTimestamp": "ISO-8601 | null",
      "waitTimeMinutes": 0,
      "priority": "normal | urgent",
      "status": "waiting | in_visit | completed | no_show | arrived_late",
      "appointmentStatus": "scheduled | completed | cancelled | no-show"
    }
  ],
  "totalCount": 0
}
```

### POST /queue/arrive

**Request**: `{ "appointmentId": "uuid" }`
**Response** (201): Updated queue entry with queue position and wait time
**Error** (409): `{ "message": "Patient already marked as arrived at [timestamp]" }`

### PUT /queue/{queueId}/status

**Request**: `{ "status": "cancelled" }`
**Response** (200): Updated queue entry

### PUT /queue/{queueId}/override

**Request**: `{ "newStatus": "arrived_late", "reason": "string" }`
**Response** (200): Updated queue entry with audit confirmation

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-011-arrival-queue.html` |
| **Screen Spec** | figma_spec.md#SCR-011 |
| **UXR Requirements** | UXR-103 (real-time queue refresh ≤5s), UXR-401 (status color coding), UXR-206 (screen reader live regions), UXR-003 (breadcrumb navigation), UXR-005 (single-click patient search) |
| **Design Tokens** | designsystem.md#typography, designsystem.md#colors, designsystem.md#appointment-status |

## Screen State Requirements (SCR-011)

| State | Behavior |
|-------|----------|
| **Default** | Queue table sorted by appointment time + priority; action buttons active |
| **Loading** | MUI Skeleton placeholders for table rows; buttons disabled |
| **Empty** | Centered illustration + "No patients in queue today" message + navigation CTA |
| **Error** | Error Alert with retry button; "Unable to load queue data" message |
| **Validation** | Inline validation on override reason field (required, min 10 chars) |

## Component Specifications

| Component | MUI Component | Design Token |
|-----------|---------------|--------------|
| Queue Table | `Table` / `TableContainer` | Header: neutral.100, Row: 52px min-height, Stripe: neutral.50, Border: neutral.200 |
| Status Badge | `Chip` | Pill shape (radius.full), overline typography, appointment-status colors |
| Action Buttons | `Button` (contained/outlined) | Primary: primary.500, Error: error.main, Medium size (36px) |
| Wait Time Timer | Custom `Typography` (h4) | Color threshold: success.main / warning.main / error.main |
| Status Filter | `Select` | Standard MUI select with outlined variant |
| Alert | `Alert` | Error variant for error state, Info variant for threshold warnings |
| Confirmation Dialog | `Dialog` | radius.lg (12px), elevation-3 |

## Accessibility Requirements

- `aria-live="polite"` region for queue updates (UXR-206)
- `aria-sort` attributes on sortable table columns
- `aria-busy="true"` on buttons during async operations
- `aria-label` on all action buttons with patient context
- Keyboard navigation support (Tab through rows, Enter/Space for actions) per NFR-046
- Focus management after dialog close returns to trigger button

## Traceability

| Reference | IDs |
|-----------|-----|
| Acceptance Criteria | AC-1, AC-3, AC-4 |
| Functional Requirements | FR-071, FR-072 |
| UX Requirements | UXR-103, UXR-401, UXR-206, UXR-003, UXR-005 |
| Data Requirements | DR-008 |
| Non-Functional Requirements | NFR-004 (sub-second cached views), NFR-046 (WCAG 2.1 AA), NFR-047 (mobile responsive) |
