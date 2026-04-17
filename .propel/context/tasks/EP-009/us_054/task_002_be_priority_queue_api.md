# Task - TASK_002

## Requirement Reference

- User Story: US_054
- Story Location: .propel/context/tasks/EP-009/us_054/us_054.md
- Acceptance Criteria:
  - AC-1: Given a walk-in patient is marked as urgent, When they are added to the queue, Then the system automatically positions them above non-urgent patients in the queue.
  - AC-2: Given the queue is displayed, When a staff member needs to adjust order, Then they can manually drag-and-drop or use up/down controls to reorder queue entries.
  - AC-3: Given a manual queue adjustment is made, When the change is saved, Then the audit log records the reorder action with staff attribution, original position, and new position.
  - AC-4: Given multiple urgent patients are in the queue, When they are displayed, Then urgent patients are sorted by arrival time within the urgent priority tier.
- Edge Case:
  - When a staff member tries to move a non-urgent patient above an urgent patient, the system allows it but displays a confirmation dialog warning about the priority override (frontend handles dialog; backend allows the operation).
  - System uses optimistic locking for concurrent queue adjustments; if a conflict occurs, return 409 Conflict with the latest queue state.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |

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

Implement the backend API endpoints and business logic for priority queue management and manual queue reorder. This includes: (1) PUT endpoint to set a queue entry's priority to urgent/normal with automatic repositioning, (2) PUT endpoint to reorder queue entries with optimistic locking to handle concurrent modifications, (3) audit logging for all queue reorder and priority change operations, and (4) queue sorting logic that enforces urgent-first ordering with arrival time sub-sorting within each priority tier.

## Dependent Tasks

- US_008 tasks (QueueEntry entity and base DbContext configuration)
- US_053 tasks (GET /queue/today endpoint, base QueueService)
- task_003_db_priority_queue_schema (queue_position column, row_version column, QueueAuditLog table migration)

## Impacted Components

- `Server/src/Features/Queue/Controllers/QueueController.cs` — MODIFY to add PUT /queue/{id}/priority and PUT /queue/reorder endpoints
- `Server/src/Features/Queue/Services/QueueService.cs` — MODIFY to add SetPriorityAsync, ReorderQueueAsync methods
- `Server/src/Features/Queue/Services/IQueueService.cs` — MODIFY to add interface methods
- `Server/src/Features/Queue/Models/SetPriorityRequest.cs` — CREATE request DTO
- `Server/src/Features/Queue/Models/ReorderQueueRequest.cs` — CREATE request DTO with queue_id and new_position
- `Server/src/Features/Queue/Models/QueueReorderResponse.cs` — CREATE response DTO with updated queue list
- `Server/src/Features/Audit/Services/AuditService.cs` — MODIFY to add LogQueueReorder, LogPriorityChange methods
- `Server/src/Data/Entities/QueueAuditLog.cs` — CREATE entity for queue-specific audit entries

## Implementation Plan

1. **Request/Response DTOs**: Create `SetPriorityRequest` (queue_id, priority enum: normal|urgent), `ReorderQueueRequest` (queue_id, new_position int), and `QueueReorderResponse` (updated queue list with positions). Add DataAnnotation validation on all DTOs.
2. **Priority change endpoint**: Implement `PUT /queue/{queue_id}/priority` in QueueController. Validate queue_id exists. Call QueueService.SetPriorityAsync which updates the priority field and recalculates queue_position — urgent entries get positions starting from 1 (sorted by arrival_timestamp), normal entries follow after (sorted by arrival_timestamp). Log via AuditService.LogPriorityChange.
3. **Queue reorder endpoint**: Implement `PUT /queue/reorder` in QueueController. Accept ReorderQueueRequest with queue_id and target new_position. Call QueueService.ReorderQueueAsync which reads current row_version, validates it matches, shifts other entries' positions, updates the target entry position, and increments row_version. If row_version mismatch, return 409 Conflict with current queue state.
4. **Optimistic locking**: Use the `row_version` column (byte[] mapped to PostgreSQL xmin or a custom concurrency token). EF Core `[ConcurrencyCheck]` attribute on row_version. Catch `DbUpdateConcurrencyException` and return 409 with refreshed queue data.
5. **Queue sorting service logic**: Implement `GetSortedQueueAsync` that returns queue entries sorted by: (a) priority descending (urgent=1, normal=0), (b) queue_position ascending within each tier, (c) arrival_timestamp ascending as tiebreaker. This ensures urgent patients always appear above normal patients.
6. **Audit logging**: On every priority change or reorder operation, insert a `QueueAuditLog` record with: action_type (PRIORITY_CHANGE | REORDER), queue_id, staff_user_id (from JWT claims), original_position, new_position, original_priority, new_priority, timestamp. Use Serilog structured logging for operational visibility.
7. **Authorization**: Require `[Authorize(Roles = "Staff,Admin")]` on both endpoints. Extract staff_user_id from `HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)` for audit attribution.
8. **Cache invalidation**: After any priority change or reorder, invalidate the Redis cache key for today's queue (`queue:today:{date}`) to ensure subsequent GET /queue/today calls reflect the latest state.

