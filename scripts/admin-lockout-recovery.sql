-- =============================================================================
-- admin-lockout-recovery.sql
-- Emergency Admin Account Lockout Recovery Script
-- =============================================================================
--
-- PURPOSE
--   Unlock a specific admin account that has been locked by ASP.NET Core Identity
--   after 5 consecutive failed login attempts, in situations where no other admin
--   account is available to perform the unlock through the application UI.
--
-- HIPAA COMPLIANCE
--   All manual account modifications are audited per 45 CFR §164.312(b) (FR-093,
--   DR-016). This script inserts an AuditLog entry recording the manual unlock
--   before committing. Do NOT modify or skip the audit insert.
--
-- PREREQUISITES
--   1. PostgreSQL 16.x with access to the upacip database.
--   2. Database user must have SELECT, UPDATE on asp_net_users and INSERT on audit_logs.
--      Use a superuser or a privileged maintenance role — NOT the application role
--      (upacip_app), which has REVOKE on UPDATE/DELETE for audit_logs.
--      The application role can INSERT into audit_logs; use it or the superuser role.
--   3. The target account MUST have the 'Admin' role in asp_net_user_roles.
--      This script validates the role before unlocking. Non-admin accounts are rejected.
--   4. Run during a scheduled maintenance window where possible to avoid race conditions
--      with active login attempts.
--
-- USAGE
--   Option A — Pass email as a psql variable (recommended):
--     psql -h <host> -p 5432 -U <superuser> -d upacip \
--       -v admin_email='admin@example.com' \
--       -f scripts/admin-lockout-recovery.sql
--
--   Option B — Edit the placeholder directly:
--     Replace 'REPLACE_WITH_ADMIN_EMAIL' below with the actual email address,
--     then run: psql -h <host> -p 5432 -U <superuser> -d upacip -f scripts/admin-lockout-recovery.sql
--
-- POST-EXECUTION VERIFICATION
--   After the script commits successfully, verify:
--     1. The account is unlocked:
--          SELECT "Email", "LockoutEnd", "AccessFailedCount"
--          FROM asp_net_users
--          WHERE "Email" = '<target_email>';
--        Expected: LockoutEnd IS NULL, AccessFailedCount = 0.
--     2. The audit log entry was created:
--          SELECT "LogId", "Action", "ResourceType", "ResourceId", "Timestamp",
--                 "IpAddress", "UserAgent"
--          FROM audit_logs
--          WHERE "Action" = 'AdminManualUnlock'
--          ORDER BY "Timestamp" DESC
--          LIMIT 5;
--     3. Notify the account owner that their account has been unlocked and recommend
--        an immediate password change per the organisation's incident response policy.
--
-- SAFETY GUARDS ENFORCED BY THIS SCRIPT
--   ✓ Accepts only a specific email — prevents mass unlock of all accounts.
--   ✓ Verifies Admin role before unlocking — prevents privilege escalation.
--   ✓ Wraps all operations in a single transaction — atomicity (DR-029).
--   ✓ Inserts an AuditLog entry before COMMIT — immutable HIPAA trail (FR-093).
--   ✓ RAISE EXCEPTION terminates the transaction on any validation failure.
--   ✓ Does NOT expose PII beyond what the executing DBA already has access to (NFR-017).
--
-- =============================================================================

-- Set the target admin email.
-- If running with psql -v admin_email='value', this \set is overridden by the CLI arg.
-- If running without -v, change 'REPLACE_WITH_ADMIN_EMAIL' to the actual address.
\set target_email :'admin_email'

DO $$
DECLARE
    v_target_email   TEXT    := :'target_email';
    v_user_id        UUID;
    v_admin_role_id  UUID;
    v_has_admin_role BOOLEAN := FALSE;
    v_is_locked      BOOLEAN := FALSE;
    v_audit_log_id   UUID    := gen_random_uuid();
