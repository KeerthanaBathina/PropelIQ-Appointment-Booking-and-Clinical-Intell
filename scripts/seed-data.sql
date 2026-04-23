-- =============================================================================
-- UPACIP Seed Data Script
-- Version: 1.0.0  |  Target: PostgreSQL 16+
-- =============================================================================
-- Purpose : Populate all 10 domain entity tables with realistic healthcare
--           mock data for development, QA, and demo environments.
-- Idempotent: TRUNCATE CASCADE + deterministic UUIDs — running twice yields
--           the same row counts (no duplicates).
-- Safety  : Aborts if upacip.environment = 'Production'.
--
-- Run via:
--   psql -U upacip_app -d upacip -f scripts/seed-data.sql
--   .\scripts\seed-database.ps1   (recommended — includes env checks)
--
-- Password note:
--   All seed users share the BCrypt hash below (cleartext: SeedPassword1!).
--   This hash was pre-computed with BCrypt.Net-Next (work factor 10).
--   NEVER use seed credentials outside development.
-- =============================================================================

-- ---------------------------------------------------------------------------
-- SECTION 0 — Production safety guard
-- ---------------------------------------------------------------------------
-- The database-level setting 'upacip.environment' is set via:
--   ALTER DATABASE upacip SET upacip.environment = 'Development';
-- If it is ever set to 'Production' this block aborts the entire script.
DO $$
BEGIN
    IF current_setting('upacip.environment', true) = 'Production' THEN
        RAISE EXCEPTION
            'SEED SCRIPT ABORTED: Cannot seed a Production database. '
            'Set upacip.environment to Development first.';
    END IF;
END;
$$;

-- ---------------------------------------------------------------------------
-- SECTION 1 — Idempotent reset
-- ---------------------------------------------------------------------------
-- Domain tables: TRUNCATE CASCADE handles FK dependency ordering automatically.
TRUNCATE TABLE
    notification_logs,
    queue_entries,
    extracted_data,
    audit_logs,
    medical_codes,
    intake_data,
    clinical_documents,
    appointments,
    patients,
    cpt_bundle_rules,
    cpt_code_library,
    payer_rule_violations,
    payer_rules,
    bundling_edits,
    code_modifiers
RESTART IDENTITY CASCADE;

-- Identity tables: delete seed rows by known UUIDs to preserve non-seed users.
DELETE FROM asp_net_user_roles
WHERE "UserId" IN (
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    '00000000-0000-0000-0000-000000000003',
    '00000000-0000-0000-0000-000000000004',
    '00000000-0000-0000-0000-000000000005'
);

DELETE FROM asp_net_users
WHERE "Id" IN (
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    '00000000-0000-0000-0000-000000000003',
    '00000000-0000-0000-0000-000000000004',
    '00000000-0000-0000-0000-000000000005'
);

-- ---------------------------------------------------------------------------
-- SECTION 2 — Identity users (5 records)
-- ---------------------------------------------------------------------------
-- BCrypt(SeedPassword1!, workFactor=10) — development only; never use in prod.
INSERT INTO asp_net_users (
    "Id", "FullName", "DateOfBirth", "CreatedAt", "UpdatedAt", "DeletedAt",
    "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
    "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
    "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled",
    "LockoutEnd", "LockoutEnabled", "AccessFailedCount"
) VALUES
-- Admin
(
    '00000000-0000-0000-0000-000000000001',
    'System Administrator',
    '1980-06-15',
    '2026-01-01 08:00:00+00', '2026-01-01 08:00:00+00', NULL,
    'admin@upacip.dev', 'ADMIN@UPACIP.DEV',
    'admin@upacip.dev', 'ADMIN@UPACIP.DEV',
    true,
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'SEED-SECURITY-STAMP-ADMIN-001',
    'SEED-CONCURRENCY-STAMP-ADMIN-001',
    '+15550000001', true, false, NULL, true, 0
),
-- Staff 1
(
    '00000000-0000-0000-0000-000000000002',
    'Sarah Mitchell',
    '1985-03-22',
    '2026-01-01 08:00:00+00', '2026-01-01 08:00:00+00', NULL,
    'staff1@upacip.dev', 'STAFF1@UPACIP.DEV',
    'staff1@upacip.dev', 'STAFF1@UPACIP.DEV',
    true,
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'SEED-SECURITY-STAMP-STAFF-002',
    'SEED-CONCURRENCY-STAMP-STAFF-002',
    '+15550000002', true, false, NULL, true, 0
),
-- Staff 2
(
    '00000000-0000-0000-0000-000000000003',
    'James Thornton',
    '1978-11-08',
    '2026-01-01 08:00:00+00', '2026-01-01 08:00:00+00', NULL,
    'staff2@upacip.dev', 'STAFF2@UPACIP.DEV',
    'staff2@upacip.dev', 'STAFF2@UPACIP.DEV',
    true,
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'SEED-SECURITY-STAMP-STAFF-003',
    'SEED-CONCURRENCY-STAMP-STAFF-003',
    '+15550000003', true, false, NULL, true, 0
),
-- Patient user 1
(
    '00000000-0000-0000-0000-000000000004',
    'Alice Johnson',
    '1990-07-04',
    '2026-01-01 08:00:00+00', '2026-01-01 08:00:00+00', NULL,
    'patient1@upacip.dev', 'PATIENT1@UPACIP.DEV',
    'patient1@upacip.dev', 'PATIENT1@UPACIP.DEV',
    true,
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'SEED-SECURITY-STAMP-PAT-004',
    'SEED-CONCURRENCY-STAMP-PAT-004',
    '+15550000004', true, false, NULL, true, 0
),
-- Patient user 2
(
    '00000000-0000-0000-0000-000000000005',
    'Robert Kim',
    '1965-12-30',
    '2026-01-01 08:00:00+00', '2026-01-01 08:00:00+00', NULL,
    'patient2@upacip.dev', 'PATIENT2@UPACIP.DEV',
    'patient2@upacip.dev', 'PATIENT2@UPACIP.DEV',
    true,
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'SEED-SECURITY-STAMP-PAT-005',
    'SEED-CONCURRENCY-STAMP-PAT-005',
    '+15550000005', true, false, NULL, true, 0
);

-- Role assignments
-- Role GUIDs from AddIdentitySchema migration:
--   Admin:   c3d4e5f6-a7b8-9c0d-1e2f-a3b4c5d6e7f8
--   Staff:   b2c3d4e5-f6a7-8b9c-0d1e-f2a3b4c5d6e7
--   Patient: a1b2c3d4-e5f6-7a8b-9c0d-e1f2a3b4c5d6
INSERT INTO asp_net_user_roles ("UserId", "RoleId") VALUES
('00000000-0000-0000-0000-000000000001', 'c3d4e5f6-a7b8-9c0d-1e2f-a3b4c5d6e7f8'), -- admin → Admin
('00000000-0000-0000-0000-000000000002', 'b2c3d4e5-f6a7-8b9c-0d1e-f2a3b4c5d6e7'), -- staff1 → Staff
('00000000-0000-0000-0000-000000000003', 'b2c3d4e5-f6a7-8b9c-0d1e-f2a3b4c5d6e7'), -- staff2 → Staff
('00000000-0000-0000-0000-000000000004', 'a1b2c3d4-e5f6-7a8b-9c0d-e1f2a3b4c5d6'), -- patient1 → Patient
('00000000-0000-0000-0000-000000000005', 'a1b2c3d4-e5f6-7a8b-9c0d-e1f2a3b4c5d6'); -- patient2 → Patient

-- ---------------------------------------------------------------------------
-- SECTION 3 — Patients (10 records)
-- ---------------------------------------------------------------------------
-- UUIDs: 10000000-0000-0000-0000-00000000000X
-- Patient 10 has DeletedAt set (soft-deleted) to exercise the global query filter.
INSERT INTO patients (
    "Id", "Email", "PasswordHash", "FullName", "DateOfBirth",
    "PhoneNumber", "EmergencyContact", "DeletedAt", "CreatedAt", "UpdatedAt"
) VALUES
(
    '10000000-0000-0000-0000-000000000001',
    'patient01@test.upacip.dev',
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'Eleanor Hartley', '1978-04-12',
    '+15551001001', 'David Hartley (+15551001002)',
    NULL, '2026-01-05 09:00:00+00', '2026-01-05 09:00:00+00'
),
(
    '10000000-0000-0000-0000-000000000002',
    'patient02@test.upacip.dev',
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'Marcus Thompson', '1965-09-28',
    '+15551002001', 'Linda Thompson (+15551002002)',
    NULL, '2026-01-07 10:30:00+00', '2026-01-07 10:30:00+00'
),
(
    '10000000-0000-0000-0000-000000000003',
    'patient03@test.upacip.dev',
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'Priya Patel', '1990-02-17',
    '+15551003001', NULL,
    NULL, '2026-01-10 08:15:00+00', '2026-01-10 08:15:00+00'
),
(
    '10000000-0000-0000-0000-000000000004',
    'patient04@test.upacip.dev',
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'James Okafor', '1955-11-03',
    '+15551004001', 'Grace Okafor (+15551004002)',
    NULL, '2026-01-12 14:00:00+00', '2026-01-12 14:00:00+00'
),
(
    '10000000-0000-0000-0000-000000000005',
    'patient05@test.upacip.dev',
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'Maria Santos', '1982-07-25',
    '+15551005001', 'Carlos Santos (+15551005002)',
    NULL, '2026-01-15 11:45:00+00', '2026-01-15 11:45:00+00'
),
(
    '10000000-0000-0000-0000-000000000006',
    'patient06@test.upacip.dev',
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'David Chen', '1940-03-08',
    '+15551006001', 'Wei Chen (+15551006002)',
    NULL, '2026-01-18 09:30:00+00', '2026-01-18 09:30:00+00'
),
(
    '10000000-0000-0000-0000-000000000007',
    'patient07@test.upacip.dev',
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'Jennifer Walsh', '1997-06-14',
    '+15551007001', NULL,
    NULL, '2026-01-20 13:00:00+00', '2026-01-20 13:00:00+00'
),
(
    '10000000-0000-0000-0000-000000000008',
    'patient08@test.upacip.dev',
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'Ahmed Hassan', '1972-01-19',
    '+15551008001', 'Fatima Hassan (+15551008002)',
    NULL, '2026-01-22 10:00:00+00', '2026-01-22 10:00:00+00'
),
(
    '10000000-0000-0000-0000-000000000009',
    'patient09@test.upacip.dev',
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'Susan Brewer', '1988-10-31',
    '+15551009001', 'Tom Brewer (+15551009002)',
    NULL, '2026-01-25 15:30:00+00', '2026-01-25 15:30:00+00'
),
-- Patient 10: soft-deleted — exercises global query filter (p => p.DeletedAt == null)
(
    '10000000-0000-0000-0000-000000000010',
    'patient10@test.upacip.dev',
    '$2a$10$TK/MO5V2drnBAbMB0BzxhOqkOmk5LkqmXKj.L.rl2w3U1lCGiOBjO',
    'Paul Reeves', '1960-08-07',
    '+15551010001', NULL,
    '2026-03-01 12:00:00+00', '2026-01-28 08:00:00+00', '2026-03-01 12:00:00+00'
);

-- ---------------------------------------------------------------------------
-- SECTION 4 — Appointments (50 records)
-- ---------------------------------------------------------------------------
-- UUIDs: 20000000-0000-0000-0000-0000000000XX (01-50)
-- Distribution:
--   01-10  : past Completed (Jan–Mar 2026)
--   11-15  : past NoShow     (Feb–Apr 2026)
--   16-20  : Cancelled       (Feb–Mar 2026)
--   21-30  : Scheduled this week (Apr 19-25 2026) — also have queue entries
--   31-45  : future Scheduled (Apr 26–Jul 2026)
--   46-50  : past Completed walk-ins
-- All Version = 1 (optimistic locking initial state)
-- 10 records include PreferredSlotCriteria JSONB (records 21-30)

