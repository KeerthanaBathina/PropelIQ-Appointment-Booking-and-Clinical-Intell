# Task - task_001_db_seed_data_script

## Requirement Reference

- User Story: us_011
- Story Location: .propel/context/tasks/EP-DATA/us_011/us_011.md
- Acceptance Criteria:
  - AC-1: Given a fresh database, When the seed script runs, Then it populates at least 10 patients, 50 appointments, 5 users (Patient, Staff, Admin), 20 clinical documents, and corresponding extracted data.
  - AC-2: Given seed data includes appointments, When the data is queried, Then appointments span past, current, and future dates with all status values (scheduled, completed, cancelled, no-show).
  - AC-3: Given seed data includes clinical documents, When the data is queried, Then documents include all categories (lab_result, prescription, clinical_note, imaging_report) with realistic extracted data.
  - AC-4: Given the seed script is idempotent, When it runs multiple times, Then it does not create duplicate records and can reset data to a known state.
- Edge Case:
  - What happens when seed script runs against a production database? Script checks for environment variable and aborts if environment is "Production".
  - How does the system handle seed data for optimistic locking? Appointment version fields start at 1.

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
| Deployment | PowerShell Scripts | 5.1+ |

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

Create a comprehensive SQL seed data script (`scripts/seed-data.sql`) that populates all 10 domain entity tables with realistic healthcare mock data. The script inserts 5 users (1 admin, 2 staff, 2 patient-role users), 10 patients, 50 appointments spanning past/current/future dates with all 4 status values, 10 intake data records with JSONB content, 20 clinical documents across all 4 categories, 30+ extracted data records with confidence scores, 15 medical codes (ICD-10 and CPT), 20 audit log entries, 10 queue entries, and 25 notification logs. The script is idempotent via `TRUNCATE CASCADE` at the start, guarded against production execution, and uses deterministic UUIDs for reproducibility. A companion PowerShell script (`scripts/seed-database.ps1`) wraps execution with environment checks.

## Dependent Tasks

- US_008 task_002_be_efcore_configuration_migrations — Domain entity tables must exist (CreateDomainEntities migration applied).
- US_010 task_001_be_constraint_migration — Referential integrity constraints must be in place (AddReferentialIntegrityConstraints migration applied).
- US_005 task_001_be_identity_configuration — Identity tables (AspNetUsers, AspNetRoles) must exist for user seed data.

## Impacted Components

- **NEW** `scripts/seed-data.sql` — SQL script with INSERT statements for all 10 domain entities, TRUNCATE idempotency, production guard, deterministic UUIDs
- **NEW** `scripts/seed-database.ps1` — PowerShell wrapper script with environment variable check, psql invocation, and success/failure reporting

## Implementation Plan

1. **Add production safety guard**: Begin `seed-data.sql` with a PL/pgSQL block that checks the `UPACIP_ENVIRONMENT` database-level setting (set via `ALTER DATABASE upacip SET upacip.environment = 'Development'`). If the value is `'Production'`, the script raises an exception and aborts: `RAISE EXCEPTION 'SEED SCRIPT ABORTED: Cannot seed a Production database'`. The companion PowerShell script also checks `$env:ASPNETCORE_ENVIRONMENT` before invoking psql.

2. **Implement idempotent reset via TRUNCATE CASCADE**: After the production guard, execute `TRUNCATE TABLE notification_logs, queue_entries, audit_logs, medical_codes, extracted_data, clinical_documents, intake_data, appointments, patients CASCADE;`. This removes all existing seed data while respecting FK relationships. Identity/user tables use a conditional approach: delete seed users by known UUIDs rather than truncating the entire identity table (to preserve any non-seed users during development).