## Current Project State

```
Server/
├── src/
│   ├── Features/
│   │   ├── Queue/
│   │   │   ├── Controllers/
│   │   │   │   └── QueueController.cs    [from US_053]
│   │   │   ├── Services/
│   │   │   │   ├── IQueueService.cs      [from US_053]
│   │   │   │   └── QueueService.cs       [from US_053]
│   │   │   └── Models/
│   │   └── Audit/
│   │       └── Services/
│   │           └── AuditService.cs       [from EP-TECH]
│   └── Data/
│       ├── Entities/
│       │   └── QueueEntry.cs             [from US_008]
│       └── AppDbContext.cs
```

> Placeholder: updated during execution based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/src/Features/Queue/Models/SetPriorityRequest.cs | DTO with queue_id (Guid) and priority (enum: Normal, Urgent) with DataAnnotation validation |
| CREATE | Server/src/Features/Queue/Models/ReorderQueueRequest.cs | DTO with queue_id (Guid) and new_position (int, Range 1-999) with DataAnnotation validation |
| CREATE | Server/src/Features/Queue/Models/QueueReorderResponse.cs | Response DTO with List of QueueEntryDto ordered by position |
| CREATE | Server/src/Data/Entities/QueueAuditLog.cs | Entity: audit_id, action_type, queue_id, staff_user_id, original_position, new_position, original_priority, new_priority, timestamp |
| MODIFY | Server/src/Features/Queue/Controllers/QueueController.cs | Add PUT /queue/{queue_id}/priority and PUT /queue/reorder endpoints with [Authorize(Roles="Staff,Admin")] |
| MODIFY | Server/src/Features/Queue/Services/IQueueService.cs | Add SetPriorityAsync and ReorderQueueAsync method signatures |
| MODIFY | Server/src/Features/Queue/Services/QueueService.cs | Implement SetPriorityAsync (priority update + auto-reposition), ReorderQueueAsync (position swap + optimistic locking), GetSortedQueueAsync (two-tier sort) |
| MODIFY | Server/src/Features/Audit/Services/AuditService.cs | Add LogQueueReorder and LogPriorityChange methods writing to QueueAuditLog table |
| MODIFY | Server/src/Data/AppDbContext.cs | Add DbSet for QueueAuditLog, configure concurrency token on QueueEntry.row_version |

## External References

- [ASP.NET Core 8 Web API — Controller-based APIs](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [EF Core 8 — Optimistic Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency?tabs=data-annotations)
- [EF Core 8 — PostgreSQL Concurrency Tokens (Npgsql)](https://www.npgsql.org/efcore/modeling/concurrency.html)
- [ASP.NET Core Authorization — Role-based](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-8.0)
- [Serilog Structured Logging](https://serilog.net/)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] PUT /queue/{id}/priority returns 200 with updated queue when valid priority set
- [ ] PUT /queue/{id}/priority returns 404 when queue_id does not exist
- [ ] PUT /queue/reorder returns 200 with reordered queue on valid request
- [ ] PUT /queue/reorder returns 409 Conflict when row_version mismatch (concurrent modification)
- [ ] Urgent patients always appear above normal patients in GET /queue/today response
- [ ] Multiple urgent patients sorted by arrival_timestamp ascending
- [ ] QueueAuditLog records created for every priority change and reorder operation
- [ ] Audit records contain correct staff_user_id, original_position, new_position
- [ ] Redis cache invalidated after priority/reorder operations
- [ ] Unauthorized users (non-Staff, non-Admin) receive 403 Forbidden

## Implementation Checklist

- [ ] Create `SetPriorityRequest` DTO with Guid queue_id and QueuePriority enum, validated via DataAnnotations
- [ ] Create `ReorderQueueRequest` DTO with Guid queue_id and int new_position (Range 1-999), validated via DataAnnotations
- [ ] Create `QueueReorderResponse` DTO wrapping ordered List of QueueEntryDto
- [ ] Create `QueueAuditLog` entity with action_type, queue_id, staff_user_id, original/new position and priority fields, timestamp
- [ ] Implement `PUT /queue/{queue_id}/priority` endpoint — validate input, call service, return sorted queue; [Authorize(Roles="Staff,Admin")]
- [ ] Implement `PUT /queue/reorder` endpoint — validate input, call service with optimistic lock check, return 200 or 409; [Authorize(Roles="Staff,Admin")]
- [ ] Implement `SetPriorityAsync` in QueueService — update priority, recalculate all queue_position values (urgent first by arrival, normal second by arrival), write QueueAuditLog
- [ ] Implement `ReorderQueueAsync` in QueueService — read row_version, shift positions, update target, catch DbUpdateConcurrencyException for 409, invalidate Redis cache, write QueueAuditLog
