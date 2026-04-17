# Task - TASK_003

## Requirement Reference

- User Story: US_053
- Story Location: .propel/context/tasks/EP-009/us_053/us_053.md
- Acceptance Criteria:
    - AC-1: **Given** the staff member opens the arrival queue, **When** the dashboard loads, **Then** it displays all arrived patients sorted by appointment time and priority with sub-second response time.
    - AC-4: **Given** the queue contains entries, **When** the staff member calculates average wait time, **Then** the system displays the average wait time for all currently waiting patients.
- Edge Case:
    - EC-1: When the dashboard has 100+ queue entries, system paginates with 25 entries per page and maintains sort order across pages.

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
| Caching | Upstash Redis | 7.x |
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

Create the database migration, optimized queries, and Redis caching infrastructure for the Arrival Queue Dashboard. This task adds composite indexes on the `QueueEntries` table to support efficient sorting by appointment time and priority, writes the EF Core LINQ query that joins `QueueEntries` with `Appointments` and `Patients` for today's queue data, and establishes the Redis cache key strategy with 5-minute TTL per NFR-004 (sub-second cached views). The QueueEntry entity is defined in design.md with attributes: queue_id, appointment_id, arrival_timestamp, wait_time_minutes (computed), priority (enum), status (enum), created_at, updated_at.

## Dependent Tasks

- US_004 tasks (Redis caching foundation — provides Redis connection and base caching infrastructure)
- EP-DATA tasks (provides QueueEntry and Appointment entity definitions and initial migration)

## Impacted Components

| Component | Type | Project |
|-----------|------|---------|
| QueueEntry (entity) | Existing | Server (Data/Entities) |
| Appointment (entity) | Existing | Server (Data/Entities) |
| Patient (entity) | Existing | Server (Data/Entities) |
| IQueueRepository / QueueRepository | New | Server (Data/Repositories) |
| EF Core Migration (AddQueueIndexes) | New | Server (Data/Migrations) |
| Redis cache configuration | Modified | Server (appsettings.json) |

## Implementation Plan

1. **Create EF Core migration for queue performance indexes**:
   - Add composite index on `QueueEntries` table: `(status, created_at)` for filtering today's entries by status.
   - Add composite index: `(priority DESC, appointment_time ASC)` on the join of QueueEntries + Appointments for sort order optimization.
   - Add index on `QueueEntries.appointment_id` (foreign key index) for fast join performance.
   - Verify existing index on `Appointments.appointment_date` for date-based filtering.

2. **Create `IQueueRepository` interface and `QueueRepository` implementation**:
   - `GetTodayQueueAsync(int? providerId, string? status, int page, int pageSize)`: EF Core LINQ query that:
     - Joins `QueueEntries` → `Appointments` → `Patients` (and `Providers`)
     - Filters by `Appointment.appointment_date = today`
     - Optionally filters by provider_id and queue entry status
     - Orders by `QueueEntry.priority DESC` (urgent first), then `Appointment.appointment_time ASC`
     - Projects to a flat query result DTO (patient_name, appointment_time, provider_name, appointment_type, arrival_timestamp, priority, status)
     - Applies `.Skip()` / `.Take()` for pagination
     - Returns items + total count via a single query with `CountAsync` + `ToListAsync`

3. **Implement average wait time query**:
   - `GetAverageWaitTimeAsync()`: Query `QueueEntries` where `status = waiting` and `arrival_timestamp IS NOT NULL` for today, compute `AVG(EXTRACT(EPOCH FROM (NOW() - arrival_timestamp)) / 60)` as average wait time in minutes. Use raw SQL or EF Core function mapping for PostgreSQL `EXTRACT`.

4. **Implement Redis cache key strategy**:
   - Cache key pattern: `queue:today:{providerId ?? "all"}:{status ?? "all"}:{page}:{pageSize}`
   - TTL: 5 minutes (300 seconds) per NFR-004
   - Serialization: JSON via `System.Text.Json`
   - Cache invalidation: Expose `InvalidateQueueCacheAsync()` that deletes all keys matching `queue:today:*` pattern (called on arrival status changes from US_052).

5. **Add Redis configuration** in `appsettings.json` for queue-specific cache settings: TTL duration, cache key prefix, and max cache entries.

## Current Project State

- Project structure is placeholder — to be updated based on completion of dependent tasks.

```
Server/
├── Data/
│   ├── Entities/
│   │   ├── QueueEntry.cs
│   │   ├── Appointment.cs
│   │   └── Patient.cs
│   ├── Repositories/
│   └── Migrations/
├── appsettings.json
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Data/Migrations/YYYYMMDD_AddQueuePerformanceIndexes.cs | EF Core migration adding composite indexes for queue sorting and filtering |
| CREATE | Server/Data/Repositories/IQueueRepository.cs | Repository interface for queue data access |
| CREATE | Server/Data/Repositories/QueueRepository.cs | EF Core LINQ queries with joins, sorting, filtering, pagination |
| MODIFY | Server/Data/AppDbContext.cs | Add index configurations in OnModelCreating for QueueEntry |
| MODIFY | Server/appsettings.json | Add Redis queue cache settings (TTL, key prefix) |
| MODIFY | Server/Program.cs | Register IQueueRepository in DI container |

## External References

- [EF Core 8 — Creating Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [EF Core 8 — Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [EF Core 8 — Complex Query Operators](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators)
- [PostgreSQL 16 — CREATE INDEX](https://www.postgresql.org/docs/16/sql-createindex.html)
- [StackExchange.Redis — Keys Pattern Matching](https://stackexchange.github.io/StackExchange.Redis/KeysScan.html)
- [Npgsql EF Core — PostgreSQL-specific Functions](https://www.npgsql.org/efcore/mapping/translations.html)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Migration applies successfully and creates indexes on QueueEntries table
- [ ] Query returns correctly sorted results (urgent first, then appointment time ascending)
- [ ] Pagination returns correct page subset with accurate total_count
- [ ] Filter by provider_id returns only entries for that provider
- [ ] Filter by status returns only entries with that status
- [ ] Average wait time query returns correct mean for waiting patients
- [ ] Redis cache stores response with 5-min TTL
- [ ] Cache invalidation clears all queue:today:* keys
- [ ] Query performance < 200ms for 100+ queue entries (before cache)

## Implementation Checklist

- [ ] Create EF Core migration adding composite indexes: `(status, created_at)` and `(appointment_id)` on QueueEntries; verify appointment_date index on Appointments
- [ ] Configure index definitions in `AppDbContext.OnModelCreating` with `HasIndex` fluent API for QueueEntry sort and filter columns
- [ ] Create `IQueueRepository` interface with `GetTodayQueueAsync` (filtered, sorted, paginated) and `GetAverageWaitTimeAsync` method signatures
- [ ] Implement `QueueRepository.GetTodayQueueAsync` with EF Core LINQ join (QueueEntries → Appointments → Patients), priority DESC + appointment_time ASC sort, filter application, and Skip/Take pagination
- [ ] Implement `QueueRepository.GetAverageWaitTimeAsync` computing average minutes from `arrival_timestamp` to now for waiting entries using PostgreSQL `EXTRACT` via Npgsql
- [ ] Register `IQueueRepository` in DI and add Redis queue cache configuration (TTL=300s, key prefix=`queue:today`) to `appsettings.json`