3. **Seed 5 Users with deterministic UUIDs**: Insert into `asp_net_users` (or the Identity table name used by the project) with fixed GUIDs:
   - `00000000-0000-0000-0000-000000000001` — Admin user (admin@upacip.dev, role: Admin)
   - `00000000-0000-0000-0000-000000000002` — Staff user 1 (staff1@upacip.dev, role: Staff)
   - `00000000-0000-0000-0000-000000000003` — Staff user 2 (staff2@upacip.dev, role: Staff)
   - `00000000-0000-0000-0000-000000000004` — Patient user 1 (patient1@upacip.dev, role: Patient)
   - `00000000-0000-0000-0000-000000000005` — Patient user 2 (patient2@upacip.dev, role: Patient)
   Password hashes use bcrypt of `"SeedPassword1!"` (development only). Also seed the corresponding role assignments in `asp_net_user_roles`.

4. **Seed 10 Patients with realistic healthcare data**: Insert patients with realistic names, dates of birth spanning ages 18-85, phone numbers, emergency contacts, and unique emails (`patient01@test.upacip.dev` through `patient10@test.upacip.dev`). Use deterministic UUIDs `10000000-0000-0000-0000-00000000000X`. All patients have `deleted_at = NULL` (active), except patient 10 which has `deleted_at` set (soft-deleted) to exercise the global query filter.

5. **Seed 50 Appointments spanning all statuses and date ranges**: Distribute appointments across patients with:
   - 15 appointments in the past (completed and no-show statuses)
   - 10 appointments today/this week (scheduled and in-progress)
   - 15 appointments in the future (scheduled)
   - 5 cancelled appointments
   - 5 walk-in appointments (`is_walk_in = true`)
   All appointments have `version = 1` (optimistic locking initial state) and deterministic UUIDs `20000000-0000-0000-0000-0000000000XX`. Include `preferred_slot_criteria` JSONB for 10 appointments (e.g., `{"preferred_day": "Monday", "preferred_time": "morning"}`).

6. **Seed 20 Clinical Documents across all 4 categories**: Distribute evenly: 5 lab_result, 5 prescription, 5 clinical_note, 5 imaging_report. Use `uploader_user_id` pointing to staff users. Set `processing_status` to a mix of completed (15), processing (3), queued (1), failed (1). File paths use the pattern `/documents/seed/<category>/<uuid>.pdf`. Deterministic UUIDs `30000000-0000-0000-0000-0000000000XX`.

7. **Seed extracted data, medical codes, intake data, audit logs, queue entries, and notification logs**: 
   - **30 ExtractedData**: Link to clinical documents with `data_type` covering all 4 types (medication, diagnosis, procedure, allergy). Confidence scores range 0.65-0.98. `data_content` JSONB with realistic entries (e.g., `{"medication_name": "Lisinopril", "dosage": "10mg", "frequency": "daily"}`). 10 flagged_for_review, 15 verified by staff users.
   - **15 MedicalCodes**: Mix of ICD-10 (e.g., I10 Hypertension, E11.9 Type 2 Diabetes) and CPT (e.g., 99213 Office Visit). 10 suggested_by_ai with confidence scores, 8 approved by staff.
   - **10 IntakeData**: Linked to patients, mix of `ai_conversational` and `manual_form` methods. JSONB fields with realistic intake content.
   - **20 AuditLogs**: Mix of login, data_access, data_modify actions. Linked to seed users.
   - **10 QueueEntries**: Linked to today's appointments, mix of waiting/in_visit/completed statuses.
   - **25 NotificationLogs**: Linked to appointments, covering all notification types and delivery channels. Mix of sent/failed/bounced statuses with retry counts.

8. **Add verification queries at end of script**: Append `SELECT` count queries that verify minimum data volumes: `SELECT COUNT(*) AS patient_count FROM patients; SELECT COUNT(*) AS appointment_count FROM appointments;` etc. Print a summary line confirming seed was successful.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/ (10 domain entities)
│       ├── Configurations/ (9 config files)
│       └── Migrations/
│           ├── *_CreateDomainEntities.cs
│           └── *_AddReferentialIntegrityConstraints.cs
├── app/
└── scripts/
    ├── provision-database.ps1
    ├── provision-database.sql
    └── provision-pgvector.sql
