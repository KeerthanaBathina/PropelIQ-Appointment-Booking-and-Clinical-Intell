-- Check today's existing appointments and queue entries
SELECT a."Id", p."FullName", a."AppointmentTime", a."Status", a."ProviderName"
FROM appointments a
JOIN patients p ON p."Id" = a."PatientId"
WHERE a."AppointmentTime" >= DATE_TRUNC('day', NOW())
  AND a."AppointmentTime" < DATE_TRUNC('day', NOW()) + INTERVAL '1 day'
ORDER BY a."AppointmentTime";

-- Existing queue entries today
SELECT q."Id", q."AppointmentId", q."Status", q."Priority", q."QueuePosition"
FROM queue_entries q
WHERE q."ArrivalTimestamp" >= DATE_TRUNC('day', NOW());
