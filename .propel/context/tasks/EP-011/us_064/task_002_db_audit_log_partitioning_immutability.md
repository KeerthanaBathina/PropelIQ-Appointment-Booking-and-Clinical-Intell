# Task - task_002_db_audit_log_partitioning_immutability

## Requirement Reference

- User Story: US_064
- Story Location: .propel/context/tasks/EP-011/us_064/us_064.md
- Acceptance Criteria:
    - AC-1: **Given** any data is accessed, created, modified, or deleted, **When** the operation completes, **Then** an immutable audit log entry is created with user ID, action type, entity type, entity ID, timestamp, and IP address.
    - AC-2: **Given** audit log entries exist, **When** any user attempts to modify or delete a log entry, **Then** the operation is rejected and the attempt itself is logged.
    - AC-4: **Given** audit logs are stored, **When** the retention period is checked, **Then** logs are retained for a minimum of 7 years per HIPAA requirements.
- Edge Cases:
    - What happens when the audit log table grows extremely large (millions of records)? System uses table partitioning by month and indexes on user_id, entity_type, and created_at for query performance.

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
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | N/A | - |
| Library | N/A | - |
| AI/ML | N/A | - |
| Vector Store | N/A | - |
| AI Gateway | N/A | - |
| Mobile | N/A | - |

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

Implement PostgreSQL-level schema enhancements for the `audit_logs` table to ensure database-enforced immutability, high-performance querying at scale, HIPAA-compliant retention, and efficient storage management. This task converts the existing `audit_logs` table (created by US_008) into a range-partitioned table by month on the `timestamp` column, adds composite and single-column indexes optimized for the query patterns in task_001, creates PostgreSQL triggers to enforce immutability at the database level (preventing UPDATE and DELETE operations regardless of application-layer bypass), and configures a retention policy ensuring logs are retained for a minimum of 7 years per DR-016. The partitioning strategy addresses the edge case of millions of records by enabling partition pruning during date-range queries and efficient archival of old partitions. This aligns with Architecture Decision #5 (CQRS for Audit Log Access) by optimizing the physical storage layer for both append-only writes and indexed read queries.

## Dependent Tasks

- US_008 (task_002_be_efcore_configuration_migrations) — Requires initial `audit_logs` table schema, `AuditLogConfiguration`, and base migration
- US_003 — Requires PostgreSQL 16 database with superuser access for trigger and partition creation

## Impacted Components

- **MODIFY** `audit_logs` table — Convert to range-partitioned table on `timestamp` column
- **NEW** `audit_logs_YYYY_MM` partitions — Monthly child partitions (current month + 12 months forward)
- **NEW** `idx_audit_logs_user_id_timestamp` — Composite index for user-filtered date-range queries
- **NEW** `idx_audit_logs_entity_type_timestamp` — Composite index for entity-filtered date-range queries
- **NEW** `idx_audit_logs_action_timestamp` — Composite index for action-filtered date-range queries
- **NEW** `idx_audit_logs_timestamp` — B-tree index on timestamp for date-range partition pruning
- **NEW** `trg_audit_logs_prevent_update` — BEFORE UPDATE trigger raising exception
- **NEW** `trg_audit_logs_prevent_delete` — BEFORE DELETE trigger raising exception
- **NEW** `fn_audit_logs_prevent_modification()` — Trigger function for immutability enforcement
- **NEW** EF Core migration file — Migration script for partitioning, indexes, and triggers
- **NEW** `Server/Data/Scripts/create_audit_partitions.sql` — Partition creation script (raw SQL)
- **NEW** `Server/Data/Scripts/audit_retention_policy.sql` — Retention policy verification script

## Implementation Plan

1. **Convert `audit_logs` to Range-Partitioned Table** — Create an EF Core migration that executes raw SQL to convert the existing `audit_logs` table to a range-partitioned table on the `timestamp` column. PostgreSQL 16 native partitioning is used (`PARTITION BY RANGE (timestamp)`). The migration must: (a) rename the existing `audit_logs` table to `audit_logs_old`, (b) create the new partitioned `audit_logs` table with identical schema, (c) create initial monthly partitions covering the current month through 12 months forward using the naming convention `audit_logs_YYYY_MM` (e.g., `audit_logs_2026_04`, `audit_logs_2026_05`, ..., `audit_logs_2027_04`), (d) migrate existing data from `audit_logs_old` to the new partitioned table using `INSERT INTO audit_logs SELECT * FROM audit_logs_old`, (e) drop `audit_logs_old` after verification. The migration MUST be wrapped in a transaction with rollback support (DR-029). Include a default partition `audit_logs_default` to catch any rows outside defined ranges.

