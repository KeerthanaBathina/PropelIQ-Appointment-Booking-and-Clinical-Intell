-- Seed today's arrival queue with 5 patients (May 6, 2026 UTC)
-- Creates appointments at today's times + matching queue entries.

DO $$
DECLARE
  today_utc TIMESTAMPTZ := DATE_TRUNC('day', NOW() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC';
  tenant    UUID        := '00000000-0000-0000-0000-000000000001';
BEGIN

  -- ── 1. Insert 5 today-dated appointments (skip if already present) ────────
  INSERT INTO appointments (
    "Id", "PatientId", "AppointmentTime", "Status", "IsWalkIn",
    "AppointmentType", "ProviderName", "BookingReference",
    "NoShowRiskScore", "NoShowRiskBand", "IsRiskEstimated", "RequiresOutreach",
    "Version", "TenantId", "CreatedAt", "UpdatedAt"
  )
  VALUES
    -- Patient: Eleanor Hartley
    (
      'c1000000-0000-0000-0000-000000000001',
      '10000000-0000-0000-0000-000000000001',
      today_utc + INTERVAL '9 hours',
      'Scheduled', false, 'General Checkup', 'Sarah Mitchell',
      'QUEUE-2026-0001', 18, 'Low', false, false, 0, tenant, NOW(), NOW()
    ),
    -- Patient: Marcus Thompson
    (
      'c1000000-0000-0000-0000-000000000002',
      '10000000-0000-0000-0000-000000000002',
      today_utc + INTERVAL '9 hours 30 minutes',
      'Scheduled', false, 'Follow-up', 'James Thornton',
      'QUEUE-2026-0002', 62, 'Medium', false, true, 0, tenant, NOW(), NOW()
    ),
    -- Patient: Priya Patel
    (
      'c1000000-0000-0000-0000-000000000003',
      '10000000-0000-0000-0000-000000000003',
      today_utc + INTERVAL '10 hours',
      'Scheduled', false, 'Annual Review', 'Sarah Mitchell',
      'QUEUE-2026-0003', 85, 'High', false, true, 0, tenant, NOW(), NOW()
    ),
    -- Patient: James Okafor
    (
      'c1000000-0000-0000-0000-000000000004',
      '10000000-0000-0000-0000-000000000004',
      today_utc + INTERVAL '10 hours 30 minutes',
      'Scheduled', false, 'Specialist Review', 'James Thornton',
      'QUEUE-2026-0004', 35, 'Low', false, false, 0, tenant, NOW(), NOW()
    ),
    -- Patient: Maria Santos (walk-in)
    (
      'c1000000-0000-0000-0000-000000000005',
      '10000000-0000-0000-0000-000000000005',
      today_utc + INTERVAL '11 hours',
      'Scheduled', true, 'Walk-in Consultation', 'Sarah Mitchell',
      'QUEUE-2026-0005', 10, 'Low', false, false, 0, tenant, NOW(), NOW()
    )
  ON CONFLICT DO NOTHING;

  -- ── 2. Insert queue entries with today's arrival timestamps ───────────────
  INSERT INTO queue_entries (
    "Id", "AppointmentId", "ArrivalTimestamp", "WaitTimeMinutes",
    "Priority", "Status", "QueuePosition",
    "IsAutoNoShow", "IsDelayedDetection", "Version",
    "CreatedAt", "UpdatedAt"
  )
  VALUES
    -- Eleanor Hartley — arrived 8 min ago, Waiting, Normal, pos 1
    (
      'e1000000-0000-0000-0000-000000000001',
      'c1000000-0000-0000-0000-000000000001',
      NOW() - INTERVAL '8 minutes', 8, 'Normal', 'Waiting', 1,
      false, false, 0, NOW(), NOW()
    ),
    -- Marcus Thompson — arrived 5 min ago, Waiting, Urgent (medium risk + outreach)
    (
      'e1000000-0000-0000-0000-000000000002',
      'c1000000-0000-0000-0000-000000000002',
      NOW() - INTERVAL '5 minutes', 5, 'Urgent', 'Waiting', 2,
      false, false, 0, NOW(), NOW()
    ),
    -- Priya Patel — arrived 18 min ago, InVisit, Urgent (high risk)
    (
      'e1000000-0000-0000-0000-000000000003',
      'c1000000-0000-0000-0000-000000000003',
      NOW() - INTERVAL '18 minutes', 18, 'Urgent', 'InVisit', 3,
      false, false, 0, NOW(), NOW()
    ),
    -- James Okafor — arrived 2 min ago, Waiting, Normal
    (
      'e1000000-0000-0000-0000-000000000004',
      'c1000000-0000-0000-0000-000000000004',
      NOW() - INTERVAL '2 minutes', 2, 'Normal', 'Waiting', 4,
      false, false, 0, NOW(), NOW()
    ),
    -- Maria Santos — walk-in, arrived 11 min ago, Waiting, Normal
    (
      'e1000000-0000-0000-0000-000000000005',
      'c1000000-0000-0000-0000-000000000005',
      NOW() - INTERVAL '11 minutes', 11, 'Normal', 'Waiting', 5,
      false, false, 0, NOW(), NOW()
    )
  ON CONFLICT DO NOTHING;

END $$;

SELECT
  p."FullName"          AS "Patient",
  a."AppointmentType",
  a."ProviderName",
  q."Status",
  q."Priority",
  q."WaitTimeMinutes"   AS "Wait (min)",
  q."QueuePosition"     AS "Pos"
FROM queue_entries q
JOIN appointments a ON a."Id" = q."AppointmentId"
JOIN patients     p ON p."Id" = a."PatientId"
WHERE q."ArrivalTimestamp" >= DATE_TRUNC('day', NOW() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
ORDER BY q."QueuePosition";

