# Task - TASK_001

## Requirement Reference

- User Story: US_054
- Story Location: .propel/context/tasks/EP-009/us_054/us_054.md
- Acceptance Criteria:
  - AC-1: Given a walk-in patient is marked as urgent, When they are added to the queue, Then the system automatically positions them above non-urgent patients in the queue.
  - AC-2: Given the queue is displayed, When a staff member needs to adjust order, Then they can manually drag-and-drop or use up/down controls to reorder queue entries.
  - AC-4: Given multiple urgent patients are in the queue, When they are displayed, Then urgent patients are sorted by arrival time within the urgent priority tier.
- Edge Case:
  - When a staff member tries to move a non-urgent patient above an urgent patient, the system allows it but displays a confirmation dialog warning about the priority override.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-011-arrival-queue.html` |
| **Screen Spec** | figma_spec.md#SCR-011 |
| **UXR Requirements** | UXR-103, UXR-102, UXR-003, UXR-005, UXR-206, UXR-401, UXR-502, UXR-601, UXR-604 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing, designsystem.md#badge, designsystem.md#table, designsystem.md#timer |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement (UI Tasks Only)**

**IF Wireframe Status = AVAILABLE:**

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
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Database | PostgreSQL | 16.x |
| Library | @dnd-kit/core (drag-and-drop) | 6.x |

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

Implement the priority queue display and manual queue adjustment UI on the Arrival Queue Dashboard (SCR-011). This task adds visual priority tier separation, drag-and-drop reorder controls with up/down arrow fallback, priority override confirmation dialog, and real-time queue re-rendering when priority or order changes. The queue table must sort urgent patients above normal patients, with urgent patients sorted by arrival time within their tier.

## Dependent Tasks

- US_053 tasks (queue dashboard UI foundation — table, filters, status badges, timer display)
- US_008 tasks (QueueEntry entity definition)
- task_003_db_priority_queue_schema (queue_position column for manual ordering)
- task_002_be_priority_queue_api (PUT /queue/{id}/priority and PUT /queue/reorder endpoints)

## Impacted Components

- `app/src/features/queue/components/ArrivalQueueTable.tsx` — ADD drag-and-drop row capability, priority column styling, up/down reorder controls
- `app/src/features/queue/components/PriorityBadge.tsx` — CREATE priority badge component (urgent=error.main, normal=neutral.200)
- `app/src/features/queue/components/QueueReorderControls.tsx` — CREATE up/down arrow buttons for manual queue reorder
- `app/src/features/queue/components/PriorityOverrideDialog.tsx` — CREATE confirmation dialog for moving non-urgent above urgent
- `app/src/features/queue/hooks/useQueueReorder.ts` — CREATE hook for reorder API calls with optimistic updates and conflict handling
- `app/src/features/queue/hooks/useQueuePriority.ts` — CREATE hook for priority change API calls
- `app/src/features/queue/store/queueStore.ts` — MODIFY to add queue sort logic (urgent tier first, then arrival time)

## Implementation Plan

1. **Priority badge component**: Create `PriorityBadge` using MUI Chip with error.main background for urgent and neutral.200 for normal. Use overline typography per designsystem.md.
2. **Queue sort logic**: Update Zustand queue store to implement two-tier sorting — urgent patients first (sorted by arrival_timestamp ascending), then normal patients (sorted by arrival_timestamp ascending). When `queue_position` is present, use it as the primary sort within each tier.
3. **Drag-and-drop reorder**: Integrate @dnd-kit/core for row-level drag-and-drop on the queue table. Each `<tr>` becomes a draggable/droppable item keyed by queue_id. On drop, calculate new position and call PUT /queue/reorder.
4. **Up/down arrow controls**: Add `QueueReorderControls` with MUI IconButtons (ArrowUpward, ArrowDownward) in the Actions column. Disable up arrow for first item and down arrow for last item. On click, swap positions with adjacent entry.
5. **Priority override confirmation**: When a drag-and-drop or arrow move would place a non-urgent patient above an urgent patient, intercept the action and display `PriorityOverrideDialog` (MUI Dialog) with warning text per UXR-102 destructive action confirmation pattern. Proceed only on user confirmation.
6. **Optimistic UI updates with conflict handling**: Use React Query mutations for reorder/priority APIs. Apply optimistic updates to the local queue state. On 409 Conflict response (optimistic locking failure), invalidate cache, refetch queue data, and display a Snackbar notification: "Queue was updated by another staff member. Refreshing..."
7. **Accessibility**: Add `aria-label` for drag handles ("Drag to reorder [patient name]"), `aria-live="polite"` region for queue position announcements per UXR-206, and keyboard support for reorder (Enter/Space to grab, Arrow keys to move, Escape to cancel).
8. **Screen states**: Implement all 5 SCR-011 states — Default (sorted queue with priority tiers), Loading (skeleton placeholders per UXR-502 for >300ms), Empty ("No patients in queue" illustration + CTA), Error (actionable error message with retry per UXR-601), Validation (confirmation dialogs for priority overrides).

## Current Project State

```
app/
├── src/
│   ├── features/
│   │   └── queue/
│   │       ├── components/
│   │       │   └── ArrivalQueueTable.tsx  [from US_053]
│   │       ├── hooks/
│   │       └── store/
│   │           └── queueStore.ts          [from US_053]
│   └── ...
```

> Placeholder: updated during execution based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/features/queue/components/PriorityBadge.tsx | Priority badge using MUI Chip — urgent (error.main) and normal (neutral.200) |
| CREATE | app/src/features/queue/components/QueueReorderControls.tsx | Up/down arrow IconButtons for manual queue position adjustment |
| CREATE | app/src/features/queue/components/PriorityOverrideDialog.tsx | MUI Dialog confirming non-urgent above urgent priority override |
| CREATE | app/src/features/queue/hooks/useQueueReorder.ts | React Query mutation hook for PUT /queue/reorder with optimistic updates |
| CREATE | app/src/features/queue/hooks/useQueuePriority.ts | React Query mutation hook for PUT /queue/{id}/priority |
| MODIFY | app/src/features/queue/components/ArrivalQueueTable.tsx | Add drag-and-drop via @dnd-kit, priority column, reorder controls |
| MODIFY | app/src/features/queue/store/queueStore.ts | Add two-tier sort logic (urgent first by arrival, then normal by arrival) |

## External References

- [@dnd-kit documentation — React drag-and-drop](https://docs.dndkit.com/)
- [MUI 5.x Dialog component](https://mui.com/material-ui/react-dialog/)
- [MUI 5.x Chip component](https://mui.com/material-ui/react-chip/)
- [MUI 5.x IconButton component](https://mui.com/material-ui/api/icon-button/)
- [React Query v4 — Optimistic Updates](https://tanstack.com/query/v4/docs/framework/react/guides/optimistic-updates)
- [WAI-ARIA drag-and-drop pattern](https://www.w3.org/WAI/ARIA/apg/patterns/drag-and-drop/)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Priority badge renders correctly for both urgent (red) and normal (gray) states
- [ ] Drag-and-drop reorder updates queue positions via API and re-renders sorted list
- [ ] Up/down arrow controls swap adjacent entries and disable at boundaries
- [ ] Priority override confirmation dialog appears when moving non-urgent above urgent
- [ ] Optimistic locking conflict (409) triggers refetch and user notification
- [ ] Screen reader announces queue position changes via aria-live region
- [ ] Keyboard navigation works for drag-and-drop (Enter/Space grab, Arrows move, Escape cancel)
- [ ] All 5 screen states render correctly (Default, Loading, Empty, Error, Validation)

## Implementation Checklist

- [ ] Create `PriorityBadge` component with urgent/normal variants using MUI Chip and design tokens
- [ ] Update `queueStore.ts` sort logic — urgent tier first sorted by arrival_timestamp, then normal tier sorted by arrival_timestamp, with queue_position override when present
- [ ] Integrate @dnd-kit/core in `ArrivalQueueTable` for row-level drag-and-drop reorder
- [ ] Create `QueueReorderControls` with ArrowUpward/ArrowDownward IconButtons and boundary disable logic
- [ ] Create `PriorityOverrideDialog` MUI Dialog with warning message for non-urgent-above-urgent moves
- [ ] Create `useQueueReorder` hook with React Query mutation, optimistic update, and 409 conflict handling (refetch + Snackbar)
- [ ] Create `useQueuePriority` hook with React Query mutation for PUT /queue/{id}/priority
- [ ] Add ARIA attributes — drag handle labels, aria-live polite region, aria-sort on sortable columns, keyboard reorder support
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