2. **Create Partition Management Script** — Create `Server/Data/Scripts/create_audit_partitions.sql` containing a PL/pgSQL function `create_audit_partitions(months_ahead INTEGER DEFAULT 12)` that dynamically creates monthly partitions for the specified number of months ahead if they do not already exist. The function checks for existing partitions using `pg_class` catalog queries before creation to ensure idempotency. This script is designed to be executed by a scheduled job (Windows Task Scheduler) monthly to pre-create partitions. Include `IF NOT EXISTS` guards to prevent errors on re-execution. Log partition creation to PostgreSQL server log using `RAISE NOTICE`.

3. **Add Composite Indexes for Query Performance** — Create indexes optimized for the query patterns in `AuditLogQueryService` (task_001):
   - `idx_audit_logs_user_id_timestamp` on `(user_id, timestamp DESC)` — supports filtering by user with date ordering
   - `idx_audit_logs_entity_type_timestamp` on `(entity_type, timestamp DESC)` — supports filtering by entity type with date ordering
   - `idx_audit_logs_action_timestamp` on `(action, timestamp DESC)` — supports filtering by action type with date ordering
   - `idx_audit_logs_timestamp` on `(timestamp DESC)` — supports pure date-range queries and partition pruning
   All indexes are created on the parent partitioned table and automatically propagated to child partitions by PostgreSQL 16. Use `CREATE INDEX CONCURRENTLY` where possible to avoid locking during creation (NFR-021 zero-downtime migrations).

4. **Create Immutability Trigger Function** — Create PL/pgSQL function `fn_audit_logs_prevent_modification()` that raises an exception when invoked: `RAISE EXCEPTION 'Audit log entries are immutable. UPDATE and DELETE operations are prohibited per HIPAA compliance (DR-016, NFR-012).'` with `ERRCODE = '55000'` (object_not_in_prerequisite_state). The function returns NULL (irrelevant since exception is always raised). This provides database-level enforcement independent of the application layer — even direct SQL access or bypassed API calls cannot modify audit records (AC-2).

5. **Attach Immutability Triggers** — Create two triggers on the `audit_logs` table:
   - `trg_audit_logs_prevent_update`: `BEFORE UPDATE ON audit_logs FOR EACH ROW EXECUTE FUNCTION fn_audit_logs_prevent_modification()`
   - `trg_audit_logs_prevent_delete`: `BEFORE DELETE ON audit_logs FOR EACH ROW EXECUTE FUNCTION fn_audit_logs_prevent_modification()`
   Triggers are defined on the parent partitioned table and automatically inherited by all child partitions in PostgreSQL 16. Verify trigger inheritance by querying `pg_trigger` for a child partition. The INSERT trigger is intentionally NOT created — append-only writes must succeed.

6. **Configure Retention Policy** — Create `Server/Data/Scripts/audit_retention_policy.sql` containing:
   - A verification query that checks the oldest partition's date range against the 7-year retention requirement (DR-016): `SELECT MIN(timestamp) FROM audit_logs` and asserts it is within the retention window.
   - A `COMMENT ON TABLE audit_logs IS 'HIPAA Audit Trail — 7-year minimum retention per 45 CFR §164.312(b). DO NOT drop partitions less than 7 years old.'` for documentation.
   - A safety function `fn_audit_logs_safe_archive(cutoff_date DATE)` that verifies the cutoff date is at least 7 years old before detaching any partition: `IF cutoff_date > (CURRENT_DATE - INTERVAL '7 years') THEN RAISE EXCEPTION 'Cannot archive partitions less than 7 years old per HIPAA DR-016.'`. Partitions older than 7 years are DETACHED (not dropped) to allow offline archival.
   - The retention verification script is designed to be run by a scheduled job quarterly as a compliance check.

7. **Revoke DML Permissions on `audit_logs`** — Execute `REVOKE UPDATE, DELETE ON audit_logs FROM public` and from the application database role. Grant only `INSERT` and `SELECT` permissions to the application role: `GRANT INSERT, SELECT ON audit_logs TO app_role`. This provides a third layer of immutability protection (in addition to service-layer and trigger enforcement). Include `REVOKE` statements for all child partitions and the default partition.

8. **Create EF Core Migration with Rollback** — Wrap all the above changes into a single EF Core migration class using `migrationBuilder.Sql()` for raw SQL execution. The `Up()` method applies partitioning, indexes, triggers, permissions, and retention comments. The `Down()` method provides rollback: drop triggers, drop trigger function, drop indexes, rename partitioned table, recreate non-partitioned table, restore data, restore original permissions. Verify migration can be applied and rolled back cleanly using `dotnet ef migrations script` (DR-029, DR-032). Name the migration: `YYYYMMDD_AddAuditLogPartitioningAndImmutability`.

## Current Project State

- [Placeholder — to be updated based on completion of dependent tasks US_008]

