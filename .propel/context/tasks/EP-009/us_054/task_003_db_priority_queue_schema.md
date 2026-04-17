# Task - TASK_003

## Requirement Reference

- User Story: US_054
- Story Location: .propel/context/tasks/EP-009/us_054/us_054.md
- Acceptance Criteria:
  - AC-1: Given a walk-in patient is marked as urgent, When they are added to the queue, Then the system automatically positions them above non-urgent patients in the queue. (Requires queue_position column for positional ordering.)
  - AC-3: Given a manual queue adjustment is made, When the change is saved, Then the audit log records the reorder action with staff attribution, original position, and new position. (Requires QueueAuditLog table.)
- Edge Case:
  - System uses optimistic locking; if a conflict occurs, the latest change is displayed with a notification. (Requires row_version concurrency token column.)

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
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |

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

Create the database schema migration to support priority queue management and manual queue reorder for US_054. This adds a `queue_position` column to the existing QueueEntry table for explicit ordering, a `row_version` concurrency token for optimistic locking during concurrent modifications, and a new `QueueAuditLog` table to record all priority changes and reorder actions with full staff attribution. Also adds an index on (priority, queue_position) for performant sorted queries.

## Dependent Tasks

- US_008 tasks (QueueEntry table creation with base columns: queue_id, appointment_id, arrival_timestamp, wait_time_minutes, priority, status, created_at, updated_at)

## Impacted Components

- `Server/src/Data/Migrations/YYYYMMDDHHMMSS_AddQueuePositionAndAuditLog.cs` — CREATE EF Core migration
- `Server/src/Data/Entities/QueueEntry.cs` — MODIFY to add queue_position and row_version properties
- `Server/src/Data/Entities/QueueAuditLog.cs` — CREATE new entity
- `Server/src/Data/AppDbContext.cs` — MODIFY to configure QueueAuditLog DbSet and concurrency token

## Implementation Plan

1. **Add queue_position column to QueueEntry**: Add `queue_position` (int, NOT NULL, DEFAULT 0) to the QueueEntry table. This column determines display order within the queue. Urgent patients get lower position numbers (appear first). Default 0 is recalculated by the service layer on insert.
2. **Add row_version concurrency token**: Add `row_version` (integer, NOT NULL, DEFAULT 1) to QueueEntry for optimistic locking. EF Core maps this with `[ConcurrencyCheck]`. On each update, the service increments row_version; if the DB value differs from the expected value, `DbUpdateConcurrencyException` is thrown.
3. **Create QueueAuditLog table**: New table with columns: `audit_id` (UUID PK, DEFAULT gen_random_uuid()), `action_type` (VARCHAR(20) NOT NULL — 'PRIORITY_CHANGE' or 'REORDER'), `queue_id` (UUID FK to QueueEntry, NOT NULL), `staff_user_id` (UUID FK to Users, NOT NULL), `original_position` (INT), `new_position` (INT), `original_priority` (VARCHAR(10)), `new_priority` (VARCHAR(10)), `created_at` (TIMESTAMPTZ, DEFAULT NOW()).
4. **Create composite index**: Add index `IX_QueueEntry_Priority_Position` on (priority DESC, queue_position ASC) for performant sorted queue retrieval. This supports the two-tier sort: urgent patients first, then by position.
5. **Create index on QueueAuditLog**: Add index `IX_QueueAuditLog_QueueId_CreatedAt` on (queue_id, created_at DESC) for efficient audit history queries per queue entry.
6. **Generate EF Core migration**: Run `dotnet ef migrations add AddQueuePositionAndAuditLog` to generate the migration file. Verify the Up() and Down() methods are correct with rollback support.

## Current Project State

```
Server/
├── src/
│   └── Data/
│       ├── Entities/
│       │   └── QueueEntry.cs             [from US_008 — base entity]
│       ├── Migrations/
│       │   └── [existing migrations]
│       └── AppDbContext.cs
```

> Placeholder: updated during execution based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/src/Data/Entities/QueueEntry.cs | Add queue_position (int) and row_version (int) properties with [ConcurrencyCheck] |
| CREATE | Server/src/Data/Entities/QueueAuditLog.cs | New entity with audit_id, action_type, queue_id, staff_user_id, original/new position/priority, created_at |
| MODIFY | Server/src/Data/AppDbContext.cs | Add DbSet for QueueAuditLog, configure composite index on QueueEntry, configure concurrency token |
| CREATE | Server/src/Data/Migrations/YYYYMMDDHHMMSS_AddQueuePositionAndAuditLog.cs | EF Core migration with Up/Down methods for schema changes |

## External References

- [EF Core 8 — Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [EF Core 8 — Concurrency Tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency?tabs=data-annotations)
- [Npgsql EF Core — PostgreSQL Concurrency](https://www.npgsql.org/efcore/modeling/concurrency.html)
- [PostgreSQL 16 — CREATE INDEX](https://www.postgresql.org/docs/16/sql-createindex.html)
- [DR-008: System MUST store queue entries with arrival timestamp, priority level, and current status](design.md)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration Up() executes successfully against PostgreSQL 16.x
- [ ] Migration Down() rolls back cleanly (removes columns and table)
- [ ] QueueEntry table has queue_position (int, NOT NULL, DEFAULT 0) column
- [ ] QueueEntry table has row_version (int, NOT NULL, DEFAULT 1) column
- [ ] QueueAuditLog table created with all required columns and FK constraints
- [ ] Composite index IX_QueueEntry_Priority_Position exists on (priority DESC, queue_position ASC)
- [ ] Index IX_QueueAuditLog_QueueId_CreatedAt exists on (queue_id, created_at DESC)
- [ ] EF Core model snapshot updated correctly
- [ ] Optimistic locking triggers DbUpdateConcurrencyException when row_version mismatches

## Implementation Checklist

- [ ] Add `queue_position` (int, NOT NULL, DEFAULT 0) property to QueueEntry entity with column configuration in AppDbContext
- [ ] Add `row_version` (int, NOT NULL, DEFAULT 1) property to QueueEntry entity with [ConcurrencyCheck] attribute
- [ ] Create `QueueAuditLog` entity class with audit_id (UUID PK), action_type (string), queue_id (UUID FK), staff_user_id (UUID FK), original_position, new_position, original_priority, new_priority, created_at
- [ ] Register DbSet for QueueAuditLog in AppDbContext and configure FK relationships
- [ ] Configure composite index IX_QueueEntry_Priority_Position in OnModelCreating via HasIndex
- [ ] Generate and validate EF Core migration with `dotnet ef migrations add AddQueuePositionAndAuditLog` — verify Up() and Down() methods have correct rollback
