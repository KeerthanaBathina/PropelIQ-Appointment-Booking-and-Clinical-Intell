SELECT u."Id", u."Email", u."FullName", r."Name" as "Role"
FROM asp_net_users u
JOIN asp_net_user_roles ur ON u."Id" = ur."UserId"
JOIN asp_net_roles r ON ur."RoleId" = r."Id"
WHERE r."Name" = 'Patient'
ORDER BY u."FullName"
LIMIT 15;
