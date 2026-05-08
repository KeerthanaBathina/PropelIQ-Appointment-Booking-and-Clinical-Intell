-- Seed today's queue with 6 patients (May 8, 2026)
-- Uses real patient IDs from the patients table.

DO $$
DECLARE
  today_utc TIMESTAMPTZ := DATE_TRUNC('day', NOW() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC';
  tenant    UUID        := '00000000-0000-0000-0000-000000000001';
BEGIN

  -- ── 1. Insert today's appointments (skip if already present) ─────────────
  INSERT INTO appointments (
    "Id", "PatientId", "AppointmentTime", "Status", "IsWalkIn",
    "AppointmentType", "ProviderName", "BookingReference",
    "NoShowRiskScore", "NoShowRiskBand", "IsRiskEstimated", "RequiresOutreach",
    "Version", "TenantId", "CreatedAt", "UpdatedAt"
  )
  VALUES
    -- 1. Eleanor Hartley — 9:00 AM General Checkup (Dr. Sarah Mitchell)
    (
      'f1000000-0000-0000-0000-000000000001',
      'b0000000-0000-0000-0000-000000000001',
      today_utc + INTERVAL '9 hours',
      'Scheduled', false, 'General Checkup', 'Dr. Sarah Mitchell',
      'Q2026-0001', 18, 'Low', true, false, 0, tenant, NOW(), NOW()
    ),
    -- 2. Marcus Thompson — 9:30 AM Follow-up (Dr. James Thornton)
    (
      'f1000000-0000-0000-0000-000000000002',
      'b0000000-0000-0000-0000-000000000002',
      today_utc + INTERVAL '9 hours 30 minutes',
      'Scheduled', false, 'Follow-up', 'Dr. James Thornton',
      'Q2026-0002', 62, 'Medium', true, true, 0, tenant, NOW(), NOW()
    ),
    -- 3. Priya Patel — 10:00 AM Annual Review (Dr. Sarah Mitchell)
    (
      'f1000000-0000-0000-0000-000000000003',
      'b0000000-0000-0000-0000-000000000003',
      today_utc + INTERVAL '10 hours',
      'Scheduled', false, 'Annual Review', 'Dr. Sarah Mitchell',
      'Q2026-0003', 85, 'High', true, true, 0, tenant, NOW(), NOW()
    ),
    -- 4. James Okafor — 10:30 AM Specialist Review (Dr. James Thornton)
    (
      'f1000000-0000-0000-0000-000000000004',
      'b0000000-0000-0000-0000-000000000004',
      today_utc + INTERVAL '10 hours 30 minutes',
      'Scheduled', false, 'Specialist Review', 'Dr. James Thornton',
      'Q2026-0004', 35, 'Low', true, false, 0, tenant, NOW(), NOW()
    ),
    -- 5. David Chen — 11:00 AM Walk-in (Dr. Sarah Mitchell)
    (
      'f1000000-0000-0000-0000-000000000005',
      'b0000000-0000-0000-0000-000000000006',
      today_utc + INTERVAL '11 hours',
      'Scheduled', true, 'Walk-in Consultation', 'Dr. Sarah Mitchell',
      'Q2026-0005', 10, 'Low', true, false, 0, tenant, NOW(), NOW()
    ),
    -- 6. Jennifer Walsh — 11:30 AM Cardiology (Dr. Emily Park)
    (
      'f1000000-0000-0000-0000-000000000006',
      'b0000000-0000-0000-0000-000000000007',
      today_utc + INTERVAL '11 hours 30 minutes',
      'Scheduled', false, 'Cardiology Consult', 'Dr. Emily Park',
      'Q2026-0006', 45, 'Medium', true, true, 0, tenant, NOW(), NOW()
    ),
    -- 7. Ahmed Hassan — 1:00 PM Diabetes Review (Dr. James Thornton)
    (
      'f1000000-0000-0000-0000-000000000007',
      'b0000000-0000-0000-0000-000000000008',
      today_utc + INTERVAL '13 hours',
      'Scheduled', false, 'Diabetes Review', 'Dr. James Thornton',
      'Q2026-0007', 28, 'Low', true, false, 0, tenant, NOW(), NOW()
    ),
    -- 8. Susan Brewer — 2:00 PM Blood Work Review (Dr. Emily Park)
    (
      'f1000000-0000-0000-0000-000000000008',
      'b0000000-0000-0000-0000-000000000009',
      today_utc + INTERVAL '14 hours',
      'Scheduled', false, 'Blood Work Review', 'Dr. Emily Park',
      'Q2026-0008', 52, 'Medium', true, true, 0, tenant, NOW(), NOW()
    )
  ON CONFLICT ("Id") DO NOTHING;

  -- ── 2. Insert queue entries ────────────────────────────────────────────────
  INSERT INTO queue_entries (
    "Id", "AppointmentId", "ArrivalTimestamp", "WaitTimeMinutes",
    "Priority", "Status", "QueuePosition",
    "IsAutoNoShow", "IsDelayedDetection", "Version",
    "CreatedAt", "UpdatedAt"
  )
  VALUES
    -- Eleanor Hartley — InVisit (already being seen), pos 1
    (
      'ee000000-0000-0000-0000-000000000001',
      'f1000000-0000-0000-0000-000000000001',
      NOW() - INTERVAL '45 minutes', 45, 'Normal', 'InVisit', 1,
      false, false, 0, NOW(), NOW()
    ),
    -- Marcus Thompson — Waiting (arrived, high risk), pos 2
    (
      'ee000000-0000-0000-0000-000000000002',
      'f1000000-0000-0000-0000-000000000002',
      NOW() - INTERVAL '32 minutes', 32, 'Urgent', 'Waiting', 2,
      false, false, 0, NOW(), NOW()
    ),
    -- Priya Patel — Waiting, Urgent (high no-show risk), pos 3
    (
      'ee000000-0000-0000-0000-000000000003',
      'f1000000-0000-0000-0000-000000000003',
      NOW() - INTERVAL '18 minutes', 18, 'Urgent', 'Waiting', 3,
      false, false, 0, NOW(), NOW()
    ),
    -- James Okafor — Waiting, Normal, arrived recently, pos 4
    (
      'ee000000-0000-0000-0000-000000000004',
      'f1000000-0000-0000-0000-000000000004',
      NOW() - INTERVAL '8 minutes', 8, 'Normal', 'Waiting', 4,
      false, false, 0, NOW(), NOW()
    ),
    -- David Chen — Waiting, Normal (walk-in), pos 5
    (
      'ee000000-0000-0000-0000-000000000005',
      'f1000000-0000-0000-0000-000000000005',
      NOW() - INTERVAL '5 minutes', 5, 'Normal', 'Waiting', 5,
      false, false, 0, NOW(), NOW()
    ),
    -- Jennifer Walsh — Waiting, Urgent, pos 6
    (
      'ee000000-0000-0000-0000-000000000006',
      'f1000000-0000-0000-0000-000000000006',
      NOW() - INTERVAL '2 minutes', 2, 'Urgent', 'Waiting', 6,
      false, false, 0, NOW(), NOW()
    ),
    -- Ahmed Hassan — Waiting, Normal (afternoon), pos 7
    (
      'ee000000-0000-0000-0000-000000000007',
      'f1000000-0000-0000-0000-000000000007',
      NOW() - INTERVAL '1 minutes', 1, 'Normal', 'Waiting', 7,
      false, false, 0, NOW(), NOW()
    ),
    -- Susan Brewer — Waiting, Normal (afternoon), pos 8
    (
      'ee000000-0000-0000-0000-000000000008',
      'f1000000-0000-0000-0000-000000000008',
      NOW(), 0, 'Normal', 'Waiting', 8,
      false, false, 0, NOW(), NOW()
    )
  ON CONFLICT ("Id") DO NOTHING;

END $$;

-- Confirm results
SELECT
  q."QueuePosition"     AS "#",
  p."FullName"          AS "Patient",
  a."AppointmentTime"   AS "Appt Time",
  a."AppointmentType"   AS "Type",
  a."ProviderName"      AS "Provider",
  q."Status",
  q."Priority",
  q."WaitTimeMinutes"   AS "Wait (min)"
FROM queue_entries q
JOIN appointments a ON a."Id" = q."AppointmentId"
JOIN patients     p ON p."Id" = a."PatientId"
WHERE q."ArrivalTimestamp" >= DATE_TRUNC('day', NOW() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
  AND q."Status" NOT IN ('NoShow', 'Cancelled')
ORDER BY q."QueuePosition";
