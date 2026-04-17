# Task - task_003_db_arrival_status_schema

## Task ID

- ID: task_003_db_arrival_status_schema

## Task Title

- Add Schema Migration for Arrival Status Support

## Parent User Story

- **User Story**: US_052 — Patient Arrival Status Marking
- **Epic**: EP-009

## Description

Create EF Core database migration to extend the QueueEntry and Appointment schemas to support the full arrival status lifecycle: arrived, no-show, cancelled, and arrived-late states. Adds new columns for cancellation tracking and override audit, performance indexes for daily queue queries, data integrity constraints, and a database view for efficient dashboard data retrieval. Includes rollback migration for safe reversal.

## Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Migration Tool | EF Core Migrations (`dotnet ef`) | 8.x |

## Acceptance Criteria Mapping

| AC | Description | Coverage |
|----|-------------|----------|
| AC-1 | Arrival timestamp recorded when staff marks arrived | QueueEntry.arrival_timestamp column (existing) + NOT NULL constraint when status is waiting/arrived_late |
| AC-2 | Auto no-show after 15 min | QueueEntry.status enum extended with 'no_show' value; Appointment.status already supports 'no-show' |
| AC-3 | Cancel updates immediately and slot released | QueueEntry.cancelled_at timestamp column + status enum 'cancelled' value |

## Edge Cases

| Edge Case | Implementation |
|-----------|----------------|
| Override no-show to arrived-late | QueueEntry.status extended with 'arrived_late' value; override_reason column stores audit reason |
| Duplicate arrival prevention | Unique partial index on (appointment_id) WHERE status IN ('waiting', 'arrived_late') prevents concurrent duplicates at DB level |
| Migration failure rollback | DOWN migration method reverses all changes: drops view, drops indexes, removes columns, reverts enum |
| Existing data compatibility | Migration handles existing QueueEntry rows: no data migration needed since new columns are nullable and new enum values don't affect existing rows |

## Implementation Checklist

- [ ] Add EF Core migration to extend `QueueEntry.status` enum type: add `no_show`, `arrived_late`, and `cancelled` values to the PostgreSQL enum via `ALTER TYPE queue_entry_status ADD VALUE` statements; update EF Core entity configuration to map new enum values
- [ ] Add `cancelled_at` (`timestamp with time zone`, nullable) and `override_reason` (`text`, nullable, max 500 chars) columns to `QueueEntry` table via `ALTER TABLE` in migration; update EF Core `QueueEntry` entity with corresponding properties and Fluent API configuration
- [ ] Create composite index `IX_QueueEntry_Status_CreatedAt` on `(status, created_at)` for efficient daily queue filtering; create partial unique index `IX_QueueEntry_AppointmentId_Active` on `(appointment_id)` WHERE `status IN ('waiting', 'arrived_late')` to enforce single active arrival per appointment at database level
- [ ] Add `CHECK` constraint `CK_QueueEntry_ArrivalTimestamp_Required` ensuring `arrival_timestamp IS NOT NULL` when `status IN ('waiting', 'arrived_late')` to enforce data integrity; add `CHECK` constraint `CK_QueueEntry_CancelledAt_Required` ensuring `cancelled_at IS NOT NULL` when `status = 'cancelled'`
- [ ] Create database view `vw_daily_queue` joining `QueueEntry q` with `Appointment a` and `Patient p` on `q.appointment_id = a.appointment_id` and `a.patient_id = p.patient_id`; select columns: `q.queue_id`, `q.appointment_id`, `p.first_name || ' ' || p.last_name AS patient_name`, `a.appointment_time`, `q.arrival_timestamp`, `q.priority`, `q.status`, `a.status AS appointment_status`, `q.override_reason`; filter by `a.appointment_time::date = CURRENT_DATE`
- [ ] Write rollback (DOWN) migration method: `DROP VIEW IF EXISTS vw_daily_queue`; drop indexes; drop CHECK constraints; drop new columns (`cancelled_at`, `override_reason`); document that PostgreSQL enum values cannot be removed — note this limitation in migration comments

## Effort Estimate

- **Estimated Hours**: 3
- **Complexity**: Medium

## Dependencies

| Dependency | Type | Description |
|------------|------|-------------|
| US_008 | External | QueueEntry and Appointment tables must exist with base schema (queue_id, appointment_id, arrival_timestamp, wait_time_minutes, priority, status, created_at, updated_at) |

## Schema Changes Summary

### QueueEntry Table — Modifications