```text
Server/
├── Data/
│   ├── ApplicationDbContext.cs
│   ├── Configurations/
│   │   └── AuditLogConfiguration.cs
│   ├── Migrations/
│   │   └── [existing migrations from US_008]
│   └── Scripts/
└── Models/
    └── Entities/
        ├── AuditLog.cs
        └── Enums/
            └── AuditAction.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Data/Migrations/YYYYMMDD_AddAuditLogPartitioningAndImmutability.cs | EF Core migration with partitioning, indexes, triggers, permissions, and rollback |
| CREATE | Server/Data/Scripts/create_audit_partitions.sql | PL/pgSQL function for idempotent monthly partition creation |
| CREATE | Server/Data/Scripts/audit_retention_policy.sql | Retention verification query, safety archive function, and HIPAA compliance comment |
| MODIFY | audit_logs table (PostgreSQL) | Convert to range-partitioned table on timestamp column |
| CREATE | audit_logs_YYYY_MM partitions (PostgreSQL) | Monthly child partitions for current + 12 months forward |
| CREATE | audit_logs_default partition (PostgreSQL) | Default catch-all partition for out-of-range timestamps |
| CREATE | fn_audit_logs_prevent_modification() (PostgreSQL) | Trigger function raising exception on UPDATE/DELETE |
| CREATE | trg_audit_logs_prevent_update (PostgreSQL) | BEFORE UPDATE trigger on audit_logs |
| CREATE | trg_audit_logs_prevent_delete (PostgreSQL) | BEFORE DELETE trigger on audit_logs |

## External References

- [PostgreSQL 16 Table Partitioning](https://www.postgresql.org/docs/16/ddl-partitioning.html)
- [PostgreSQL 16 Range Partitioning](https://www.postgresql.org/docs/16/ddl-partitioning.html#DDL-PARTITIONING-DECLARATIVE)
- [PostgreSQL 16 Trigger Functions (PL/pgSQL)](https://www.postgresql.org/docs/16/plpgsql-trigger.html)
- [PostgreSQL 16 CREATE INDEX CONCURRENTLY](https://www.postgresql.org/docs/16/sql-createindex.html#SQL-CREATEINDEX-CONCURRENTLY)
- [EF Core 8 Raw SQL Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing?tabs=dotnet-core-cli#adding-raw-sql)
- [HIPAA Security Rule — Audit Controls 45 CFR §164.312(b)](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)
- [PostgreSQL 16 GRANT/REVOKE Permissions](https://www.postgresql.org/docs/16/sql-grant.html)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Migration applies cleanly with `dotnet ef database update`
- [ ] Migration rolls back cleanly with `dotnet ef database update <previous_migration>`
- [ ] `audit_logs` table is partitioned (verify via `\d+ audit_logs` showing partitions)
- [ ] Monthly partitions exist for current + 12 months forward
- [ ] Default partition catches out-of-range timestamps
- [ ] `UPDATE audit_logs SET action = 'Login' WHERE log_id = ...` raises EXCEPTION (AC-2)
- [ ] `DELETE FROM audit_logs WHERE log_id = ...` raises EXCEPTION (AC-2)
- [ ] `INSERT INTO audit_logs (...)` succeeds without trigger interference (AC-1)
- [ ] Composite indexes exist and are used by query plans (verify with `EXPLAIN ANALYZE`)
- [ ] Application role has only INSERT and SELECT permissions on audit_logs
- [ ] Retention verification script confirms 7-year policy (AC-4, DR-016)
- [ ] Partition creation script is idempotent (re-execution does not error)

## Implementation Checklist

- [ ] Create EF Core migration converting `audit_logs` to range-partitioned table on `timestamp` with data migration and rollback (DR-029)
- [ ] Create monthly partitions (current + 12 months forward) with default partition and `audit_logs_YYYY_MM` naming convention
- [ ] Add composite indexes: `(user_id, timestamp DESC)`, `(entity_type, timestamp DESC)`, `(action, timestamp DESC)`, `(timestamp DESC)` using CONCURRENTLY (NFR-021)
- [ ] Create `fn_audit_logs_prevent_modification()` trigger function raising EXCEPTION on UPDATE/DELETE with HIPAA error message (AC-2)
- [ ] Attach `trg_audit_logs_prevent_update` and `trg_audit_logs_prevent_delete` BEFORE triggers to `audit_logs` parent table (AC-2)
- [ ] Create `audit_retention_policy.sql` with 7-year verification query and safe archive function with date guard (AC-4, DR-016)
- [ ] Revoke UPDATE/DELETE permissions, grant only INSERT/SELECT to application database role
- [ ] Create `create_audit_partitions.sql` with idempotent PL/pgSQL function for scheduled monthly partition pre-creation
