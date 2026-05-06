SELECT "Email", "EmailConfirmed", "LockoutEnabled", "TwoFactorEnabled", LEFT("PasswordHash",30) AS hash_prefix FROM asp_net_users ORDER BY "Email";
