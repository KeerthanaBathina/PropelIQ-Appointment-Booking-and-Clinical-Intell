-- Fix invalid 'Called' status — not a valid QueueStatus enum value
UPDATE queue_entries
SET "Status" = 'Waiting', "UpdatedAt" = NOW()
WHERE "Status" = 'Called';

-- Confirm
SELECT q."QueuePosition" AS "#", p."FullName" AS "Patient", q."Status", q."Priority", q."WaitTimeMinutes" AS "Wait(min)"
FROM queue_entries q
JOIN appointments a ON a."Id" = q."AppointmentId"
JOIN patients p ON p."Id" = a."PatientId"
WHERE q."ArrivalTimestamp" >= DATE_TRUNC('day', NOW() AT TIME ZONE 'UTC') AT TIME ZONE 'UTC'
  AND q."Status" NOT IN ('NoShow', 'Cancelled')
ORDER BY q."QueuePosition";
