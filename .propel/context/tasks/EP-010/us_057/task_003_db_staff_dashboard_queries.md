# Task - TASK_003

## Requirement Reference

- User Story: us_057
- Story Location: .propel/context/tasks/EP-010/us_057/us_057.md
- Acceptance Criteria:
    - AC-1: Given the staff member logs in, When the staff dashboard loads, Then it displays today's appointment schedule, arrival queue count, and pending tasks (unverified codes, flagged conflicts, intake reviews).
    - AC-2: Given the daily schedule is displayed, When appointments are listed, Then each shows patient name, time, type, status badge (color-coded), and no-show risk score.
    - AC-4: Given the dashboard is loaded, When data changes occur (new arrival, status change), Then the dashboard updates within 5 seconds without manual refresh. [Indexes ensure sub-second DB queries supporting 5s cache-miss cycle]
- Edge Cases:
    - No appointments scheduled: Query returns empty result set; stats return zero counts.
    - Multiple staff viewing same patient changes: Database queries are idempotent and consistent; caching layer (task_002) handles staleness.

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

Create optimized PostgreSQL database indexes and an EF Core migration to support the Staff Dashboard aggregation queries. The dashboard requires efficient querying of today's appointments joined with patient data, queue entry counts by status, unverified medical codes, flagged extracted data for review, and recently completed document processing. These indexes ensure sub-second response time for the dashboard API (NFR-004) when cache misses occur. No new tables are created; this task adds composite indexes and a database view for the dashboard summary to the existing schema established by US_008 (EP-DATA).

## Dependent Tasks

- US_008 (EP-DATA) — All domain entities and base schema must exist (Appointment, Patient, QueueEntry, MedicalCode, ExtractedData, ClinicalDocument tables)

## Impacted Components

- **NEW** `Server/Data/Migrations/XXXXXXXX_AddStaffDashboardIndexes.cs` — EF Core migration adding composite indexes for dashboard queries
- **NEW** `Server/Data/Views/vw_staff_dashboard_stats.sql` — SQL view for aggregated dashboard statistics (reference script)
- **MODIFY** `Server/Data/ApplicationDbContext.cs` — Add index configuration in `OnModelCreating` for dashboard query optimization

## Implementation Plan

1. **Analyze query patterns** from task_002 (BE) to identify index requirements:
   - Today's schedule: `SELECT ... FROM appointments a JOIN patients p ON a.patient_id = p.patient_id WHERE DATE(a.appointment_time) = CURRENT_DATE ORDER BY a.appointment_time ASC`
   - Queue count: `SELECT COUNT(*) FROM queue_entries WHERE status IN ('waiting', 'in_visit') AND DATE(arrival_timestamp) = CURRENT_DATE`
   - Unverified codes: `SELECT ... FROM medical_codes mc JOIN patients p ON mc.patient_id = p.patient_id WHERE mc.approved_by_user_id IS NULL AND mc.suggested_by_ai = TRUE`
   - Flagged conflicts: `SELECT ... FROM extracted_data ed JOIN clinical_documents cd ON ed.document_id = cd.document_id JOIN patients p ON cd.patient_id = p.patient_id WHERE ed.flagged_for_review = TRUE AND ed.verified_by_user_id IS NULL`
   - Completed today: `SELECT COUNT(*) FROM appointments WHERE DATE(appointment_time) = CURRENT_DATE AND status = 'completed'`

2. **Add composite indexes** via EF Core Fluent API in `OnModelCreating`:
   - `IX_appointments_time_status` on `appointments(appointment_time, status)` — Covers today's schedule and completed count queries
   - `IX_queue_entries_status_arrival` on `queue_entries(status, arrival_timestamp)` — Covers queue count query
   - `IX_medical_codes_approval_ai` on `medical_codes(approved_by_user_id, suggested_by_ai)` filtered where `approved_by_user_id IS NULL` — Covers unverified codes query
   - `IX_extracted_data_review_verified` on `extracted_data(flagged_for_review, verified_by_user_id)` filtered where `flagged_for_review = TRUE AND verified_by_user_id IS NULL` — Covers flagged conflicts query

