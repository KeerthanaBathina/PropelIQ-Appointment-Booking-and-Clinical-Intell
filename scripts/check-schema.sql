SELECT "Id", "AppointmentType", "ProviderName", "BookingReference", "NoShowRiskBand", "NoShowRiskScore", "IsRiskEstimated"
FROM appointments 
WHERE "Id" IN ('20000000-0000-0000-0000-000000000033','20000000-0000-0000-0000-000000000024')
LIMIT 5;
