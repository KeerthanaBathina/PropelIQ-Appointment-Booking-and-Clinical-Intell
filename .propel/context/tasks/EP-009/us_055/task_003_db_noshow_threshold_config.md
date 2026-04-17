# Task - task_003_db_noshow_threshold_config

## Requirement Reference
- User Story: US_055
- Story Location: .propel/context/tasks/EP-009/us_055/us_055.md
- Acceptance Criteria:
    - AC-1: Given a patient has not been marked as arrived, When 15 minutes pass after their scheduled appointment time, Then the system automatically marks the appointment as "no-show" and updates the queue.
    - AC-3: Given the wait time threshold is configurable, When an admin changes the threshold value, Then the alert behavior updates immediately for all active queue entries.
    - AC-4: Given auto no-show detection runs, When a patient is marked as no-show, Then the event is logged in the audit trail with timestamp and "auto-detected" attribution.
- Edge Case:
    - System outage recovery: System processes all overdue appointments retroactively on recovery, marking them with "delayed-detection" flag. DB must support the delayed_detection metadata field.

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
| Frontend | N/A | N/A |
| Backend | N/A | N/A |
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
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
Add database schema changes to support configurable wait time threshold and auto no-show detection metadata. This includes creating a `SystemConfig` table for key-value system configuration (used by threshold and future settings), adding `is_auto_noshow` and `is_delayed_detection` columns to the `QueueEntry` table for tracking auto-detected no-shows, and adding an index on `Appointment` for efficient no-show detection queries. All changes are delivered as EF Core code-first migrations with rollback support.

## Dependent Tasks
- US_008 tasks (EP-DATA/us_008/) — QueueEntry, Appointment, and AuditLog entity models and base migrations must exist.

## Impacted Components
- **NEW** `SystemConfig` entity model — Key-value configuration table for system-wide settings (Server/Models/)
- **NEW** EF Core migration `AddNoShowDetectionSchema` — Migration adding SystemConfig table, QueueEntry columns, and Appointment index (Server/Migrations/)
- **MODIFY** `QueueEntry` entity — Add `is_auto_noshow` (bool) and `is_delayed_detection` (bool) columns (Server/Models/)
- **MODIFY** `ApplicationDbContext` — Register SystemConfig entity, configure QueueEntry new columns, add Appointment index (Server/Data/)
- **NEW** Seed data script — Insert default threshold config row (key: `queue.wait_threshold_minutes`, value: `30`) (Server/Data/Seeds/)

## Implementation Plan
1. **Create `SystemConfig` entity model**: Define entity with fields:
   - `config_id` (UUID, PK)
   - `config_key` (string, unique, not null) — e.g., `queue.wait_threshold_minutes`
   - `config_value` (string, not null) — stores value as string; parsed by application layer
   - `description` (string, nullable) — human-readable description
   - `updated_by_user_id` (UUID, FK to User, nullable) — tracks who last changed the config
   - `created_at` (timestamp, not null, default: now)
   - `updated_at` (timestamp, not null, default: now)

2. **Add columns to `QueueEntry` entity**:
   - `is_auto_noshow` (bool, not null, default: false) — true when no-show was auto-detected by the background service
   - `is_delayed_detection` (bool, not null, default: false) — true when no-show was detected during system outage recovery

3. **Add composite index on `Appointment`**: Create index on `(status, appointment_time)` filtered to `status = 'scheduled'` for efficient no-show detection queries. The background service queries `WHERE status = 'scheduled' AND appointment_time < @threshold_time` every 60 seconds — this index prevents full table scans.

4. **Add index on `SystemConfig`**: Create unique index on `config_key` for O(1) lookups.

5. **Create EF Core migration**: Generate migration `AddNoShowDetectionSchema` using `dotnet ef migrations add AddNoShowDetectionSchema`. Verify the generated migration includes:
   - CREATE TABLE `system_configs`
   - ALTER TABLE `queue_entries` ADD COLUMN `is_auto_noshow`, `is_delayed_detection`
   - CREATE INDEX on `appointments` (status, appointment_time)
   - CREATE UNIQUE INDEX on `system_configs` (config_key)

6. **Create seed data**: Insert default configuration row:
   ```sql
   INSERT INTO system_configs (config_id, config_key, config_value, description, created_at, updated_at)
   VALUES (gen_random_uuid(), 'queue.wait_threshold_minutes', '30', 'Wait time threshold in minutes for staff alerts (default: 30)', NOW(), NOW());
   ```

7. **Verify rollback**: Ensure migration Down() method drops the SystemConfig table, removes QueueEntry columns, and drops the Appointment index cleanly.

## Current Project State
- [Placeholder — to be updated based on completion of dependent task US_008]

## Expected Changes
| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/SystemConfig.cs | SystemConfig entity model with config_key/config_value fields |
| CREATE | Server/Migrations/YYYYMMDD_AddNoShowDetectionSchema.cs | EF Core migration: SystemConfig table, QueueEntry columns, Appointment index |
| MODIFY | Server/Models/QueueEntry.cs | Add is_auto_noshow (bool) and is_delayed_detection (bool) properties |
| MODIFY | Server/Data/ApplicationDbContext.cs | Register SystemConfig DbSet, configure new columns and indexes |
| CREATE | Server/Data/Seeds/SeedSystemConfig.cs | Seed default threshold config row (queue.wait_threshold_minutes = 30) |

> Only list concrete, verifiable file operations. No speculative directory trees.

## External References
- [EF Core 8 Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli) — Code-first migration creation and rollback patterns
- [EF Core 8 Index Configuration](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=data-annotations) — Filtered index configuration for PostgreSQL
- [Npgsql PostgreSQL Provider](https://www.npgsql.org/efcore/mapping/general.html) — PostgreSQL-specific EF Core type mapping and index syntax
- [PostgreSQL Filtered Indexes](https://www.postgresql.org/docs/16/indexes-partial.html) — Partial index syntax for status-filtered appointment queries

## Build Commands
- [Refer to applicable technology stack specific build commands](.propel/build/)

## Implementation Validation Strategy
- [ ] Unit tests pass
- [ ] Migration applies successfully: `dotnet ef database update`
- [ ] Migration rolls back cleanly: `dotnet ef database update <previous_migration>`
- [ ] SystemConfig table created with unique index on config_key
- [ ] Default threshold seed data present (queue.wait_threshold_minutes = 30)
- [ ] QueueEntry table has is_auto_noshow and is_delayed_detection columns with default false
- [ ] Appointment index on (status, appointment_time) exists and is used by EXPLAIN ANALYZE
- [ ] Foreign key on updated_by_user_id references User table correctly
- [ ] All column defaults are correct (bool = false, timestamps = NOW())

## Implementation Checklist
- [ ] Create `SystemConfig` entity model with config_key, config_value, and audit fields
- [ ] Add `is_auto_noshow` and `is_delayed_detection` columns to QueueEntry entity
- [ ] Configure composite index on Appointment (status, appointment_time) for no-show detection
- [ ] Configure unique index on SystemConfig (config_key)
- [ ] Generate EF Core migration `AddNoShowDetectionSchema`
- [ ] Create seed data script for default threshold config (30 minutes)
- [ ] Verify migration rollback (Down method) drops all created artifacts cleanly

**Traceability:** US_055 AC-1, AC-3, AC-4 | FR-076, FR-079 | DR-008, DR-009 | UC-008
