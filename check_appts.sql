SELECT "Id", "AppointmentTime", "Status", "ProviderName", "AppointmentType", "BookingReference", "PatientId"
FROM appointments
ORDER BY "AppointmentTime" DESC
LIMIT 20;
