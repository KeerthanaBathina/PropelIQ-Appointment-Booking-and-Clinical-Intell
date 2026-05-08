SELECT pp."Id" as profile_id, pp."UserId", u."Email", u."FullName"
FROM patient_profiles pp
JOIN asp_net_users u ON pp."UserId" = u."Id"
ORDER BY u."FullName"
LIMIT 15;