3. **Create EF Core migration** using `dotnet ef migrations add AddStaffDashboardIndexes`.

4. **Create reference SQL view** (`vw_staff_dashboard_stats`) for documentation purposes — aggregates today's appointment count, queue count, pending task count, completed count. This is a reference script; the BE service (task_002) executes the individual EF Core queries.

5. **Validate indexes** by running `EXPLAIN ANALYZE` on each query pattern to confirm index scans instead of sequential scans.

**Focus on how to implement:**
- Use EF Core `HasIndex()` with `HasFilter()` for partial/filtered indexes (PostgreSQL supports this via Npgsql provider)
- Composite index column order matters: place equality-filtered columns first, range/sort columns second
- Use `INCLUDE` columns in indexes where possible to enable index-only scans
- Migration must include `Down()` method for rollback support (DR-029)

## Current Project State

```
[Placeholder — to be updated based on dependent task completion]
Server/
├── Data/
│   ├── ApplicationDbContext.cs       (MODIFY)
│   ├── Migrations/
│   │   └── XXXXXXXX_AddStaffDashboardIndexes.cs  (NEW)
│   └── Views/
│       └── vw_staff_dashboard_stats.sql           (NEW - reference)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Data/ApplicationDbContext.cs | Add composite index definitions in OnModelCreating for dashboard queries |
| CREATE | Server/Data/Migrations/XXXXXXXX_AddStaffDashboardIndexes.cs | EF Core migration with Up() creating indexes and Down() dropping them |
| CREATE | Server/Data/Views/vw_staff_dashboard_stats.sql | Reference SQL view script for dashboard aggregated statistics |

## External References

- [EF Core 8 — Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=fluent-api)
- [Npgsql EF Core — Index methods](https://www.npgsql.org/efcore/modeling/indexes.html)
- [PostgreSQL 16 — CREATE INDEX](https://www.postgresql.org/docs/16/sql-createindex.html)
- [PostgreSQL 16 — Partial Indexes](https://www.postgresql.org/docs/16/indexes-partial.html)
- [PostgreSQL — EXPLAIN ANALYZE](https://www.postgresql.org/docs/16/sql-explain.html)

## Build Commands

- `cd Server && dotnet ef migrations add AddStaffDashboardIndexes` — Generate migration
- `cd Server && dotnet ef database update` — Apply migration
- `cd Server && dotnet ef migrations script --idempotent` — Generate idempotent SQL script for review

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies successfully (`dotnet ef database update`)
- [ ] Migration rolls back successfully (`dotnet ef database update <previous_migration>`)
- [ ] `EXPLAIN ANALYZE` on today's schedule query shows Index Scan (not Seq Scan)
- [ ] `EXPLAIN ANALYZE` on queue count query shows Index Scan
- [ ] `EXPLAIN ANALYZE` on unverified codes query shows Index Scan
- [ ] `EXPLAIN ANALYZE` on flagged conflicts query shows Index Scan
- [ ] Dashboard API response time <500ms on cache miss with 10K appointment records
- [ ] No existing queries degraded by new indexes (verify with existing test suite)

## Implementation Checklist

- [ ] Add `IX_appointments_time_status` composite index on `appointments(appointment_time, status)` in `OnModelCreating`
- [ ] Add `IX_queue_entries_status_arrival` composite index on `queue_entries(status, arrival_timestamp)` in `OnModelCreating`
- [ ] Add `IX_medical_codes_approval_ai` filtered index on `medical_codes(approved_by_user_id, suggested_by_ai)` where `approved_by_user_id IS NULL`
- [ ] Add `IX_extracted_data_review_verified` filtered index on `extracted_data(flagged_for_review, verified_by_user_id)` where conditions met
- [ ] Generate EF Core migration with `dotnet ef migrations add AddStaffDashboardIndexes`
- [ ] Verify migration `Down()` method drops all created indexes
- [ ] Create reference SQL view `vw_staff_dashboard_stats` for documentation
- [ ] Run `EXPLAIN ANALYZE` on each dashboard query pattern to verify index usage