BEGIN
    -- ── Guard: Reject placeholder — script must be invoked with a real email ──────────────
    IF v_target_email = 'REPLACE_WITH_ADMIN_EMAIL' OR trim(v_target_email) = '' THEN
        RAISE EXCEPTION
            'Admin email parameter is required. '
            'Run: psql ... -v admin_email=''admin@example.com'' -f scripts/admin-lockout-recovery.sql';
    END IF;

    -- ── Step 1: Locate the user by email ─────────────────────────────────────────────────
    SELECT "Id"
    INTO   v_user_id
    FROM   asp_net_users
    WHERE  "NormalizedEmail" = upper(trim(v_target_email));

    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'No account found with email: %. Verify the email address is correct.',
            v_target_email;
    END IF;

    -- ── Step 2: Verify Admin role (prevent privilege escalation) ─────────────────────────
    -- Look up the stable Admin role by name (seeded with name 'Admin' in RoleSeedConfiguration).
    SELECT "Id"
    INTO   v_admin_role_id
    FROM   asp_net_roles
    WHERE  "NormalizedName" = 'ADMIN';

    IF v_admin_role_id IS NULL THEN
        RAISE EXCEPTION
            'Admin role not found in asp_net_roles. '
            'Ensure the database seed migrations have been applied.';
    END IF;

    SELECT EXISTS (
        SELECT 1
        FROM   asp_net_user_roles
        WHERE  "UserId" = v_user_id
          AND  "RoleId" = v_admin_role_id
    )
    INTO v_has_admin_role;

    IF NOT v_has_admin_role THEN
        RAISE EXCEPTION
            'Account % does not have the Admin role. '
            'This script only unlocks Admin accounts. '
            'Use the application UI to unlock non-admin accounts.',
            v_target_email;
    END IF;

    -- ── Step 3: Check lockout state ───────────────────────────────────────────────────────
    -- Informational check — proceed even if not currently locked so the audit trail
    -- is created for any manual intervention attempt (HIPAA FR-093).
    SELECT (
        "LockoutEnd" IS NOT NULL
        AND "LockoutEnd" > NOW()
        AND "LockoutEnabled" = TRUE
    )
    INTO v_is_locked
    FROM asp_net_users
    WHERE "Id" = v_user_id;

    IF NOT v_is_locked THEN
        RAISE NOTICE
            'WARNING: Account % is not currently locked. '
            'Proceeding to reset failed attempt counter and insert audit log entry.',
            v_target_email;
    END IF;

    -- ── Step 4: Unlock the account ────────────────────────────────────────────────────────
    -- Reset AccessFailedCount to 0 and clear LockoutEnd per ASP.NET Core Identity schema.
    -- LockoutEnabled is preserved (controls whether future lockouts are enforced).
    UPDATE asp_net_users
    SET    "LockoutEnd"        = NULL,
           "AccessFailedCount" = 0
    WHERE  "Id" = v_user_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'UPDATE on asp_net_users affected 0 rows for UserId %. '
            'Transaction will be rolled back.', v_user_id;
    END IF;

    -- ── Step 5: Insert HIPAA audit log entry ─────────────────────────────────────────────
    -- Action = 'AdminManualUnlock' (AuditAction.AdminManualUnlock, US_065 TASK_003).
    -- ip_address  = 'database-console' — indicates this was a direct DB-level operation.
    -- user_agent  = 'emergency-recovery-script' — identifies the script as the actor.
    -- resource_id = the unlocked user's UUID (for traceability without exposing PII beyond
    --               what the audit log already stores per its schema design, NFR-017).
    INSERT INTO audit_logs (
        "LogId",
        "UserId",
        "Action",
        "ResourceType",
        "ResourceId",
        "Timestamp",
        "IpAddress",
        "UserAgent"
    )
    VALUES (
        v_audit_log_id,
        v_user_id,                           -- UserId = the unlocked admin (self-reference)
        'AdminManualUnlock',                 -- AuditAction.AdminManualUnlock
        'User',                              -- ResourceType
        v_user_id,                           -- ResourceId = unlocked user's UUID
        NOW(),                               -- UTC timestamp
        'database-console',                  -- ip_address — not an HTTP request
        'emergency-recovery-script'          -- user_agent — identifies this script
    );

    -- ── Step 6: Confirm results ───────────────────────────────────────────────────────────
    RAISE NOTICE '============================================================';
    RAISE NOTICE 'Admin account unlock SUCCESSFUL';
    RAISE NOTICE '  Email          : %', v_target_email;
    RAISE NOTICE '  UserId         : %', v_user_id;
    RAISE NOTICE '  AuditLog entry : %', v_audit_log_id;
    RAISE NOTICE '  Timestamp      : %', NOW();
    RAISE NOTICE '------------------------------------------------------------';
    RAISE NOTICE 'POST-EXECUTION: Verify with:';
    RAISE NOTICE '  SELECT "Email","LockoutEnd","AccessFailedCount"';
    RAISE NOTICE '  FROM asp_net_users WHERE "Id" = ''%'';', v_user_id;
    RAISE NOTICE '============================================================';
    RAISE NOTICE 'SECURITY: Notify the account owner and recommend an immediate';
    RAISE NOTICE 'password change per the organisation incident response policy.';
    RAISE NOTICE '============================================================';
END;
$$;