INSERT INTO appointments (
    "Id", "PatientId", "AppointmentTime", "Status", "IsWalkIn",
    "Version", "PreferredSlotCriteria", "CreatedAt", "UpdatedAt"
) VALUES
-- Past Completed (01-10)
('20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','2026-01-15 09:00:00+00','Completed',false,1,NULL,'2026-01-10 10:00:00+00','2026-01-15 09:30:00+00'),
('20000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000002','2026-01-22 10:30:00+00','Completed',false,1,NULL,'2026-01-17 10:00:00+00','2026-01-22 11:00:00+00'),
('20000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000003','2026-02-03 14:00:00+00','Completed',false,1,NULL,'2026-01-28 09:00:00+00','2026-02-03 14:45:00+00'),
('20000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000004','2026-02-10 11:00:00+00','Completed',false,1,NULL,'2026-02-05 08:00:00+00','2026-02-10 11:30:00+00'),
('20000000-0000-0000-0000-000000000005','10000000-0000-0000-0000-000000000005','2026-02-17 09:30:00+00','Completed',false,1,NULL,'2026-02-12 10:00:00+00','2026-02-17 10:00:00+00'),
('20000000-0000-0000-0000-000000000006','10000000-0000-0000-0000-000000000006','2026-02-24 15:00:00+00','Completed',false,1,NULL,'2026-02-19 09:00:00+00','2026-02-24 15:30:00+00'),
('20000000-0000-0000-0000-000000000007','10000000-0000-0000-0000-000000000007','2026-03-03 10:00:00+00','Completed',false,1,NULL,'2026-02-26 11:00:00+00','2026-03-03 10:45:00+00'),
('20000000-0000-0000-0000-000000000008','10000000-0000-0000-0000-000000000008','2026-03-10 13:30:00+00','Completed',false,1,NULL,'2026-03-05 09:00:00+00','2026-03-10 14:00:00+00'),
('20000000-0000-0000-0000-000000000009','10000000-0000-0000-0000-000000000009','2026-03-17 08:30:00+00','Completed',false,1,NULL,'2026-03-12 10:00:00+00','2026-03-17 09:00:00+00'),
('20000000-0000-0000-0000-000000000010','10000000-0000-0000-0000-000000000001','2026-03-24 11:00:00+00','Completed',false,1,NULL,'2026-03-19 09:00:00+00','2026-03-24 11:30:00+00'),
-- Past NoShow (11-15)
('20000000-0000-0000-0000-000000000011','10000000-0000-0000-0000-000000000002','2026-02-05 09:00:00+00','NoShow',false,1,NULL,'2026-01-31 10:00:00+00','2026-02-05 10:00:00+00'),
('20000000-0000-0000-0000-000000000012','10000000-0000-0000-0000-000000000003','2026-02-19 14:00:00+00','NoShow',false,1,NULL,'2026-02-14 09:00:00+00','2026-02-19 14:30:00+00'),
('20000000-0000-0000-0000-000000000013','10000000-0000-0000-0000-000000000005','2026-03-04 10:00:00+00','NoShow',false,1,NULL,'2026-02-27 10:00:00+00','2026-03-04 10:30:00+00'),
('20000000-0000-0000-0000-000000000014','10000000-0000-0000-0000-000000000007','2026-03-18 15:00:00+00','NoShow',false,1,NULL,'2026-03-13 09:00:00+00','2026-03-18 15:30:00+00'),
('20000000-0000-0000-0000-000000000015','10000000-0000-0000-0000-000000000009','2026-04-02 09:00:00+00','NoShow',false,1,NULL,'2026-03-28 10:00:00+00','2026-04-02 10:00:00+00'),
-- Cancelled (16-20)
('20000000-0000-0000-0000-000000000016','10000000-0000-0000-0000-000000000004','2026-02-12 10:00:00+00','Cancelled',false,1,NULL,'2026-02-07 09:00:00+00','2026-02-10 16:00:00+00'),
('20000000-0000-0000-0000-000000000017','10000000-0000-0000-0000-000000000006','2026-02-26 11:30:00+00','Cancelled',false,1,NULL,'2026-02-21 10:00:00+00','2026-02-24 09:00:00+00'),
('20000000-0000-0000-0000-000000000018','10000000-0000-0000-0000-000000000008','2026-03-11 14:00:00+00','Cancelled',false,1,NULL,'2026-03-06 09:00:00+00','2026-03-09 11:00:00+00'),
('20000000-0000-0000-0000-000000000019','10000000-0000-0000-0000-000000000001','2026-03-25 09:30:00+00','Cancelled',false,1,NULL,'2026-03-20 10:00:00+00','2026-03-22 14:00:00+00'),
('20000000-0000-0000-0000-000000000020','10000000-0000-0000-0000-000000000003','2026-04-08 15:00:00+00','Cancelled',false,1,NULL,'2026-04-03 09:00:00+00','2026-04-06 10:00:00+00'),
-- This week Scheduled (21-30) — with PreferredSlotCriteria JSONB
('20000000-0000-0000-0000-000000000021','10000000-0000-0000-0000-000000000001','2026-04-19 09:00:00+00','Scheduled',false,1,'{"PreferredDays":["Saturday","Sunday"],"PreferredTimeOfDay":"morning","MaxWaitMinutes":30,"Notes":null}','2026-04-14 10:00:00+00','2026-04-14 10:00:00+00'),
('20000000-0000-0000-0000-000000000022','10000000-0000-0000-0000-000000000002','2026-04-19 11:00:00+00','Scheduled',false,1,'{"PreferredDays":["Monday","Wednesday"],"PreferredTimeOfDay":"morning","MaxWaitMinutes":null,"Notes":"Please avoid early mornings"}','2026-04-14 11:00:00+00','2026-04-14 11:00:00+00'),
('20000000-0000-0000-0000-000000000023','10000000-0000-0000-0000-000000000003','2026-04-20 10:00:00+00','Scheduled',false,1,'{"PreferredDays":["Tuesday","Thursday"],"PreferredTimeOfDay":"afternoon","MaxWaitMinutes":45,"Notes":null}','2026-04-15 09:00:00+00','2026-04-15 09:00:00+00'),
('20000000-0000-0000-0000-000000000024','10000000-0000-0000-0000-000000000004','2026-04-20 14:00:00+00','Scheduled',false,1,'{"PreferredDays":["Monday"],"PreferredTimeOfDay":"afternoon","MaxWaitMinutes":60,"Notes":"Wheelchair access required"}','2026-04-15 10:00:00+00','2026-04-15 10:00:00+00'),
('20000000-0000-0000-0000-000000000025','10000000-0000-0000-0000-000000000005','2026-04-21 09:30:00+00','Scheduled',false,1,'{"PreferredDays":["Monday","Friday"],"PreferredTimeOfDay":"morning","MaxWaitMinutes":20,"Notes":null}','2026-04-16 09:00:00+00','2026-04-16 09:00:00+00'),
('20000000-0000-0000-0000-000000000026','10000000-0000-0000-0000-000000000006','2026-04-21 11:30:00+00','Scheduled',false,1,'{"PreferredDays":["Tuesday"],"PreferredTimeOfDay":"morning","MaxWaitMinutes":null,"Notes":"Hearing impaired patient"}','2026-04-16 10:00:00+00','2026-04-16 10:00:00+00'),
('20000000-0000-0000-0000-000000000027','10000000-0000-0000-0000-000000000007','2026-04-22 13:00:00+00','Scheduled',false,1,'{"PreferredDays":["Wednesday","Thursday"],"PreferredTimeOfDay":"afternoon","MaxWaitMinutes":30,"Notes":null}','2026-04-17 09:00:00+00','2026-04-17 09:00:00+00'),
('20000000-0000-0000-0000-000000000028','10000000-0000-0000-0000-000000000008','2026-04-22 15:00:00+00','Scheduled',false,1,'{"PreferredDays":["Wednesday"],"PreferredTimeOfDay":"afternoon","MaxWaitMinutes":45,"Notes":"Interpreter needed — Arabic"}','2026-04-17 10:00:00+00','2026-04-17 10:00:00+00'),
('20000000-0000-0000-0000-000000000029','10000000-0000-0000-0000-000000000009','2026-04-23 10:00:00+00','Scheduled',false,1,'{"PreferredDays":["Thursday","Friday"],"PreferredTimeOfDay":"morning","MaxWaitMinutes":30,"Notes":null}','2026-04-18 09:00:00+00','2026-04-18 09:00:00+00'),
('20000000-0000-0000-0000-000000000030','10000000-0000-0000-0000-000000000002','2026-04-25 14:00:00+00','Scheduled',false,1,'{"PreferredDays":["Saturday"],"PreferredTimeOfDay":"afternoon","MaxWaitMinutes":null,"Notes":"Annual check-up"}','2026-04-18 10:00:00+00','2026-04-18 10:00:00+00'),
-- Future Scheduled (31-45)
('20000000-0000-0000-0000-000000000031','10000000-0000-0000-0000-000000000001','2026-04-28 09:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000032','10000000-0000-0000-0000-000000000003','2026-04-30 10:30:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000033','10000000-0000-0000-0000-000000000004','2026-05-05 14:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000034','10000000-0000-0000-0000-000000000005','2026-05-08 09:30:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000035','10000000-0000-0000-0000-000000000006','2026-05-12 11:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000036','10000000-0000-0000-0000-000000000007','2026-05-14 15:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000037','10000000-0000-0000-0000-000000000008','2026-05-19 10:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000038','10000000-0000-0000-0000-000000000009','2026-05-26 09:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000039','10000000-0000-0000-0000-000000000001','2026-06-02 14:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000040','10000000-0000-0000-0000-000000000002','2026-06-09 11:30:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000041','10000000-0000-0000-0000-000000000003','2026-06-16 09:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000042','10000000-0000-0000-0000-000000000004','2026-06-23 13:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000043','10000000-0000-0000-0000-000000000005','2026-07-01 10:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000044','10000000-0000-0000-0000-000000000007','2026-07-07 15:00:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
('20000000-0000-0000-0000-000000000045','10000000-0000-0000-0000-000000000009','2026-07-14 09:30:00+00','Scheduled',false,1,NULL,'2026-04-19 08:00:00+00','2026-04-19 08:00:00+00'),
-- Walk-in Completed (46-50)
('20000000-0000-0000-0000-000000000046','10000000-0000-0000-0000-000000000002','2026-03-05 08:15:00+00','Completed',true,1,NULL,'2026-03-05 08:00:00+00','2026-03-05 09:00:00+00'),
('20000000-0000-0000-0000-000000000047','10000000-0000-0000-0000-000000000005','2026-03-12 10:45:00+00','Completed',true,1,NULL,'2026-03-12 10:30:00+00','2026-03-12 11:30:00+00'),
('20000000-0000-0000-0000-000000000048','10000000-0000-0000-0000-000000000006','2026-03-19 14:30:00+00','Completed',true,1,NULL,'2026-03-19 14:15:00+00','2026-03-19 15:15:00+00'),
('20000000-0000-0000-0000-000000000049','10000000-0000-0000-0000-000000000008','2026-04-02 09:30:00+00','Completed',true,1,NULL,'2026-04-02 09:15:00+00','2026-04-02 10:30:00+00'),
('20000000-0000-0000-0000-000000000050','10000000-0000-0000-0000-000000000009','2026-04-09 11:15:00+00','Completed',true,1,NULL,'2026-04-09 11:00:00+00','2026-04-09 12:00:00+00');

-- ---------------------------------------------------------------------------
-- SECTION 5 — Clinical Documents (20 records)
-- ---------------------------------------------------------------------------
-- UUIDs: 30000000-0000-0000-0000-0000000000XX
-- 5 LabResult (01-05), 5 Prescription (06-10), 5 ClinicalNote (11-15), 5 ImagingReport (16-20)
-- UploaderUserId: staff1 (002) and staff2 (003) alternating
-- ProcessingStatus: 15 Completed, 3 Processing, 1 Queued, 1 Failed

INSERT INTO clinical_documents (
    "Id", "PatientId", "DocumentCategory", "FilePath",
    "UploadDate", "UploaderUserId", "ProcessingStatus", "CreatedAt", "UpdatedAt"
) VALUES
-- LabResult (01-05)
('30000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','LabResult','/documents/seed/lab_result/30000000-0000-0000-0000-000000000001.pdf','2026-01-16 10:00:00+00','00000000-0000-0000-0000-000000000002','Completed','2026-01-16 10:00:00+00','2026-01-16 11:00:00+00'),
('30000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000002','LabResult','/documents/seed/lab_result/30000000-0000-0000-0000-000000000002.pdf','2026-01-23 10:00:00+00','00000000-0000-0000-0000-000000000003','Completed','2026-01-23 10:00:00+00','2026-01-23 11:00:00+00'),
('30000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000003','LabResult','/documents/seed/lab_result/30000000-0000-0000-0000-000000000003.pdf','2026-02-04 10:00:00+00','00000000-0000-0000-0000-000000000002','Completed','2026-02-04 10:00:00+00','2026-02-04 11:30:00+00'),
('30000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000004','LabResult','/documents/seed/lab_result/30000000-0000-0000-0000-000000000004.pdf','2026-02-11 10:00:00+00','00000000-0000-0000-0000-000000000003','Processing','2026-02-11 10:00:00+00','2026-02-11 10:05:00+00'),
('30000000-0000-0000-0000-000000000005','10000000-0000-0000-0000-000000000005','LabResult','/documents/seed/lab_result/30000000-0000-0000-0000-000000000005.pdf','2026-02-18 10:00:00+00','00000000-0000-0000-0000-000000000002','Completed','2026-02-18 10:00:00+00','2026-02-18 11:00:00+00'),
-- Prescription (06-10)
('30000000-0000-0000-0000-000000000006','10000000-0000-0000-0000-000000000001','Prescription','/documents/seed/prescription/30000000-0000-0000-0000-000000000006.pdf','2026-01-16 10:30:00+00','00000000-0000-0000-0000-000000000003','Completed','2026-01-16 10:30:00+00','2026-01-16 11:30:00+00'),
('30000000-0000-0000-0000-000000000007','10000000-0000-0000-0000-000000000006','Prescription','/documents/seed/prescription/30000000-0000-0000-0000-000000000007.pdf','2026-02-25 10:00:00+00','00000000-0000-0000-0000-000000000002','Completed','2026-02-25 10:00:00+00','2026-02-25 11:00:00+00'),
('30000000-0000-0000-0000-000000000008','10000000-0000-0000-0000-000000000007','Prescription','/documents/seed/prescription/30000000-0000-0000-0000-000000000008.pdf','2026-03-04 10:00:00+00','00000000-0000-0000-0000-000000000003','Completed','2026-03-04 10:00:00+00','2026-03-04 11:00:00+00'),
('30000000-0000-0000-0000-000000000009','10000000-0000-0000-0000-000000000008','Prescription','/documents/seed/prescription/30000000-0000-0000-0000-000000000009.pdf','2026-03-11 10:00:00+00','00000000-0000-0000-0000-000000000002','Processing','2026-03-11 10:00:00+00','2026-03-11 10:05:00+00'),
('30000000-0000-0000-0000-000000000010','10000000-0000-0000-0000-000000000009','Prescription','/documents/seed/prescription/30000000-0000-0000-0000-000000000010.pdf','2026-03-18 10:00:00+00','00000000-0000-0000-0000-000000000003','Completed','2026-03-18 10:00:00+00','2026-03-18 11:00:00+00'),
-- ClinicalNote (11-15)
('30000000-0000-0000-0000-000000000011','10000000-0000-0000-0000-000000000002','ClinicalNote','/documents/seed/clinical_note/30000000-0000-0000-0000-000000000011.pdf','2026-01-23 11:00:00+00','00000000-0000-0000-0000-000000000002','Completed','2026-01-23 11:00:00+00','2026-01-23 12:00:00+00'),
('30000000-0000-0000-0000-000000000012','10000000-0000-0000-0000-000000000003','ClinicalNote','/documents/seed/clinical_note/30000000-0000-0000-0000-000000000012.pdf','2026-02-04 11:00:00+00','00000000-0000-0000-0000-000000000003','Completed','2026-02-04 11:00:00+00','2026-02-04 12:00:00+00'),
('30000000-0000-0000-0000-000000000013','10000000-0000-0000-0000-000000000005','ClinicalNote','/documents/seed/clinical_note/30000000-0000-0000-0000-000000000013.pdf','2026-02-18 11:00:00+00','00000000-0000-0000-0000-000000000002','Completed','2026-02-18 11:00:00+00','2026-02-18 12:00:00+00'),
('30000000-0000-0000-0000-000000000014','10000000-0000-0000-0000-000000000007','ClinicalNote','/documents/seed/clinical_note/30000000-0000-0000-0000-000000000014.pdf','2026-03-04 11:00:00+00','00000000-0000-0000-0000-000000000003','Failed','2026-03-04 11:00:00+00','2026-03-04 11:10:00+00'),
('30000000-0000-0000-0000-000000000015','10000000-0000-0000-0000-000000000009','ClinicalNote','/documents/seed/clinical_note/30000000-0000-0000-0000-000000000015.pdf','2026-03-18 11:00:00+00','00000000-0000-0000-0000-000000000002','Completed','2026-03-18 11:00:00+00','2026-03-18 12:00:00+00'),
-- ImagingReport (16-20)
('30000000-0000-0000-0000-000000000016','10000000-0000-0000-0000-000000000004','ImagingReport','/documents/seed/imaging_report/30000000-0000-0000-0000-000000000016.pdf','2026-02-11 11:00:00+00','00000000-0000-0000-0000-000000000003','Completed','2026-02-11 11:00:00+00','2026-02-11 12:30:00+00'),
('30000000-0000-0000-0000-000000000017','10000000-0000-0000-0000-000000000006','ImagingReport','/documents/seed/imaging_report/30000000-0000-0000-0000-000000000017.pdf','2026-02-25 11:00:00+00','00000000-0000-0000-0000-000000000002','Completed','2026-02-25 11:00:00+00','2026-02-25 12:30:00+00'),
('30000000-0000-0000-0000-000000000018','10000000-0000-0000-0000-000000000008','ImagingReport','/documents/seed/imaging_report/30000000-0000-0000-0000-000000000018.pdf','2026-03-11 11:00:00+00','00000000-0000-0000-0000-000000000003','Completed','2026-03-11 11:00:00+00','2026-03-11 12:30:00+00'),
('30000000-0000-0000-0000-000000000019','10000000-0000-0000-0000-000000000001','ImagingReport','/documents/seed/imaging_report/30000000-0000-0000-0000-000000000019.pdf','2026-03-25 10:00:00+00','00000000-0000-0000-0000-000000000002','Queued','2026-03-25 10:00:00+00','2026-03-25 10:00:00+00'),
('30000000-0000-0000-0000-000000000020','10000000-0000-0000-0000-000000000009','ImagingReport','/documents/seed/imaging_report/30000000-0000-0000-0000-000000000020.pdf','2026-03-18 12:00:00+00','00000000-0000-0000-0000-000000000003','Completed','2026-03-18 12:00:00+00','2026-03-18 13:30:00+00');

-- ---------------------------------------------------------------------------
-- SECTION 6 — Extracted Data (30 records)
-- ---------------------------------------------------------------------------
-- UUIDs: 40000000-0000-0000-0000-0000000000XX
-- Linked to clinical documents 01-15 (2 per document for docs 01-10, 1 each for 11-15)
-- DataContent JSONB uses ExtractedDataContent property names (PascalCase — EF Core default)
-- 10 FlaggedForReview, 15 VerifiedByUserId set

INSERT INTO extracted_data (
    "Id", "DocumentId", "DataType", "ConfidenceScore",
    "SourceAttribution", "FlaggedForReview", "VerifiedByUserId",
    "DataContent", "CreatedAt", "UpdatedAt"
) VALUES
-- Doc 01 (LabResult, patient 01)
('40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','Diagnosis',0.94,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"Hyperlipidemia confirmed","NormalizedValue":"E78.5","Unit":null,"SourceSnippet":"Cholesterol panel indicates hyperlipidemia","Metadata":{"icd_code":"E78.5","system":"ICD-10"}}','2026-01-16 11:30:00+00','2026-01-17 09:00:00+00'),
('40000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000001','Medication',0.88,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"Atorvastatin 20mg daily","NormalizedValue":"Atorvastatin","Unit":"mg","SourceSnippet":"Patient prescribed Atorvastatin 20mg once daily","Metadata":{"dosage":"20","frequency":"daily","route":"oral"}}','2026-01-16 11:30:00+00','2026-01-17 09:00:00+00'),
-- Doc 02 (LabResult, patient 02)
('40000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000002','Diagnosis',0.97,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000003','{"RawText":"Type 2 Diabetes Mellitus without complications","NormalizedValue":"E11.9","Unit":null,"SourceSnippet":"HbA1c 7.8% consistent with T2DM","Metadata":{"icd_code":"E11.9","system":"ICD-10"}}','2026-01-23 11:30:00+00','2026-01-24 09:00:00+00'),
('40000000-0000-0000-0000-000000000004','30000000-0000-0000-0000-000000000002','Allergy',0.71,'NLP-Extractor v2.1',true,NULL,'{"RawText":"Penicillin allergy noted","NormalizedValue":"Penicillin","Unit":null,"SourceSnippet":"Patient reports penicillin allergy — hives","Metadata":{"severity":"moderate","reaction_type":"hives"}}','2026-01-23 11:30:00+00','2026-01-24 09:00:00+00'),
-- Doc 03 (LabResult, patient 03)
('40000000-0000-0000-0000-000000000005','30000000-0000-0000-0000-000000000003','Diagnosis',0.91,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"Iron deficiency anemia","NormalizedValue":"D50.9","Unit":null,"SourceSnippet":"Ferritin 8 ng/mL, Hgb 9.2 g/dL — iron deficiency anemia","Metadata":{"icd_code":"D50.9","system":"ICD-10"}}','2026-02-04 12:00:00+00','2026-02-05 09:00:00+00'),
('40000000-0000-0000-0000-000000000006','30000000-0000-0000-0000-000000000003','Procedure',0.85,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"CBC with differential performed","NormalizedValue":"85025","Unit":null,"SourceSnippet":"Complete blood count with differential ordered","Metadata":{"cpt_code":"85025","system":"CPT"}}','2026-02-04 12:00:00+00','2026-02-05 09:00:00+00'),
-- Doc 04 (LabResult, patient 04) — Processing, so no verified user
('40000000-0000-0000-0000-000000000007','30000000-0000-0000-0000-000000000004','Diagnosis',0.67,'NLP-Extractor v2.1',true,NULL,'{"RawText":"Possible hypertensive emergency","NormalizedValue":"I16.9","Unit":null,"SourceSnippet":"BP 195/110 mmHg on repeat measurement","Metadata":{"icd_code":"I16.9","system":"ICD-10"}}','2026-02-11 11:00:00+00','2026-02-11 11:00:00+00'),
-- Doc 05 (LabResult, patient 05)
('40000000-0000-0000-0000-000000000008','30000000-0000-0000-0000-000000000005','Medication',0.93,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000003','{"RawText":"Metformin 1000mg twice daily","NormalizedValue":"Metformin","Unit":"mg","SourceSnippet":"Continue Metformin 1000mg BID for glycaemic control","Metadata":{"dosage":"1000","frequency":"BID","route":"oral"}}','2026-02-18 11:30:00+00','2026-02-19 09:00:00+00'),
('40000000-0000-0000-0000-000000000009','30000000-0000-0000-0000-000000000005','Allergy',0.78,'NLP-Extractor v2.1',true,NULL,'{"RawText":"Sulfa drug allergy","NormalizedValue":"Sulfonamide","Unit":null,"SourceSnippet":"Patient allergic to sulfonamide antibiotics","Metadata":{"severity":"severe","reaction_type":"anaphylaxis"}}','2026-02-18 11:30:00+00','2026-02-19 09:00:00+00'),
-- Doc 06 (Prescription, patient 01)
('40000000-0000-0000-0000-000000000010','30000000-0000-0000-0000-000000000006','Medication',0.98,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"Lisinopril 10mg once daily","NormalizedValue":"Lisinopril","Unit":"mg","SourceSnippet":"Rx: Lisinopril 10mg PO QD for hypertension management","Metadata":{"dosage":"10","frequency":"QD","route":"oral","indication":"hypertension"}}','2026-01-16 12:00:00+00','2026-01-17 09:00:00+00'),
-- Doc 07 (Prescription, patient 06)
('40000000-0000-0000-0000-000000000011','30000000-0000-0000-0000-000000000007','Medication',0.96,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000003','{"RawText":"Amlodipine 5mg once daily","NormalizedValue":"Amlodipine","Unit":"mg","SourceSnippet":"Rx: Amlodipine 5mg PO QD — calcium channel blocker","Metadata":{"dosage":"5","frequency":"QD","route":"oral","indication":"hypertension"}}','2026-02-25 11:30:00+00','2026-02-26 09:00:00+00'),
-- Doc 08 (Prescription, patient 07)
('40000000-0000-0000-0000-000000000012','30000000-0000-0000-0000-000000000008','Medication',0.92,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"Sertraline 50mg once daily","NormalizedValue":"Sertraline","Unit":"mg","SourceSnippet":"Rx: Sertraline 50mg PO QD for depression","Metadata":{"dosage":"50","frequency":"QD","route":"oral","indication":"depression"}}','2026-03-04 11:30:00+00','2026-03-05 09:00:00+00'),
-- Doc 09 (Prescription, patient 08) — Processing
('40000000-0000-0000-0000-000000000013','30000000-0000-0000-0000-000000000009','Medication',0.65,'NLP-Extractor v2.1',true,NULL,'{"RawText":"Possible duplicate medication detected","NormalizedValue":"Warfarin","Unit":"mg","SourceSnippet":"Warfarin 5mg — check interaction with existing Rx","Metadata":{"dosage":"5","frequency":"QD","route":"oral","flag":"potential_duplicate"}}','2026-03-11 11:00:00+00','2026-03-11 11:00:00+00'),
-- Doc 10 (Prescription, patient 09)
('40000000-0000-0000-0000-000000000014','30000000-0000-0000-0000-000000000010','Medication',0.95,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000003','{"RawText":"Omeprazole 20mg once daily before meals","NormalizedValue":"Omeprazole","Unit":"mg","SourceSnippet":"Rx: Omeprazole 20mg PO QD AC for GERD","Metadata":{"dosage":"20","frequency":"QD","route":"oral","indication":"GERD"}}','2026-03-18 11:30:00+00','2026-03-19 09:00:00+00'),
-- Doc 11 (ClinicalNote, patient 02)
('40000000-0000-0000-0000-000000000015','30000000-0000-0000-0000-000000000011','Diagnosis',0.89,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"Essential hypertension, controlled","NormalizedValue":"I10","Unit":null,"SourceSnippet":"BP 128/82 — hypertension well-controlled on current regimen","Metadata":{"icd_code":"I10","system":"ICD-10"}}','2026-01-23 12:30:00+00','2026-01-24 09:00:00+00'),
-- Doc 12 (ClinicalNote, patient 03)
('40000000-0000-0000-0000-000000000016','30000000-0000-0000-0000-000000000012','Procedure',0.82,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000003','{"RawText":"Office visit established patient","NormalizedValue":"99213","Unit":null,"SourceSnippet":"Established patient office visit, moderate complexity","Metadata":{"cpt_code":"99213","system":"CPT","complexity":"moderate"}}','2026-02-04 12:30:00+00','2026-02-05 09:00:00+00'),
-- Doc 13 (ClinicalNote, patient 05)
('40000000-0000-0000-0000-000000000017','30000000-0000-0000-0000-000000000013','Diagnosis',0.76,'NLP-Extractor v2.1',true,NULL,'{"RawText":"Type 2 diabetes with kidney complications","NormalizedValue":"E11.65","Unit":null,"SourceSnippet":"eGFR 52 — diabetic nephropathy stage 3","Metadata":{"icd_code":"E11.65","system":"ICD-10"}}','2026-02-18 12:30:00+00','2026-02-19 09:00:00+00'),
-- Doc 14 (ClinicalNote, patient 07) — Failed
('40000000-0000-0000-0000-000000000018','30000000-0000-0000-0000-000000000014','Diagnosis',0.45,'NLP-Extractor v2.1',true,NULL,'{"RawText":"Extraction failed — unreadable scan","NormalizedValue":null,"Unit":null,"SourceSnippet":null,"Metadata":{"error":"low_confidence","scan_quality":"poor"}}','2026-03-04 12:00:00+00','2026-03-04 12:00:00+00'),
-- Doc 15 (ClinicalNote, patient 09)
('40000000-0000-0000-0000-000000000019','30000000-0000-0000-0000-000000000015','Allergy',0.90,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"NKDA — no known drug allergies","NormalizedValue":"NKDA","Unit":null,"SourceSnippet":"Patient reports no known drug allergies","Metadata":{"status":"confirmed_nkda"}}','2026-03-18 12:30:00+00','2026-03-19 09:00:00+00'),
-- Doc 16 (ImagingReport, patient 04)
('40000000-0000-0000-0000-000000000020','30000000-0000-0000-0000-000000000016','Diagnosis',0.93,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000003','{"RawText":"Chest X-ray: mild cardiomegaly","NormalizedValue":"I51.7","Unit":null,"SourceSnippet":"PA chest radiograph shows mild cardiac enlargement","Metadata":{"icd_code":"I51.7","system":"ICD-10","modality":"XR"}}','2026-02-11 13:00:00+00','2026-02-12 09:00:00+00'),
-- Doc 17 (ImagingReport, patient 06)
('40000000-0000-0000-0000-000000000021','30000000-0000-0000-0000-000000000017','Diagnosis',0.87,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"CT abdomen: no acute pathology","NormalizedValue":"Z09","Unit":null,"SourceSnippet":"Abdominal CT with contrast — no acute intraabdominal pathology","Metadata":{"icd_code":"Z09","system":"ICD-10","modality":"CT"}}','2026-02-25 13:00:00+00','2026-02-26 09:00:00+00'),
-- Doc 18 (ImagingReport, patient 08)
('40000000-0000-0000-0000-000000000022','30000000-0000-0000-0000-000000000018','Procedure',0.91,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000003','{"RawText":"MRI knee without contrast","NormalizedValue":"73721","Unit":null,"SourceSnippet":"MRI right knee without contrast for pain evaluation","Metadata":{"cpt_code":"73721","system":"CPT","modality":"MRI","laterality":"right"}}','2026-03-11 13:00:00+00','2026-03-12 09:00:00+00'),
-- Doc 19 (ImagingReport, patient 01) — Queued
('40000000-0000-0000-0000-000000000023','30000000-0000-0000-0000-000000000019','Procedure',0.72,'NLP-Extractor v2.1',true,NULL,'{"RawText":"Echocardiogram — pending detailed review","NormalizedValue":"93306","Unit":null,"SourceSnippet":"Transthoracic echocardiogram ordered — results pending","Metadata":{"cpt_code":"93306","system":"CPT","status":"queued"}}','2026-03-25 10:30:00+00','2026-03-25 10:30:00+00'),
-- Doc 20 (ImagingReport, patient 09)
('40000000-0000-0000-0000-000000000024','30000000-0000-0000-0000-000000000020','Diagnosis',0.95,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"Osteoarthritis of right hip, severe","NormalizedValue":"M16.11","Unit":null,"SourceSnippet":"X-ray right hip: severe osteoarthritis with joint space narrowing","Metadata":{"icd_code":"M16.11","system":"ICD-10","modality":"XR"}}','2026-03-18 14:00:00+00','2026-03-19 09:00:00+00'),
-- Additional extracted data to reach 30 total (docs 01 and 02 get a 3rd)
('40000000-0000-0000-0000-000000000025','30000000-0000-0000-0000-000000000001','Procedure',0.83,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"Lipid panel comprehensive","NormalizedValue":"80061","Unit":null,"SourceSnippet":"Comprehensive lipid panel with risk stratification","Metadata":{"cpt_code":"80061","system":"CPT"}}','2026-01-16 12:30:00+00','2026-01-17 09:00:00+00'),
('40000000-0000-0000-0000-000000000026','30000000-0000-0000-0000-000000000002','Procedure',0.79,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000003','{"RawText":"HbA1c blood test","NormalizedValue":"83036","Unit":"%","SourceSnippet":"Haemoglobin A1c 7.8% — glycated haemoglobin","Metadata":{"cpt_code":"83036","system":"CPT","value":"7.8"}}','2026-01-23 12:30:00+00','2026-01-24 09:00:00+00'),
('40000000-0000-0000-0000-000000000027','30000000-0000-0000-0000-000000000005','Diagnosis',0.84,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000003','{"RawText":"Chronic kidney disease stage 2","NormalizedValue":"N18.2","Unit":null,"SourceSnippet":"eGFR 68 mL/min — CKD stage 2","Metadata":{"icd_code":"N18.2","system":"ICD-10"}}','2026-02-18 12:30:00+00','2026-02-19 09:00:00+00'),
('40000000-0000-0000-0000-000000000028','30000000-0000-0000-0000-000000000010','Diagnosis',0.90,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"Gastroesophageal reflux disease","NormalizedValue":"K21.0","Unit":null,"SourceSnippet":"GERD with oesophagitis — on PPI therapy","Metadata":{"icd_code":"K21.0","system":"ICD-10"}}','2026-03-18 12:30:00+00','2026-03-19 09:00:00+00'),
('40000000-0000-0000-0000-000000000029','30000000-0000-0000-0000-000000000011','Medication',0.86,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000003','{"RawText":"Losartan 50mg once daily","NormalizedValue":"Losartan","Unit":"mg","SourceSnippet":"Losartan 50mg QD for hypertension and renal protection","Metadata":{"dosage":"50","frequency":"QD","route":"oral"}}','2026-01-23 13:00:00+00','2026-01-24 09:00:00+00'),
('40000000-0000-0000-0000-000000000030','30000000-0000-0000-0000-000000000016','Procedure',0.88,'NLP-Extractor v2.1',false,'00000000-0000-0000-0000-000000000002','{"RawText":"PA and lateral chest X-ray","NormalizedValue":"71046","Unit":null,"SourceSnippet":"2-view chest radiograph PA and lateral projections","Metadata":{"cpt_code":"71046","system":"CPT","views":"2"}}','2026-02-11 14:00:00+00','2026-02-12 09:00:00+00');

-- ---------------------------------------------------------------------------
-- SECTION 7 — Medical Codes (15 records)
-- ---------------------------------------------------------------------------
-- UUIDs: 50000000-0000-0000-0000-0000000000XX
-- 10 SuggestedByAi, 8 ApprovedByUserId set
-- Mix of Icd10 and Cpt

INSERT INTO medical_codes (
    "Id", "PatientId", "CodeType", "CodeValue", "Description",
    "Justification", "SuggestedByAi", "ApprovedByUserId",
    "AiConfidenceScore", "CreatedAt", "UpdatedAt"
) VALUES
('50000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','Icd10','I10','Essential (primary) hypertension','Documented elevated BP readings on multiple visits; patient on antihypertensive therapy.',true,'00000000-0000-0000-0000-000000000002',0.97,'2026-01-16 12:00:00+00','2026-01-17 09:00:00+00'),
('50000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001','Icd10','E78.5','Hyperlipidaemia, unspecified','Elevated LDL and total cholesterol on lipid panel; prescribed Atorvastatin.',true,'00000000-0000-0000-0000-000000000003',0.94,'2026-01-16 12:00:00+00','2026-01-17 09:00:00+00'),
('50000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000002','Icd10','E11.9','Type 2 diabetes mellitus without complications','HbA1c 7.8%; on Metformin; no evidence of complications at current review.',true,'00000000-0000-0000-0000-000000000002',0.96,'2026-01-23 12:00:00+00','2026-01-24 09:00:00+00'),
('50000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000002','Icd10','I10','Essential (primary) hypertension','Comorbid hypertension alongside T2DM; controlled on Losartan.',true,'00000000-0000-0000-0000-000000000003',0.92,'2026-01-23 12:00:00+00','2026-01-24 09:00:00+00'),
('50000000-0000-0000-0000-000000000005','10000000-0000-0000-0000-000000000003','Icd10','D50.9','Iron deficiency anaemia, unspecified','Ferritin 8 ng/mL; Hgb 9.2 g/dL; consistent with iron deficiency anaemia.',true,'00000000-0000-0000-0000-000000000002',0.91,'2026-02-04 12:00:00+00','2026-02-05 09:00:00+00'),
('50000000-0000-0000-0000-000000000006','10000000-0000-0000-0000-000000000004','Icd10','I16.9','Hypertensive crisis, unspecified','BP 195/110 on repeat; urgent intervention required.',false,NULL,NULL,'2026-02-11 12:00:00+00','2026-02-11 12:00:00+00'),
('50000000-0000-0000-0000-000000000007','10000000-0000-0000-0000-000000000005','Icd10','E11.65','Type 2 diabetes mellitus with hyperglycaemia','eGFR 52 indicating CKD with diabetic nephropathy component.',true,NULL,0.76,'2026-02-18 12:00:00+00','2026-02-18 12:00:00+00'),
('50000000-0000-0000-0000-000000000008','10000000-0000-0000-0000-000000000005','Icd10','N18.2','Chronic kidney disease, stage 2','eGFR 68 mL/min/1.73m²; mildly reduced kidney function.',true,'00000000-0000-0000-0000-000000000002',0.84,'2026-02-18 12:00:00+00','2026-02-19 09:00:00+00'),
('50000000-0000-0000-0000-000000000009','10000000-0000-0000-0000-000000000006','Icd10','I10','Essential (primary) hypertension','Longstanding hypertension managed with Amlodipine; BP well controlled.',false,'00000000-0000-0000-0000-000000000003',NULL,'2026-02-25 12:00:00+00','2026-02-26 09:00:00+00'),
('50000000-0000-0000-0000-000000000010','10000000-0000-0000-0000-000000000007','Icd10','F32.1','Major depressive disorder, single episode, moderate','PHQ-9 score 14; on Sertraline 50mg; referred for CBT.',true,'00000000-0000-0000-0000-000000000002',0.89,'2026-03-04 12:00:00+00','2026-03-05 09:00:00+00'),
('50000000-0000-0000-0000-000000000011','10000000-0000-0000-0000-000000000008','Icd10','M17.11','Primary osteoarthritis, right knee','Radiographic evidence of right knee OA; progressive pain limiting mobility.',true,'00000000-0000-0000-0000-000000000003',0.93,'2026-03-11 12:00:00+00','2026-03-12 09:00:00+00'),
('50000000-0000-0000-0000-000000000012','10000000-0000-0000-0000-000000000009','Icd10','K21.0','Gastro-oesophageal reflux disease with oesophagitis','Symptom-confirmed GERD; responsive to PPI; endoscopy findings pending.',true,'00000000-0000-0000-0000-000000000002',0.90,'2026-03-18 12:00:00+00','2026-03-19 09:00:00+00'),
('50000000-0000-0000-0000-000000000013','10000000-0000-0000-0000-000000000001','Cpt','99213','Office or other outpatient visit, established patient — low complexity','Established patient follow-up for hypertension and hyperlipidaemia management.',false,'00000000-0000-0000-0000-000000000003',NULL,'2026-01-16 12:00:00+00','2026-01-17 09:00:00+00'),
('50000000-0000-0000-0000-000000000014','10000000-0000-0000-0000-000000000004','Cpt','71046','Radiologic examination, chest; 2 views','Chest X-ray for cardiac assessment — mild cardiomegaly noted.',true,'00000000-0000-0000-0000-000000000002',0.88,'2026-02-11 12:00:00+00','2026-02-12 09:00:00+00'),
('50000000-0000-0000-0000-000000000015','10000000-0000-0000-0000-000000000008','Cpt','73721','MRI, joint of lower extremity; without contrast','MRI right knee for OA severity assessment and surgical planning.',true,NULL,0.91,'2026-03-11 12:00:00+00','2026-03-11 12:00:00+00');

-- ---------------------------------------------------------------------------
-- SECTION 8 — Intake Data (10 records)
-- ---------------------------------------------------------------------------
-- UUIDs: 60000000-0000-0000-0000-0000000000XX
-- Mix of AiConversational and ManualForm methods
-- JSONB property names match C# OwnedType property names (PascalCase)

INSERT INTO intake_data (
    "Id", "PatientId", "IntakeMethod", "CompletedAt",
    "MandatoryFields", "OptionalFields", "InsuranceInfo",
    "CreatedAt", "UpdatedAt"
) VALUES
(
    '60000000-0000-0000-0000-000000000001',
    '10000000-0000-0000-0000-000000000001',
    'AiConversational',
    '2026-01-14 20:30:00+00',
    '{"ChiefComplaint":"Persistent headaches and dizziness for 2 weeks","Allergies":"NKDA","CurrentMedications":["Lisinopril 10mg daily","Atorvastatin 20mg daily"],"MedicalHistory":"Hypertension diagnosed 2020; hyperlipidaemia diagnosed 2021"}',
    '{"FamilyHistory":"Father — hypertension; Mother — type 2 diabetes","SocialHistory":"Non-smoker; occasional alcohol; sedentary desk job","ReviewOfSystems":"Headaches, dizziness, no chest pain","AdditionalNotes":null}',
    '{"Provider":"BlueCross BlueShield","PolicyNumber":"BCB-1001-HART","GroupNumber":"GRP-5501","PrimaryInsuredName":"Eleanor Hartley","PolicyExpiryDate":"2027-01-01"}',
    '2026-01-14 20:00:00+00', '2026-01-14 20:30:00+00'
),
(
    '60000000-0000-0000-0000-000000000002',
    '10000000-0000-0000-0000-000000000002',
    'ManualForm',
    '2026-01-21 14:15:00+00',
    '{"ChiefComplaint":"Fatigue, frequent urination, blurred vision","Allergies":"Penicillin — hives","CurrentMedications":["Metformin 1000mg BID","Losartan 50mg daily"],"MedicalHistory":"Type 2 diabetes diagnosed 2018; hypertension diagnosed 2019"}',
    '{"FamilyHistory":"Both parents — type 2 diabetes","SocialHistory":"Former smoker — quit 2015; no alcohol","ReviewOfSystems":"Fatigue, polyuria, polydipsia, blurred vision","AdditionalNotes":"Patient requests diabetic dietitian referral"}',
    '{"Provider":"Aetna","PolicyNumber":"AET-2002-THOM","GroupNumber":"GRP-7702","PrimaryInsuredName":"Marcus Thompson","PolicyExpiryDate":"2026-12-31"}',
    '2026-01-21 14:00:00+00', '2026-01-21 14:15:00+00'
),
(
    '60000000-0000-0000-0000-000000000003',
    '10000000-0000-0000-0000-000000000003',
    'AiConversational',
    '2026-02-02 19:45:00+00',
    '{"ChiefComplaint":"Tiredness, shortness of breath on exertion, pale skin","Allergies":"Sulfonamides — anaphylaxis","CurrentMedications":["Ferrous sulfate 325mg daily"],"MedicalHistory":"Iron deficiency anaemia diagnosed January 2026"}',
    '{"FamilyHistory":"No significant family history","SocialHistory":"Non-smoker; moderate alcohol; vegetarian diet","ReviewOfSystems":"Fatigue, dyspnoea on exertion, pallor, palpitations","AdditionalNotes":null}',
    '{"Provider":"United Healthcare","PolicyNumber":"UHC-3003-PATE","GroupNumber":"GRP-9903","PrimaryInsuredName":"Priya Patel","PolicyExpiryDate":"2027-06-30"}',
    '2026-02-02 19:30:00+00', '2026-02-02 19:45:00+00'
),
(
    '60000000-0000-0000-0000-000000000004',
    '10000000-0000-0000-0000-000000000004',
    'ManualForm',
    '2026-02-09 10:30:00+00',
    '{"ChiefComplaint":"Severe headache, chest tightness, visual changes","Allergies":"NKDA","CurrentMedications":["Amlodipine 5mg daily","Hydrochlorothiazide 25mg daily"],"MedicalHistory":"Hypertension for 20 years; previous MI 2015; pacemaker 2018"}',
    '{"FamilyHistory":"Father — MI age 58; Brother — hypertension","SocialHistory":"Non-smoker; no alcohol; retired","ReviewOfSystems":"Severe headache, chest tightness, visual blurring, no syncope","AdditionalNotes":"Requires wheelchair access; hard of hearing — speak clearly"}',
    '{"Provider":"Medicare","PolicyNumber":"MCR-4004-OKAF","GroupNumber":null,"PrimaryInsuredName":"James Okafor","PolicyExpiryDate":"2099-12-31"}',
    '2026-02-09 10:00:00+00', '2026-02-09 10:30:00+00'
),
(
    '60000000-0000-0000-0000-000000000005',
    '10000000-0000-0000-0000-000000000005',
    'AiConversational',
    '2026-02-16 18:00:00+00',
    '{"ChiefComplaint":"Increased thirst, swollen ankles, decreased urine output","Allergies":"Sulfonamides — anaphylaxis","CurrentMedications":["Metformin 1000mg BID","Insulin glargine 20 units nightly"],"MedicalHistory":"Type 2 diabetes 2015; hypertension 2016; CKD stage 2 detected 2024"}',
    '{"FamilyHistory":"Mother — T2DM; Father — hypertension and CVD","SocialHistory":"Non-smoker; no alcohol; low-sodium diet","ReviewOfSystems":"Oedema bilateral, oliguria, polydipsia, no chest pain","AdditionalNotes":"Patient concerned about kidney function progression"}',
    '{"Provider":"Cigna","PolicyNumber":"CIG-5005-SANT","GroupNumber":"GRP-1105","PrimaryInsuredName":"Maria Santos","PolicyExpiryDate":"2027-03-31"}',
    '2026-02-16 17:45:00+00', '2026-02-16 18:00:00+00'
),
(
    '60000000-0000-0000-0000-000000000006',
    '10000000-0000-0000-0000-000000000006',
    'ManualForm',
    '2026-02-23 09:15:00+00',
    '{"ChiefComplaint":"Dizziness, palpitations, mild chest discomfort","Allergies":"NKDA","CurrentMedications":["Amlodipine 5mg daily","Aspirin 81mg daily"],"MedicalHistory":"Hypertension 20 years; mild cardiomegaly detected 2025"}',
    '{"FamilyHistory":"Father — hypertension and heart failure; Mother — stroke","SocialHistory":"Former smoker — quit 1990; no alcohol; retired","ReviewOfSystems":"Dizziness on standing, palpitations, exertional dyspnoea","AdditionalNotes":"Hearing impaired — written materials preferred"}',
    '{"Provider":"Medicare","PolicyNumber":"MCR-6006-CHEN","GroupNumber":null,"PrimaryInsuredName":"David Chen","PolicyExpiryDate":"2099-12-31"}',
    '2026-02-23 09:00:00+00', '2026-02-23 09:15:00+00'
),
(
    '60000000-0000-0000-0000-000000000007',
    '10000000-0000-0000-0000-000000000007',
    'AiConversational',
    '2026-03-02 21:00:00+00',
    '{"ChiefComplaint":"Low mood, sleep disturbance, loss of interest in activities","Allergies":"NKDA","CurrentMedications":["Sertraline 50mg daily","Melatonin 5mg at bedtime"],"MedicalHistory":"Major depressive disorder diagnosed 2025; no prior hospitalisations"}',
    '{"FamilyHistory":"Mother — depression; Maternal aunt — bipolar disorder","SocialHistory":"Non-smoker; no alcohol; remote software developer; limited social contact","ReviewOfSystems":"Low mood, insomnia, anhedonia, poor concentration, no suicidal ideation","AdditionalNotes":"Patient prefers morning appointments; anxious about large waiting rooms"}',
    '{"Provider":"Blue Cross Blue Shield","PolicyNumber":"BCB-7007-WALS","GroupNumber":"GRP-2207","PrimaryInsuredName":"Jennifer Walsh","PolicyExpiryDate":"2027-09-30"}',
    '2026-03-02 20:45:00+00', '2026-03-02 21:00:00+00'
),
(
    '60000000-0000-0000-0000-000000000008',
    '10000000-0000-0000-0000-000000000008',
    'ManualForm',
    '2026-03-09 13:00:00+00',
    '{"ChiefComplaint":"Right knee pain worsening over 6 months, morning stiffness","Allergies":"NKDA","CurrentMedications":["Naproxen 500mg BID PRN","Omeprazole 20mg daily"],"MedicalHistory":"Right knee OA diagnosed 2022; on physiotherapy; no prior surgeries"}',
    '{"FamilyHistory":"Father — OA knees and hips; Mother — rheumatoid arthritis","SocialHistory":"Non-smoker; moderate alcohol; retired nurse","ReviewOfSystems":"Knee pain, stiffness, crepitus, reduced ROM, no fever","AdditionalNotes":"Interpreter needed — Arabic; requests male physician if available"}',
    '{"Provider":"Aetna","PolicyNumber":"AET-8008-HASS","GroupNumber":"GRP-3308","PrimaryInsuredName":"Ahmed Hassan","PolicyExpiryDate":"2026-12-31"}',
    '2026-03-09 12:45:00+00', '2026-03-09 13:00:00+00'
),
(
    '60000000-0000-0000-0000-000000000009',
    '10000000-0000-0000-0000-000000000009',
    'AiConversational',
    '2026-03-16 17:30:00+00',
    '{"ChiefComplaint":"Heartburn, regurgitation, epigastric pain after meals","Allergies":"NKDA","CurrentMedications":["Omeprazole 20mg daily AC"],"MedicalHistory":"GERD with oesophagitis confirmed on endoscopy 2024; hip OA 2025"}',
    '{"FamilyHistory":"Mother — GERD; No family history of GI cancer","SocialHistory":"Non-smoker; no alcohol; high-stress office job","ReviewOfSystems":"Heartburn, regurgitation, epigastric pain, no dysphagia, no blood","AdditionalNotes":null}',
    '{"Provider":"United Healthcare","PolicyNumber":"UHC-9009-BREW","GroupNumber":"GRP-4409","PrimaryInsuredName":"Susan Brewer","PolicyExpiryDate":"2027-01-31"}',
    '2026-03-16 17:15:00+00', '2026-03-16 17:30:00+00'
),
(
    '60000000-0000-0000-0000-000000000010',
    '10000000-0000-0000-0000-000000000001',
    'ManualForm',
    '2026-03-23 11:30:00+00',
    '{"ChiefComplaint":"Routine follow-up — hypertension and hyperlipidaemia management","Allergies":"NKDA","CurrentMedications":["Lisinopril 10mg daily","Atorvastatin 20mg daily"],"MedicalHistory":"Hypertension 2020; hyperlipidaemia 2021; echocardiogram pending"}',
    '{"FamilyHistory":"Father — hypertension; Mother — type 2 diabetes","SocialHistory":"Non-smoker; occasional alcohol; started mild exercise programme","ReviewOfSystems":"No chest pain, no dyspnoea, mild residual headaches","AdditionalNotes":"Patient requests referral for cardiac assessment"}',
    '{"Provider":"BlueCross BlueShield","PolicyNumber":"BCB-1001-HART","GroupNumber":"GRP-5501","PrimaryInsuredName":"Eleanor Hartley","PolicyExpiryDate":"2027-01-01"}',
    '2026-03-23 11:00:00+00', '2026-03-23 11:30:00+00'
);

-- ---------------------------------------------------------------------------
-- SECTION 9 — Audit Logs (20 records)
-- ---------------------------------------------------------------------------
-- UUIDs: 70000000-0000-0000-0000-0000000000XX
-- AuditLog uses LogId (not Id) as PK; no CreatedAt/UpdatedAt (append-only)

INSERT INTO audit_logs (
    "LogId", "UserId", "Action", "ResourceType", "ResourceId",
    "Timestamp", "IpAddress", "UserAgent"
) VALUES
('70000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002','Login','Session',NULL,'2026-01-16 08:55:00+00','192.168.1.10','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000002','DataAccess','Patient','10000000-0000-0000-0000-000000000001','2026-01-16 09:02:00+00','192.168.1.10','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000002','DataModify','Appointment','20000000-0000-0000-0000-000000000001','2026-01-15 09:31:00+00','192.168.1.10','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000002','DataAccess','ClinicalDocument','30000000-0000-0000-0000-000000000001','2026-01-16 10:05:00+00','192.168.1.10','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000005','00000000-0000-0000-0000-000000000002','Logout','Session',NULL,'2026-01-16 17:00:00+00','192.168.1.10','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000006','00000000-0000-0000-0000-000000000003','Login','Session',NULL,'2026-01-23 08:50:00+00','192.168.1.11','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000007','00000000-0000-0000-0000-000000000003','DataAccess','Patient','10000000-0000-0000-0000-000000000002','2026-01-23 09:10:00+00','192.168.1.11','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000008','00000000-0000-0000-0000-000000000003','DataModify','ClinicalDocument','30000000-0000-0000-0000-000000000002','2026-01-23 11:05:00+00','192.168.1.11','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000009','00000000-0000-0000-0000-000000000003','DataAccess','ExtractedData','40000000-0000-0000-0000-000000000003','2026-01-23 11:35:00+00','192.168.1.11','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000010','00000000-0000-0000-0000-000000000001','Login','Session',NULL,'2026-02-01 09:00:00+00','10.0.0.1','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Admin/1.0'),
('70000000-0000-0000-0000-000000000011','00000000-0000-0000-0000-000000000001','DataAccess','Patient','10000000-0000-0000-0000-000000000004','2026-02-11 12:05:00+00','10.0.0.1','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Admin/1.0'),
('70000000-0000-0000-0000-000000000012','00000000-0000-0000-0000-000000000001','DataModify','MedicalCode','50000000-0000-0000-0000-000000000006','2026-02-11 12:30:00+00','10.0.0.1','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Admin/1.0'),
('70000000-0000-0000-0000-000000000013','00000000-0000-0000-0000-000000000002','Login','Session',NULL,'2026-02-18 08:45:00+00','192.168.1.10','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000014','00000000-0000-0000-0000-000000000002','DataAccess','Patient','10000000-0000-0000-0000-000000000005','2026-02-18 09:00:00+00','192.168.1.10','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000015','00000000-0000-0000-0000-000000000002','DataModify','Appointment','20000000-0000-0000-0000-000000000005','2026-02-17 10:02:00+00','192.168.1.10','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000016','00000000-0000-0000-0000-000000000003','DataAccess','ClinicalDocument','30000000-0000-0000-0000-000000000016','2026-02-11 11:05:00+00','192.168.1.11','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000017','00000000-0000-0000-0000-000000000003','DataModify','ExtractedData','40000000-0000-0000-0000-000000000020','2026-02-11 13:05:00+00','192.168.1.11','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000018','00000000-0000-0000-0000-000000000002','DataDelete','Appointment','20000000-0000-0000-0000-000000000016','2026-02-10 16:01:00+00','192.168.1.10','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000019','00000000-0000-0000-0000-000000000001','DataModify','Patient','10000000-0000-0000-0000-000000000010','2026-03-01 12:01:00+00','10.0.0.1','Mozilla/5.0 (Windows NT 10.0; Win64; x64) UPACIP-Admin/1.0'),
('70000000-0000-0000-0000-000000000020','00000000-0000-0000-0000-000000000003','Logout','Session',NULL,'2026-03-11 17:00:00+00','192.168.1.11','Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0');

-- ---------------------------------------------------------------------------
-- SECTION 10 — Queue Entries (10 records)
-- ---------------------------------------------------------------------------
-- UUIDs: 80000000-0000-0000-0000-0000000000XX
-- Linked to this-week appointments (21-30)
-- Mix of Waiting, InVisit, Completed statuses

INSERT INTO queue_entries (
    "Id", "AppointmentId", "ArrivalTimestamp", "WaitTimeMinutes",
    "Priority", "Status", "CreatedAt", "UpdatedAt"
) VALUES
('80000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000021','2026-04-19 08:50:00+00',10,'Normal','InVisit','2026-04-19 08:50:00+00','2026-04-19 09:05:00+00'),
('80000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000022','2026-04-19 10:45:00+00',15,'Normal','Waiting','2026-04-19 10:45:00+00','2026-04-19 10:45:00+00'),
('80000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000023','2026-04-20 09:52:00+00',8,'Normal','Waiting','2026-04-20 09:52:00+00','2026-04-20 09:52:00+00'),
('80000000-0000-0000-0000-000000000004','20000000-0000-0000-0000-000000000024','2026-04-20 13:50:00+00',10,'Urgent','Waiting','2026-04-20 13:50:00+00','2026-04-20 13:50:00+00'),
('80000000-0000-0000-0000-000000000005','20000000-0000-0000-0000-000000000025','2026-04-21 09:22:00+00',8,'Normal','Waiting','2026-04-21 09:22:00+00','2026-04-21 09:22:00+00'),
('80000000-0000-0000-0000-000000000006','20000000-0000-0000-0000-000000000026','2026-04-21 11:20:00+00',10,'Urgent','Waiting','2026-04-21 11:20:00+00','2026-04-21 11:20:00+00'),
('80000000-0000-0000-0000-000000000007','20000000-0000-0000-0000-000000000027','2026-04-22 12:50:00+00',10,'Normal','Waiting','2026-04-22 12:50:00+00','2026-04-22 12:50:00+00'),
('80000000-0000-0000-0000-000000000008','20000000-0000-0000-0000-000000000028','2026-04-22 14:50:00+00',10,'Normal','Waiting','2026-04-22 14:50:00+00','2026-04-22 14:50:00+00'),
('80000000-0000-0000-0000-000000000009','20000000-0000-0000-0000-000000000029','2026-04-23 09:52:00+00',8,'Normal','Waiting','2026-04-23 09:52:00+00','2026-04-23 09:52:00+00'),
('80000000-0000-0000-0000-000000000010','20000000-0000-0000-0000-000000000030','2026-04-25 13:50:00+00',10,'Normal','Waiting','2026-04-25 13:50:00+00','2026-04-25 13:50:00+00');

-- ---------------------------------------------------------------------------
-- SECTION 11 — Notification Logs (25 records)
-- ---------------------------------------------------------------------------
-- UUIDs: 90000000-0000-0000-0000-0000000000XX
-- NotificationLog uses NotificationId (not Id) as PK; no UpdatedAt

INSERT INTO notification_logs (
    "NotificationId", "AppointmentId", "NotificationType", "DeliveryChannel",
    "Status", "RetryCount", "SentAt", "CreatedAt"
) VALUES
-- Appointment 01 (past, completed) — confirmation sent
('90000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','Confirmation','Email','Sent',0,'2026-01-10 10:05:00+00','2026-01-10 10:05:00+00'),
('90000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001','Reminder24h','Email','Sent',0,'2026-01-14 09:00:00+00','2026-01-14 09:00:00+00'),
-- Appointment 02
('90000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000002','Confirmation','Sms','Sent',0,'2026-01-17 10:05:00+00','2026-01-17 10:05:00+00'),
('90000000-0000-0000-0000-000000000004','20000000-0000-0000-0000-000000000002','Reminder24h','Sms','Sent',0,'2026-01-21 10:30:00+00','2026-01-21 10:30:00+00'),
-- Appointment 03 — reminder bounced, retried
('90000000-0000-0000-0000-000000000005','20000000-0000-0000-0000-000000000003','Confirmation','Email','Sent',0,'2026-01-28 09:05:00+00','2026-01-28 09:05:00+00'),
('90000000-0000-0000-0000-000000000006','20000000-0000-0000-0000-000000000003','Reminder24h','Email','Bounced',1,NULL,'2026-02-02 09:00:00+00'),
-- Appointment 05 — both channels
('90000000-0000-0000-0000-000000000007','20000000-0000-0000-0000-000000000005','Confirmation','Email','Sent',0,'2026-02-12 10:05:00+00','2026-02-12 10:05:00+00'),
('90000000-0000-0000-0000-000000000008','20000000-0000-0000-0000-000000000005','Confirmation','Sms','Sent',0,'2026-02-12 10:05:00+00','2026-02-12 10:05:00+00'),
-- Appointment 11 (no-show) — reminder2h failed
('90000000-0000-0000-0000-000000000009','20000000-0000-0000-0000-000000000011','Confirmation','Email','Sent',0,'2026-01-31 10:05:00+00','2026-01-31 10:05:00+00'),
('90000000-0000-0000-0000-000000000010','20000000-0000-0000-0000-000000000011','Reminder24h','Email','Sent',0,'2026-02-04 09:00:00+00','2026-02-04 09:00:00+00'),
('90000000-0000-0000-0000-000000000011','20000000-0000-0000-0000-000000000011','Reminder2h','Sms','Failed',3,NULL,'2026-02-05 07:00:00+00'),
-- Appointment 16 (cancelled) — slot swap notification
('90000000-0000-0000-0000-000000000012','20000000-0000-0000-0000-000000000016','Confirmation','Email','Sent',0,'2026-02-07 09:05:00+00','2026-02-07 09:05:00+00'),
('90000000-0000-0000-0000-000000000013','20000000-0000-0000-0000-000000000016','SlotSwap','Email','Sent',0,'2026-02-10 16:05:00+00','2026-02-10 16:05:00+00'),
-- Appointment 21 (this week) — confirmation sent
('90000000-0000-0000-0000-000000000014','20000000-0000-0000-0000-000000000021','Confirmation','Email','Sent',0,'2026-04-14 10:05:00+00','2026-04-14 10:05:00+00'),
('90000000-0000-0000-0000-000000000015','20000000-0000-0000-0000-000000000021','Reminder24h','Sms','Sent',0,'2026-04-18 09:00:00+00','2026-04-18 09:00:00+00'),
-- Appointment 22
('90000000-0000-0000-0000-000000000016','20000000-0000-0000-0000-000000000022','Confirmation','Email','Sent',0,'2026-04-14 11:05:00+00','2026-04-14 11:05:00+00'),
-- Appointment 25
('90000000-0000-0000-0000-000000000017','20000000-0000-0000-0000-000000000025','Confirmation','Email','Sent',0,'2026-04-16 09:05:00+00','2026-04-16 09:05:00+00'),
('90000000-0000-0000-0000-000000000018','20000000-0000-0000-0000-000000000025','Reminder24h','Sms','Sent',0,'2026-04-20 09:30:00+00','2026-04-20 09:30:00+00'),
-- Appointment 31 (future)
('90000000-0000-0000-0000-000000000019','20000000-0000-0000-0000-000000000031','Confirmation','Email','Sent',0,'2026-04-19 08:05:00+00','2026-04-19 08:05:00+00'),
-- Appointment 33 (future)
('90000000-0000-0000-0000-000000000020','20000000-0000-0000-0000-000000000033','Confirmation','Sms','Sent',0,'2026-04-19 08:05:00+00','2026-04-19 08:05:00+00'),
-- Appointment 35 (future)
('90000000-0000-0000-0000-000000000021','20000000-0000-0000-0000-000000000035','Confirmation','Email','Sent',0,'2026-04-19 08:05:00+00','2026-04-19 08:05:00+00'),
-- Appointment 46 (walk-in)
('90000000-0000-0000-0000-000000000022','20000000-0000-0000-0000-000000000046','Confirmation','Sms','Sent',0,'2026-03-05 08:16:00+00','2026-03-05 08:16:00+00'),
-- Appointment 47 (walk-in) — reminder bounced
('90000000-0000-0000-0000-000000000023','20000000-0000-0000-0000-000000000047','Confirmation','Email','Bounced',2,NULL,'2026-03-12 10:46:00+00'),
-- Appointment 39 (future) — confirmation sent
('90000000-0000-0000-0000-000000000024','20000000-0000-0000-0000-000000000039','Confirmation','Email','Sent',0,'2026-04-19 08:10:00+00','2026-04-19 08:10:00+00'),
-- Appointment 42 (future)
('90000000-0000-0000-0000-000000000025','20000000-0000-0000-0000-000000000042','Confirmation','Sms','Sent',0,'2026-04-19 08:10:00+00','2026-04-19 08:10:00+00');

-- ---------------------------------------------------------------------------
-- SECTION 13 — CPT Code Library (62 records across 5 categories)
-- ---------------------------------------------------------------------------
-- Covers the most common procedure categories used in primary/specialty care:
--   Evaluation & Management (E&M): 99201–99215 (new + established office visits)
--   Preventive Medicine:           99381–99397 (annual wellness visits)
--   Laboratory / Pathology:        80047–80076, 83036, 85025 (common lab panels)
--   Radiology:                     70553, 71046, 72148, 73721, 74177 (common imaging)
--   Surgery / Procedures:          10060, 10061, 10080, 10120, 20610, 36415 (minor surg)
--   Medicine / Vaccines:           90471–90474, 90686, 90715 (vaccine admin + flu/Td)
--   Anesthesia / Psychiatry:       99202–99205, 99212–99215 covered above
--
-- All rows use 'dev-2026.Q1' as library version for development purposes.
-- Effective date 2025-01-01; no expiration (null) = currently active.
-- Idempotent: ON CONFLICT (cpt_code) DO UPDATE so re-runs are safe.
-- ---------------------------------------------------------------------------
INSERT INTO cpt_code_library
    ("CptCodeId", "CptCode", "Description", "Category", "EffectiveDate", "ExpirationDate", "IsActive", "CreatedAt", "UpdatedAt")
VALUES
-- ── Evaluation & Management — New Patient ───────────────────────────────────
(gen_random_uuid(), '99201', 'Office or other outpatient visit, new patient — problem-focused', 'Evaluation & Management', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99202', 'Office or other outpatient visit, new patient — straightforward MDM (or <30 min)', 'Evaluation & Management', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99203', 'Office or other outpatient visit, new patient — low MDM (or 30–44 min)', 'Evaluation & Management', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99204', 'Office or other outpatient visit, new patient — moderate MDM (or 45–59 min)', 'Evaluation & Management', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99205', 'Office or other outpatient visit, new patient — high MDM (or 60–74 min)', 'Evaluation & Management', '2025-01-01', NULL, TRUE, NOW(), NOW()),
-- ── Evaluation & Management — Established Patient ───────────────────────────
(gen_random_uuid(), '99211', 'Office or other outpatient visit, established patient — minimal presenting problem', 'Evaluation & Management', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99212', 'Office or other outpatient visit, established patient — straightforward MDM (or <20 min)', 'Evaluation & Management', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99213', 'Office or other outpatient visit, established patient — low MDM (or 20–29 min)', 'Evaluation & Management', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99214', 'Office or other outpatient visit, established patient — moderate MDM (or 30–39 min)', 'Evaluation & Management', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99215', 'Office or other outpatient visit, established patient — high MDM (or 40–54 min)', 'Evaluation & Management', '2025-01-01', NULL, TRUE, NOW(), NOW()),
-- ── Preventive Medicine ─────────────────────────────────────────────────────
(gen_random_uuid(), '99381', 'Preventive medicine evaluation, new patient, infant (under 1 year)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99382', 'Preventive medicine evaluation, new patient, early childhood (1–4 years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99383', 'Preventive medicine evaluation, new patient, late childhood (5–11 years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99384', 'Preventive medicine evaluation, new patient, adolescent (12–17 years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99385', 'Preventive medicine evaluation, new patient, adult (18–39 years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99386', 'Preventive medicine evaluation, new patient, adult (40–64 years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99387', 'Preventive medicine evaluation, new patient, adult (65+ years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99391', 'Preventive medicine evaluation, established patient, infant (under 1 year)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99392', 'Preventive medicine evaluation, established patient, early childhood (1–4 years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99393', 'Preventive medicine evaluation, established patient, late childhood (5–11 years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99394', 'Preventive medicine evaluation, established patient, adolescent (12–17 years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99395', 'Preventive medicine evaluation, established patient, adult (18–39 years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99396', 'Preventive medicine evaluation, established patient, adult (40–64 years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99397', 'Preventive medicine evaluation, established patient, adult (65+ years)', 'Preventive Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
-- ── Laboratory / Pathology ──────────────────────────────────────────────────
(gen_random_uuid(), '80047', 'Basic metabolic panel (calcium, total)', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '80048', 'Basic metabolic panel (calcium, ionized)', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '80053', 'Comprehensive metabolic panel', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '80061', 'Lipid panel (cholesterol, triglycerides, HDL, LDL)', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '80076', 'Hepatic function panel', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '82962', 'Glucose; blood glucose monitoring device cleared by FDA', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '83036', 'Hemoglobin A1c (glycated hemoglobin)', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '83540', 'Iron; serum', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '83970', 'Parathyroid hormone (PTH)', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '84153', 'Prostate-specific antigen (PSA), total', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '84443', 'Thyroid stimulating hormone (TSH)', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '85025', 'Complete blood count (CBC) with automated differential', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '85027', 'Complete blood count (CBC) without differential', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '86580', 'Intradermal tuberculin skin test (PPD) — placement', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '87040', 'Culture, bacterial; blood', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '87086', 'Culture, bacterial; urine, quantitative colony count', 'Laboratory', '2025-01-01', NULL, TRUE, NOW(), NOW()),
-- ── Radiology ───────────────────────────────────────────────────────────────
(gen_random_uuid(), '70553', 'MRI, brain; without contrast, followed by with contrast', 'Radiology', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '71046', 'Radiologic examination, chest; 2 views', 'Radiology', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '71048', 'Radiologic examination, chest; 4 or more views', 'Radiology', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '72148', 'MRI, spinal canal and contents, lumbar; without contrast', 'Radiology', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '73721', 'MRI, any joint of lower extremity; without contrast', 'Radiology', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '74177', 'CT, abdomen and pelvis; with contrast', 'Radiology', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '76942', 'Ultrasonic guidance for needle placement, imaging supervision and interpretation', 'Radiology', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '77067', 'Screening mammography, bilateral', 'Radiology', '2025-01-01', NULL, TRUE, NOW(), NOW()),
-- ── Surgery / Minor Procedures ──────────────────────────────────────────────
(gen_random_uuid(), '10060', 'Incision and drainage of abscess; simple or single', 'Surgery', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '10061', 'Incision and drainage of abscess; complicated or multiple', 'Surgery', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '10080', 'Incision and drainage of pilonidal cyst; simple', 'Surgery', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '10120', 'Incision and removal of foreign body, subcutaneous tissues; simple', 'Surgery', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '20610', 'Arthrocentesis, aspiration and/or injection, major joint or bursa', 'Surgery', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '29125', 'Application of short arm splint; static', 'Surgery', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '36415', 'Collection of venous blood by venipuncture', 'Surgery', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '36416', 'Collection of capillary blood specimen (e.g., finger, heel, ear stick)', 'Surgery', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '69210', 'Removal of impacted cerumen, one or both ears', 'Surgery', '2025-01-01', NULL, TRUE, NOW(), NOW()),
-- ── Medicine / Vaccines ─────────────────────────────────────────────────────
(gen_random_uuid(), '90471', 'Immunization administration (includes percutaneous, intradermal, subcutaneous, or intramuscular injection); 1st injection, any route', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '90472', 'Immunization administration; each additional injection, any route', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '90473', 'Immunization administration; intranasal or oral, 1st administration', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '90474', 'Immunization administration; intranasal or oral, each additional administration', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '90686', 'Influenza virus vaccine, quadrivalent (IIV4), split virus, preservative free; 0.5 mL intramuscular', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '90715', 'Tetanus, diphtheria toxoids (Td) adsorbed, preservative free, for use in individuals 7 years or older; 0.5 mL IM', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '93000', 'Electrocardiogram, routine ECG with at least 12 leads; with interpretation and report', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '94760', 'Noninvasive ear or pulse oximetry for oxygen saturation; single determination', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '96372', 'Therapeutic, prophylactic, or diagnostic injection; subcutaneous or intramuscular', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '97110', 'Therapeutic exercises to develop strength and endurance, range of motion; each 15 minutes', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99406', 'Smoking and tobacco use cessation counseling visit; intermediate, greater than 3 minutes, up to 10 minutes', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW()),
(gen_random_uuid(), '99407', 'Smoking and tobacco use cessation counseling visit; intensive, greater than 10 minutes', 'Medicine', '2025-01-01', NULL, TRUE, NOW(), NOW())
ON CONFLICT ("CptCode") DO UPDATE
    SET "Description" = EXCLUDED."Description",
        "Category"    = EXCLUDED."Category",
        "IsActive"    = EXCLUDED."IsActive",
        "UpdatedAt"   = NOW();

-- ---------------------------------------------------------------------------
-- SECTION 14 — CPT Bundle Rules (10 NCCI / AMA bundling examples)
-- ---------------------------------------------------------------------------
-- These rules define common NCCI (National Correct Coding Initiative) and AMA
-- guidelines where individual procedure codes should be consolidated into a
-- single bundled code.  The AI coding pipeline uses these rows to flag bundling
-- opportunities for staff review (US_048 AC-3, edge case).
--
-- Idempotent: ON CONFLICT (bundle_cpt_code, component_cpt_code) DO NOTHING.
-- ---------------------------------------------------------------------------
INSERT INTO cpt_bundle_rules
    ("BundleRuleId", "BundleCptCode", "ComponentCptCode", "BundleDescription", "IsActive", "CreatedAt")
VALUES
-- ── E&M + Venipuncture bundle ────────────────────────────────────────────────
(gen_random_uuid(), '99213', '36415',
 'NCCI edit: venipuncture (36415) is bundled into E&M level 3 (99213) when performed during the same office visit.',
 TRUE, NOW()),
(gen_random_uuid(), '99214', '36415',
 'NCCI edit: venipuncture (36415) is bundled into E&M level 4 (99214) when performed during the same office visit.',
 TRUE, NOW()),
-- ── E&M + Glucose monitoring ─────────────────────────────────────────────────
(gen_random_uuid(), '99213', '82962',
 'Glucose fingerstick (82962) is typically bundled with E&M level 3 (99213) when done as part of the same visit.',
 TRUE, NOW()),
-- ── E&M + ECG ────────────────────────────────────────────────────────────────
(gen_random_uuid(), '99215', '93000',
 'Routine ECG (93000) may be bundled with high-complexity E&M (99215) per payer global service rules.',
 TRUE, NOW()),
-- ── Influenza vaccine + administration ──────────────────────────────────────
(gen_random_uuid(), '90686', '90471',
 'Influenza vaccine (90686) and immunization administration (90471) are billed together as a standard vaccine bundle.',
 TRUE, NOW()),
-- ── Comprehensive metabolic panel + basic metabolic panel ────────────────────
(gen_random_uuid(), '80053', '80047',
 'Comprehensive metabolic panel (80053) supersedes basic metabolic panel (80047) — billing both on the same date is not allowed.',
 TRUE, NOW()),
-- ── CXR views ────────────────────────────────────────────────────────────────
(gen_random_uuid(), '71048', '71046',
 '4-view chest X-ray (71048) supersedes 2-view CXR (71046); billing both on the same date is not appropriate.',
 TRUE, NOW()),
-- ── Arthrocentesis + splinting ───────────────────────────────────────────────
(gen_random_uuid(), '20610', '29125',
 'Short arm splint application (29125) may be bundled with arthrocentesis (20610) when performed at the same encounter per payer rules.',
 TRUE, NOW()),
-- ── Wound I&D complexity ─────────────────────────────────────────────────────
(gen_random_uuid(), '10061', '10060',
 'Complicated I&D (10061) supersedes simple I&D (10060) when both are reported for the same lesion.',
 TRUE, NOW()),
-- ── Preventive visit + smoking cessation ─────────────────────────────────────
(gen_random_uuid(), '99395', '99406',
 'Intermediate smoking cessation counseling (99406) may be separately reportable from a preventive visit (99395) when performed as a distinct service exceeding the bundled E&M time.',
 FALSE, NOW())   -- inactive: payer coverage varies; disabled by default for staff review
ON CONFLICT ("BundleCptCode", "ComponentCptCode") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 15 — Code Modifiers (10 standard CMS modifiers)
-- ---------------------------------------------------------------------------
-- Deterministic UUIDs in the 00000000-0000-0000-0015-xxxxxxxxxxxx range.
INSERT INTO code_modifiers (
    "ModifierId", "ModifierCode", "ModifierDescription",
    "ApplicableCodeTypes", "DocumentationRequired", "CreatedAt"
) VALUES
('00000000-0000-0000-0015-000000000001', '25',
 'Significant, Separately Identifiable E/M Service by the Same Physician or Other Qualified Health Care Professional on the Same Day of the Procedure or Other Service',
 '["cpt"]', true, NOW()),
('00000000-0000-0000-0015-000000000002', '59',
 'Distinct Procedural Service — indicates a procedure or service was distinct or independent from other non-E/M services performed on the same day',
 '["cpt"]', true, NOW()),
('00000000-0000-0000-0015-000000000003', '51',
 'Multiple Procedures — used when multiple procedures, other than E/M services, are performed at the same session by the same provider',
 '["cpt"]', false, NOW()),
('00000000-0000-0000-0015-000000000004', '76',
 'Repeat Procedure or Service by Same Physician or Other Qualified Health Care Professional',
 '["cpt"]', true, NOW()),
('00000000-0000-0000-0015-000000000005', '77',
 'Repeat Procedure by Another Physician or Other Qualified Health Care Professional',
 '["cpt"]', true, NOW()),
('00000000-0000-0000-0015-000000000006', 'XU',
 'Unusual Non-Overlapping Service — the use of a service that is distinct because it does not overlap usual components of the main service',
 '["cpt"]', true, NOW()),
('00000000-0000-0000-0015-000000000007', 'XE',
 'Separate Encounter — a service that is distinct because it occurred during a separate encounter',
 '["cpt"]', true, NOW()),
('00000000-0000-0000-0015-000000000008', 'XP',
 'Separate Practitioner — a service that is distinct because it was performed by a different practitioner',
 '["cpt"]', true, NOW()),
('00000000-0000-0000-0015-000000000009', 'XS',
 'Separate Structure — a service that is distinct because it was performed on a separate organ/structure',
 '["cpt"]', false, NOW()),
('00000000-0000-0000-0015-000000000010', '91',
 'Repeat Clinical Diagnostic Laboratory Test — same test on same patient same day in situations where it is necessary to obtain multiple test results',
 '["cpt"]', true, NOW())
ON CONFLICT ("ModifierId") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 16 — NCCI Bundling Edits (20 common edit pairs)
-- ---------------------------------------------------------------------------
-- Column 1 is the comprehensive code; Column 2 is the component code.
-- Effective 2026-01-01; no expiration set for active edits.
INSERT INTO bundling_edits (
    "EditId", "Column1Code", "Column2Code", "EditType",
    "ModifierAllowed", "AllowedModifiers", "Source",
    "EffectiveDate", "ExpirationDate", "CreatedAt"
) VALUES
-- ── E&M + Minor Procedures ───────────────────────────────────────────────
('00000000-0000-0000-0016-000000000001', '99213', '99211',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000002', '99214', '99211',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000003', '99214', '99213',
 'ComponentPart', true, '["25"]', 'NCCI', '2026-01-01', NULL, NOW()),
-- ── Surgical Pairs ───────────────────────────────────────────────────────
('00000000-0000-0000-0016-000000000004', '27447', '27310',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000005', '27447', '27370',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000006', '43239', '43235',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000007', '45378', '45330',
 'ComponentPart', true, '["59","XU"]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000008', '45385', '45378',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
-- ── Mutually Exclusive Procedures ────────────────────────────────────────
('00000000-0000-0000-0016-000000000009', '93306', '93307',
 'MutuallyExclusive', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000010', '93306', '93308',
 'MutuallyExclusive', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000011', '27130', '27132',
 'MutuallyExclusive', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000012', '64483', '64484',
 'MutuallyExclusive', true, '["59","XS"]', 'NCCI', '2026-01-01', NULL, NOW()),
-- ── Imaging + Guidance ───────────────────────────────────────────────────
('00000000-0000-0000-0016-000000000013', '77080', '77081',
 'MutuallyExclusive', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000014', '70553', '70551',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000015', '70553', '70552',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
-- ── Lab Panels ───────────────────────────────────────────────────────────
('00000000-0000-0000-0016-000000000016', '80053', '80048',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000017', '80053', '82565',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
('00000000-0000-0000-0016-000000000018', '80061', '82465',
 'ComponentPart', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
-- ── Anesthesia ───────────────────────────────────────────────────────────
('00000000-0000-0000-0016-000000000019', '00840', '00834',
 'MutuallyExclusive', false, '[]', 'NCCI', '2026-01-01', NULL, NOW()),
-- ── Standard (informational) ─────────────────────────────────────────────
('00000000-0000-0000-0016-000000000020', '99291', '99292',
 'Standard', true, '["25"]', 'NCCI', '2026-01-01', NULL, NOW())
ON CONFLICT ("EditId") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 17 — CMS Default Payer Rules (20 records, is_cms_default = TRUE)
-- ---------------------------------------------------------------------------
-- All rules effective 2026-01-01; CodeType stored as string matching CodeType enum.
INSERT INTO payer_rules (
    "RuleId", "PayerId", "PayerName", "RuleType", "CodeType",
    "PrimaryCode", "SecondaryCode",
    "RuleDescription", "DenialReason", "CorrectiveAction",
    "Severity", "IsCmsDefault", "EffectiveDate", "ExpirationDate",
    "CreatedAt", "UpdatedAt"
) VALUES
-- ── E/M + Same-Day Procedure Rules ──────────────────────────────────────
('00000000-0000-0000-0017-000000000001', NULL, NULL,
 'CombinationInvalid', 'Cpt',
 '99213', '99211',
 'Office visit 99213 includes the work of 99211 on the same date of service.',
 'Component code 99211 is bundled into 99213 by NCCI edit; separate billing not permitted.',
 'Remove 99211 from the claim. Bill only 99213 for the encounter.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

('00000000-0000-0000-0017-000000000002', NULL, NULL,
 'ModifierRequired', 'Cpt',
 '99214', '99213',
 'Two E/M codes for the same provider on the same day require modifier 25 to indicate a significant, separately identifiable service.',
 'Duplicate E/M codes billed same-day without appropriate modifier.',
 'Append modifier 25 to 99213 and document the clinical distinction in the encounter note.',
 'Warning', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Preventive vs Diagnostic E/M ─────────────────────────────────────────
('00000000-0000-0000-0017-000000000003', NULL, NULL,
 'ModifierRequired', 'Cpt',
 '99395', '99213',
 'Preventive visit (99395) and problem-oriented E/M (99213) billed same-day require modifier 25 on the problem E/M code.',
 'Same-day preventive and problem E/M without appropriate modifier.',
 'Add modifier 25 to 99213 and document a separate problem that was addressed beyond the preventive scope.',
 'Warning', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Surgical Global Period ────────────────────────────────────────────────
('00000000-0000-0000-0017-000000000004', NULL, NULL,
 'FrequencyLimit', 'Cpt',
 '27447', NULL,
 'Total knee arthroplasty (27447) carries a 90-day global surgical period. Routine follow-up E/M services during this period are bundled.',
 'E/M services billed during global period of 27447 without unrelated diagnosis or modifier 24.',
 'Attach modifier 24 and an unrelated ICD-10 code when billing E/M during the global period, or defer billing until post-global.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Duplicate Diagnosis Codes ────────────────────────────────────────────
('00000000-0000-0000-0017-000000000005', NULL, NULL,
 'CombinationInvalid', 'Icd10',
 'J45.40', 'J45.41',
 'Moderate persistent asthma (J45.40) and moderate persistent asthma with acute exacerbation (J45.41) should not be coded together on the same claim.',
 'Mutually exclusive asthma specificity codes billed together.',
 'Select only the code that best describes the documented severity and acuity. Remove the less specific code.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

('00000000-0000-0000-0017-000000000006', NULL, NULL,
 'CombinationInvalid', 'Icd10',
 'E11.9', 'E11.65',
 'Type 2 diabetes without complications (E11.9) and type 2 diabetes with hyperglycemia (E11.65) are mutually exclusive and should not be reported together.',
 'Contradictory diabetes specificity codes on the same claim.',
 'Use E11.65 when hyperglycemia is documented; E11.9 only when no complications are specified.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Imaging Bundling ─────────────────────────────────────────────────────
('00000000-0000-0000-0017-000000000007', NULL, NULL,
 'CombinationInvalid', 'Cpt',
 '70553', '70551',
 'MRI brain with and without contrast (70553) includes the without-contrast component (70551). Both cannot be billed on the same date.',
 '70551 is a component of 70553; reporting both results in duplicate billing.',
 'Bill only 70553 (with and without contrast) when both sequences are performed.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Lab Panel Unbundling ─────────────────────────────────────────────────
('00000000-0000-0000-0017-000000000008', NULL, NULL,
 'CombinationInvalid', 'Cpt',
 '80053', '80048',
 'Comprehensive metabolic panel (80053) already includes the basic metabolic panel (80048). Billing both constitutes unbundling.',
 'Component panel 80048 is contained within comprehensive panel 80053.',
 'Remove 80048 from the claim when 80053 is billed.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

('00000000-0000-0000-0017-000000000009', NULL, NULL,
 'CombinationInvalid', 'Cpt',
 '80061', '82465',
 'Lipid panel (80061) includes total cholesterol (82465) as a component; billing both is unbundling.',
 'Total cholesterol 82465 is a component of the lipid panel 80061.',
 'Remove 82465 from the claim and bill only 80061.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Colonoscopy Codes ────────────────────────────────────────────────────
('00000000-0000-0000-0017-000000000010', NULL, NULL,
 'CombinationInvalid', 'Cpt',
 '45385', '45378',
 'Colonoscopy with polypectomy (45385) includes the diagnostic colonoscopy (45378); billing both is incorrect.',
 '45378 is the base procedure component of 45385.',
 'Bill only 45385 when a polypectomy was performed during the colonoscopy.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Echocardiography ─────────────────────────────────────────────────────
('00000000-0000-0000-0017-000000000011', NULL, NULL,
 'CombinationInvalid', 'Cpt',
 '93306', '93307',
 'Complete transthoracic echo with Doppler (93306) and echo without Doppler (93307) are mutually exclusive.',
 'Mutually exclusive echocardiography codes billed on the same date.',
 'Select only 93306 when complete Doppler evaluation is documented.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Cardiac Monitoring ───────────────────────────────────────────────────
('00000000-0000-0000-0017-000000000012', NULL, NULL,
 'DocumentationRequired', 'Cpt',
 '93306', NULL,
 'Complete transthoracic echocardiography (93306) requires documentation of M-mode, 2D imaging, spectral Doppler, and color flow Doppler in the report.',
 'Incomplete documentation for comprehensive echocardiography service.',
 'Ensure the procedure report documents all required imaging modalities. If any component is absent, bill the appropriate limited code.',
 'Warning', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Physical Therapy Frequency ───────────────────────────────────────────
('00000000-0000-0000-0017-000000000013', NULL, NULL,
 'FrequencyLimit', 'Cpt',
 '97110', NULL,
 'Therapeutic exercise (97110) is subject to functional improvement and medical necessity documentation requirements after 12 visits per episode of care.',
 'Therapeutic exercise billed beyond 12 visits without supporting documentation of functional progress.',
 'Include functional outcome measures (e.g., OPTIMAL, FOTO) and physician re-certification of medical necessity in the claim documentation.',
 'Warning', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Anesthesia Unbundling ────────────────────────────────────────────────
('00000000-0000-0000-0017-000000000014', NULL, NULL,
 'CombinationInvalid', 'Cpt',
 '00840', '00834',
 'Anesthesia for lower abdominal surgery (00840) and anesthesia for hernia repair (00834) are mutually exclusive; only one anesthesia base code may be reported per anesthetic.',
 'Two mutually exclusive anesthesia base codes billed for the same anesthetic.',
 'Report only the single anesthesia base code corresponding to the primary operative procedure.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Modifier 59 Overuse ───────────────────────────────────────────────────
('00000000-0000-0000-0017-000000000015', NULL, NULL,
 'DocumentationRequired', 'Cpt',
 '45378', '45330',
 'Colonoscopy (45378) and flexible sigmoidoscopy (45330) performed same session require modifier 59 and documentation that distinct anatomic regions were examined separately.',
 'Insufficiently documented distinct procedural services during the same encounter.',
 'Attach modifier 59 (or XS) to 45330 and document the separate anatomic region in the operative note.',
 'Warning', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Spine Injection Bilateral ────────────────────────────────────────────
('00000000-0000-0000-0017-000000000016', NULL, NULL,
 'ModifierRequired', 'Cpt',
 '64483', '64484',
 'Transforaminal epidural injection L4-L5 (64483) and L5-S1 (64484) are separate anatomic levels. Multiple levels require modifier XS or 59 to indicate distinct structures.',
 'Multiple-level spine injections billed without appropriate distinct-service modifier.',
 'Append modifier XS to the secondary level injection code and document each level in the procedure note.',
 'Warning', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── ICD-10 Bilateral Coding ───────────────────────────────────────────────
('00000000-0000-0000-0017-000000000017', NULL, NULL,
 'CombinationInvalid', 'Icd10',
 'M17.11', 'M17.12',
 'Primary osteoarthritis of right knee (M17.11) and primary osteoarthritis of left knee (M17.12) may be reported together, but M17.0 (bilateral) is preferred when both knees are affected.',
 'Unilateral codes used instead of bilateral combination code M17.0.',
 'Replace M17.11 and M17.12 with M17.0 when bilateral primary osteoarthritis is documented, unless laterality-specific coding is required by payer.',
 'Info', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Preventive Colonoscopy vs Diagnostic ─────────────────────────────────
('00000000-0000-0000-0017-000000000018', NULL, NULL,
 'CombinationInvalid', 'Cpt',
 'G0121', '45378',
 'Colorectal cancer screening colonoscopy (G0121) and diagnostic colonoscopy (45378) cannot both be billed for the same session. Correct code selection depends on the patient''s indication.',
 'Screening and diagnostic colonoscopy codes billed for the same procedure.',
 'If the procedure began as screening but polyps were found, bill 45378 or 45385 with a note that the screening converted to diagnostic.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Well-Child + Sick Visit Same Day ─────────────────────────────────────
('00000000-0000-0000-0017-000000000019', NULL, NULL,
 'ModifierRequired', 'Cpt',
 '99393', '99213',
 'Preventive medicine visit (99393) and problem-focused E/M (99213) billed same day require modifier 25 on the problem E/M when a significant, separately identifiable service is provided.',
 'Same-day preventive and sick-visit E/M without modifier 25.',
 'Append modifier 25 to 99213 and document the distinct problem addressed beyond the well-child scope.',
 'Warning', TRUE, '2026-01-01', NULL, NOW(), NOW()),

-- ── Critical Care + Procedures ───────────────────────────────────────────
('00000000-0000-0000-0017-000000000020', NULL, NULL,
 'CombinationInvalid', 'Cpt',
 '99291', '36620',
 'Critical care (99291) includes arterial line placement (36620) as a bundled service; they should not be billed together on the same date.',
 'Arterial catheterization bundled into critical care is reported separately in error.',
 'Remove 36620 from the claim. The work is included in the critical care time reported.',
 'Error', TRUE, '2026-01-01', NULL, NOW(), NOW())
ON CONFLICT ("RuleId") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 12 — Verification queries
-- ---------------------------------------------------------------------------
SELECT
    'asp_net_users'      AS entity, COUNT(*) AS seeded_count, 5  AS expected_min FROM asp_net_users WHERE "Id"::text LIKE '00000000-0000-0000-0000-00000000000%'
UNION ALL SELECT 'patients',         COUNT(*), 10  FROM patients
UNION ALL SELECT 'appointments',     COUNT(*), 50  FROM appointments
UNION ALL SELECT 'clinical_documents', COUNT(*), 20 FROM clinical_documents
UNION ALL SELECT 'cpt_code_library', COUNT(*), 60  FROM cpt_code_library
UNION ALL SELECT 'cpt_bundle_rules', COUNT(*), 8   FROM cpt_bundle_rules
UNION ALL SELECT 'extracted_data',   COUNT(*), 30  FROM extracted_data
UNION ALL SELECT 'medical_codes',    COUNT(*), 15  FROM medical_codes
UNION ALL SELECT 'intake_data',      COUNT(*), 10  FROM intake_data
UNION ALL SELECT 'audit_logs',       COUNT(*), 20  FROM audit_logs
UNION ALL SELECT 'queue_entries',    COUNT(*), 10  FROM queue_entries
UNION ALL SELECT 'notification_logs',COUNT(*), 25  FROM notification_logs
UNION ALL SELECT 'code_modifiers',   COUNT(*), 10  FROM code_modifiers
UNION ALL SELECT 'bundling_edits',   COUNT(*), 20  FROM bundling_edits
UNION ALL SELECT 'payer_rules',      COUNT(*), 20  FROM payer_rules
ORDER BY entity;

-- Status distribution checks (AC-2)
SELECT 'appointment_status_distribution' AS check_name, "Status", COUNT(*) AS cnt
FROM appointments GROUP BY "Status" ORDER BY "Status";

SELECT 'soft_deleted_patients' AS check_name, COUNT(*) AS cnt
FROM patients WHERE "DeletedAt" IS NOT NULL;

SELECT 'walk_in_appointments' AS check_name, COUNT(*) AS cnt
FROM appointments WHERE "IsWalkIn" = true;

SELECT 'appointments_with_slot_criteria' AS check_name, COUNT(*) AS cnt
FROM appointments WHERE "PreferredSlotCriteria" IS NOT NULL;

DO $$
BEGIN
    RAISE NOTICE '=== UPACIP SEED DATA COMPLETE ===';
    RAISE NOTICE 'Environment: %', COALESCE(current_setting('upacip.environment', true), 'Not set (Development assumed)');
    RAISE NOTICE 'Seed timestamp: %', NOW();
    RAISE NOTICE 'Run verification: SELECT entity, seeded_count, expected_min FROM ... (above)';
END;
$$;
