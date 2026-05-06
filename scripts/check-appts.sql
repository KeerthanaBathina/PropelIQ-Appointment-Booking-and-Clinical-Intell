SELECT "Id", "PatientId", 
       to_char("AppointmentTime", 'YYYY-MM-DD HH24:MI') AS appt_time,
       "Status", "AppointmentType", "NoShowRiskBand", "IsWalkIn"
FROM appointments 
ORDER BY "AppointmentTime" DESC 
LIMIT 20;