```

> Assumes US_008 (entity models + migration) and US_010 (constraints migration) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | scripts/seed-data.sql | SQL script: production guard, TRUNCATE CASCADE, INSERT statements for all 10 entity types with deterministic UUIDs, realistic healthcare data, verification queries |
| CREATE | scripts/seed-database.ps1 | PowerShell wrapper: check ASPNETCORE_ENVIRONMENT, invoke psql with seed-data.sql, report success/failure counts |

## External References

- [PostgreSQL TRUNCATE CASCADE](https://www.postgresql.org/docs/16/sql-truncate.html)
- [PostgreSQL PL/pgSQL RAISE EXCEPTION](https://www.postgresql.org/docs/16/plpgsql-errors-and-messages.html)
- [BCrypt.Net-Next — Generating Test Password Hashes](https://github.com/BcryptNet/bcrypt.net)
- [ICD-10 Code Reference](https://www.icd10data.com/)
- [CPT Code Reference](https://www.ama-assn.org/practice-management/cpt)
- [ERD Reference — models.md Logical Data Model](../../../docs/models.md)

## Build Commands

```powershell
# Run seed script directly via psql
psql -U upacip_app -d upacip -f scripts/seed-data.sql

# Run via PowerShell wrapper (includes environment check)
$env:ASPNETCORE_ENVIRONMENT = "Development"
.\scripts\seed-database.ps1

# Verify seed data counts
psql -U upacip_app -d upacip -c "SELECT 'patients' AS entity, COUNT(*) FROM patients UNION ALL SELECT 'appointments', COUNT(*) FROM appointments UNION ALL SELECT 'clinical_documents', COUNT(*) FROM clinical_documents UNION ALL SELECT 'extracted_data', COUNT(*) FROM extracted_data UNION ALL SELECT 'medical_codes', COUNT(*) FROM medical_codes;"

# Re-run to verify idempotency (counts should remain the same)
.\scripts\seed-database.ps1
psql -U upacip_app -d upacip -c "SELECT COUNT(*) FROM patients;"
# Expected: 10 (not 20)
```

## Implementation Validation Strategy

- [ ] Seed script runs without errors on a fresh database (after migrations)
- [ ] Patient count ≥ 10, Appointment count ≥ 50, User count ≥ 5, ClinicalDocument count ≥ 20
- [ ] Appointments include all 4 statuses (scheduled, completed, cancelled, no-show) and span past/current/future dates
- [ ] Clinical documents include all 4 categories (lab_result, prescription, clinical_note, imaging_report) with linked extracted data
- [ ] Running the script twice produces identical data (idempotent — no duplicates)
- [ ] Script aborts with error message if `UPACIP_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT` is "Production"
- [ ] All Appointment `version` fields are set to 1
- [ ] One Patient has `deleted_at` set to exercise soft-delete query filter

## Implementation Checklist

- [x] Create `scripts/seed-data.sql` with PL/pgSQL production guard checking `upacip.environment` setting and `TRUNCATE CASCADE` for idempotent reset
- [x] Insert 5 users (1 Admin, 2 Staff, 2 Patient) with deterministic UUIDs, bcrypt password hashes, and role assignments into Identity tables
- [x] Insert 10 patients with realistic healthcare demographics, unique emails, deterministic UUIDs, and one soft-deleted patient (`deleted_at` set)
- [x] Insert 50 appointments distributed across past/current/future dates with all 4 status values, `version = 1`, JSONB `preferred_slot_criteria` on 10 records, and 5 walk-in appointments
- [x] Insert 20 clinical documents across all 4 categories with varied `processing_status` values, linked to patients and staff uploaders
- [x] Insert 30+ extracted data records with JSONB `data_content`, confidence scores (0.65-0.98), `flagged_for_review` flags, and verification attribution; plus 15 medical codes, 10 intake data, 20 audit logs, 10 queue entries, 25 notification logs
- [x] Create `scripts/seed-database.ps1` PowerShell wrapper with `ASPNETCORE_ENVIRONMENT` check, psql invocation, and count verification output
- [x] Add verification SELECT queries at end of SQL script confirming row counts meet AC-1 minimums
