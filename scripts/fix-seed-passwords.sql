-- Fix seed user password hashes
-- BCrypt(SeedPassword1!, workFactor=10) — generated fresh to match the correct password
UPDATE asp_net_users
SET "PasswordHash" = '$2a$10$jvP47kkz3xh1pWXCGNXxCuf6jkVeiZbgSIV8G4V.MYJV.WNgG3Wqe',
    "AccessFailedCount" = 0,
    "LockoutEnd" = NULL
WHERE "Email" IN (
    'admin@upacip.dev',
    'staff1@upacip.dev',
    'staff2@upacip.dev',
    'patient1@upacip.dev',
    'patient2@upacip.dev'
);

SELECT "Email", LEFT("PasswordHash", 30) AS hash_prefix, "AccessFailedCount" FROM asp_net_users ORDER BY "Email";
