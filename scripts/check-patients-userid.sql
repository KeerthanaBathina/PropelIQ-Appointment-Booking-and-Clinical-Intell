SELECT p."Id" as patient_id, p."UserId", u."Email", u."FullName", p."MedicalRecordNumber"
FROM patients p
JOIN asp_net_users u ON p."UserId" = u."Id"
ORDER BY u."FullName"
LIMIT 20;
