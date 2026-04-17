# Task - TASK_003

## Requirement Reference

- User Story: US_056
- Story Location: .propel/context/tasks/EP-009/us_056/us_056.md
- Acceptance Criteria:
    - AC-2: Given the queue dashboard is displayed, When the staff member applies multiple filters (provider + appointment type + status), Then filters are combined with AND logic and results update instantly.
    - AC-3: Given the staff member navigates to queue history, When they select a date range, Then the system displays historical queue data including average wait times, no-show counts, and patient throughput.
    - AC-4: Given queue history data is available, When the staff member requests an export, Then the system generates a CSV report with queue metrics for the selected period.
- Edge Cases:
    - When queue history is requested for dates before system deployment, no rows exist in the history tables for those dates; the application layer handles the empty response.

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
| Frontend | N/A | - |
| Backend | N/A | - |
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
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

Add database indexes and a materialized view / summary table to support efficient queue filtering and history aggregation queries on PostgreSQL 16. The QueueEntry table (from US_008) requires composite indexes for the filter combination (provider, appointment type, status) and a daily queue history summary table (`queue_daily_summary`) to pre-aggregate metrics for fast history reporting and CSV export. An EF Core migration script with rollback support is produced.

## Dependent Tasks

- US_008 tasks (Core Domain Entity Models) — Requires QueueEntry and Appointment entity models and initial migration in place.

## Impacted Components

- **New**: EF Core migration `AddQueueFilteringIndexesAndHistorySummary` — Adds composite indexes and queue_daily_summary table (Server/Migrations/)
- **Modify**: `AppDbContext` — Register QueueDailySummary entity and configure table mapping (Server/Data/)
- **New**: `QueueDailySummary` entity model — Date, ProviderId, AppointmentType, AvgWaitTimeMinutes, NoShowCount, CompletedCount, TotalPatients (Server/Models/)

## Implementation Plan

1. **Create `QueueDailySummary` entity model** with columns: `Id` (UUID PK), `SummaryDate` (date, not null), `ProviderId` (UUID FK nullable — null means all-provider aggregate), `AppointmentType` (string nullable), `AvgWaitTimeMinutes` (decimal), `NoShowCount` (int), `CompletedCount` (int), `TotalPatients` (int), `CreatedAt` (timestamp). Add unique constraint on (`SummaryDate`, `ProviderId`, `AppointmentType`).
2. **Register entity in AppDbContext** with `modelBuilder.Entity<QueueDailySummary>()` configuration. Map to `queue_daily_summary` table. Configure unique composite index on (`SummaryDate`, `ProviderId`, `AppointmentType`).
3. **Create EF Core migration** `AddQueueFilteringIndexesAndHistorySummary`:
    - Add composite index on `QueueEntry` table: `IX_QueueEntry_Status_Priority_CreatedAt` on (`status`, `priority`, `created_at`) for filtered queue queries.
    - Add index on `QueueEntry` table: `IX_QueueEntry_AppointmentId` on (`appointment_id`) for join performance (if not already present from US_008).
    - Add index on `Appointment` table: `IX_Appointment_ProviderId_AppointmentTime` on (`provider_id`, `appointment_time`) for provider-filtered queue queries.
    - Create `queue_daily_summary` table with columns and constraints defined above.
    - Add index `IX_QueueDailySummary_DateRange` on (`summary_date`) for date range queries.
4. **Write rollback support** in the `Down()` method: drop `queue_daily_summary` table and drop all added indexes.
5. **Add SQL seed script** for a daily aggregation job (as a comment/reference) showing the INSERT ... SELECT pattern that a background job (Hangfire or Windows Task Scheduler) would run nightly to populate `queue_daily_summary` from `QueueEntry` + `Appointment` join with GROUP BY date, provider, and type.

**Focus on how to implement:**

- Use EF Core `migrationBuilder.CreateIndex()` for composite indexes. Use `migrationBuilder.CreateTable()` for the summary table.
- Use `HasIndex(e => new { e.Status, e.Priority, e.CreatedAt })` in `OnModelCreating` for the QueueEntry composite index.
- Use `HasAlternateKey` or `HasIndex(...).IsUnique()` for the unique composite on QueueDailySummary.
- The daily summary table avoids expensive runtime GROUP BY queries on large QueueEntry tables — queries against `queue_daily_summary` are O(days) not O(queue_entries).
- Keep migration idempotent: check for existing indexes before creating.
- Foreign key from `QueueDailySummary.ProviderId` to `User.Id` (provider is a User with Staff role) — nullable to allow all-provider summary rows.

## Current Project State

- [Placeholder — to be updated based on completion of dependent US_008 tasks]

```text
Server/
├── Data/
│   └── AppDbContext.cs           # Existing from US_008
├── Models/
│   ├── QueueEntry.cs             # From US_008
│   └── Appointment.cs            # From US_008
├── Migrations/
│   └── ...                       # Existing migrations
└── ...
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/QueueDailySummary.cs | Daily summary entity: Date, ProviderId, AppointmentType, metrics columns |
| MODIFY | Server/Data/AppDbContext.cs | Register QueueDailySummary entity, configure composite unique index |
| CREATE | Server/Migrations/XXXXXX_AddQueueFilteringIndexesAndHistorySummary.cs | Migration: composite indexes on QueueEntry/Appointment + queue_daily_summary table |

## External References

- [EF Core 8 Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [EF Core 8 Index Configuration](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=data-annotations)
- [PostgreSQL 16 CREATE INDEX](https://www.postgresql.org/docs/16/sql-createindex.html)
- [PostgreSQL Composite Index Best Practices](https://www.postgresql.org/docs/16/indexes-multicolumn.html)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies successfully: `dotnet ef database update`
- [ ] Migration rolls back successfully: `dotnet ef database update <previous_migration>`
- [ ] Composite index `IX_QueueEntry_Status_Priority_CreatedAt` exists on QueueEntry table
- [ ] Index `IX_Appointment_ProviderId_AppointmentTime` exists on Appointment table
- [ ] `queue_daily_summary` table created with correct columns and constraints
- [ ] Unique constraint enforced on (summary_date, provider_id, appointment_type)
- [ ] EXPLAIN ANALYZE on filtered queue query shows index scan (not seq scan) for >100 rows

## Implementation Checklist

- [ ] Create QueueDailySummary entity model with summary columns and unique composite constraint
- [ ] Register QueueDailySummary in AppDbContext with table mapping and index configuration
- [ ] Create EF Core migration with composite indexes on QueueEntry (status, priority, created_at)
- [ ] Add provider + appointment_time index on Appointment table in migration
- [ ] Create queue_daily_summary table DDL in migration with rollback in Down() method
- [ ] Verify migration applies and rolls back cleanly on PostgreSQL 16
