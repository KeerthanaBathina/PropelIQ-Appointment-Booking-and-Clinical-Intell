SELECT "Id", "ProviderId", "ProviderName", "DayOfWeek", "StartTime", "EndTime",
       "SlotDurationMinutes", "AppointmentType", "IsActive"
FROM provider_availability_templates
ORDER BY "ProviderName", "DayOfWeek", "StartTime";