| Change | Column/Object | Type | Details |
|--------|--------------|------|---------|
| ENUM EXTEND | status | enum | Add values: `no_show`, `arrived_late`, `cancelled` |
| ADD COLUMN | cancelled_at | timestamp with time zone | Nullable; set when status transitions to cancelled |
| ADD COLUMN | override_reason | text | Nullable; max 500 chars; set when no-show overridden to arrived-late |
| ADD INDEX | IX_QueueEntry_Status_CreatedAt | btree | On (status, created_at) for daily queue queries |
| ADD INDEX | IX_QueueEntry_AppointmentId_Active | unique partial | On (appointment_id) WHERE status IN ('waiting', 'arrived_late') |
| ADD CHECK | CK_QueueEntry_ArrivalTimestamp_Required | constraint | arrival_timestamp NOT NULL when status IN ('waiting', 'arrived_late') |
| ADD CHECK | CK_QueueEntry_CancelledAt_Required | constraint | cancelled_at NOT NULL when status = 'cancelled' |

### New Database View

| View | Purpose |
|------|---------|
| vw_daily_queue | Pre-joined QueueEntry + Appointment + Patient for today's queue; used by GET /queue/today endpoint to reduce query complexity |

## Migration Pseudocode

```sql
-- UP Migration
-- 1. Extend enum
ALTER TYPE queue_entry_status ADD VALUE IF NOT EXISTS 'no_show';
ALTER TYPE queue_entry_status ADD VALUE IF NOT EXISTS 'arrived_late';
ALTER TYPE queue_entry_status ADD VALUE IF NOT EXISTS 'cancelled';

-- 2. Add columns
ALTER TABLE queue_entries
    ADD COLUMN cancelled_at TIMESTAMPTZ NULL,
    ADD COLUMN override_reason TEXT NULL;

-- 3. Add constraints
ALTER TABLE queue_entries
    ADD CONSTRAINT CK_QueueEntry_ArrivalTimestamp_Required
        CHECK (status NOT IN ('waiting', 'arrived_late') OR arrival_timestamp IS NOT NULL),
    ADD CONSTRAINT CK_QueueEntry_CancelledAt_Required
        CHECK (status != 'cancelled' OR cancelled_at IS NOT NULL),
    ADD CONSTRAINT CK_QueueEntry_OverrideReason_MaxLen
        CHECK (override_reason IS NULL OR LENGTH(override_reason) <= 500);

-- 4. Add indexes
CREATE INDEX IX_QueueEntry_Status_CreatedAt
    ON queue_entries (status, created_at);

CREATE UNIQUE INDEX IX_QueueEntry_AppointmentId_Active
    ON queue_entries (appointment_id)
    WHERE status IN ('waiting', 'arrived_late');

-- 5. Create view
CREATE OR REPLACE VIEW vw_daily_queue AS
SELECT
    q.queue_id,
    q.appointment_id,
    p.first_name || ' ' || p.last_name AS patient_name,
    a.appointment_time,
    q.arrival_timestamp,
    q.priority,
    q.status,
    a.status AS appointment_status,
    q.override_reason
FROM queue_entries q
JOIN appointments a ON q.appointment_id = a.appointment_id
JOIN patients p ON a.patient_id = p.patient_id
WHERE a.appointment_time::date = CURRENT_DATE;
```

```sql
-- DOWN Migration (Rollback)
DROP VIEW IF EXISTS vw_daily_queue;
DROP INDEX IF EXISTS IX_QueueEntry_AppointmentId_Active;
DROP INDEX IF EXISTS IX_QueueEntry_Status_CreatedAt;
ALTER TABLE queue_entries DROP CONSTRAINT IF EXISTS CK_QueueEntry_OverrideReason_MaxLen;
ALTER TABLE queue_entries DROP CONSTRAINT IF EXISTS CK_QueueEntry_CancelledAt_Required;
ALTER TABLE queue_entries DROP CONSTRAINT IF EXISTS CK_QueueEntry_ArrivalTimestamp_Required;
ALTER TABLE queue_entries DROP COLUMN IF EXISTS override_reason;
ALTER TABLE queue_entries DROP COLUMN IF EXISTS cancelled_at;
-- NOTE: PostgreSQL does not support removing enum values.
-- The added enum values (no_show, arrived_late, cancelled) will remain in the type.
-- This is a known PostgreSQL limitation. To fully revert, recreate the enum type.
```

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

## Traceability

| Reference | IDs |
|-----------|-----|
| Acceptance Criteria | AC-1, AC-2, AC-3 |
| Functional Requirements | FR-071, FR-072 |
| Data Requirements | DR-008 (queue entries with arrival timestamp, priority, status) |
| Technical Requirements | TR-015 (optimistic locking — version field already exists) |
| Non-Functional Requirements | NFR-004 (sub-second queries via indexed views) |
