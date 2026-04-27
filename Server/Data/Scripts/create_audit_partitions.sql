-- =============================================================================
-- create_audit_partitions.sql
-- UPACIP — Idempotent Monthly Partition Pre-Creation for audit_logs
-- =============================================================================
-- Version   : 1.0.0
-- Target    : PostgreSQL 16+
-- Run as    : superuser (postgres) or role with CREATE privilege on the schema
-- Scheduling: Run monthly via Windows Task Scheduler before month rollover
--             (recommended: 1st of each month at 02:00)
--
-- Usage:
--   psql -U postgres -d upacip -f Server/Data/Scripts/create_audit_partitions.sql
--
-- Or to call the function directly for a custom months-ahead window:
--   SELECT create_audit_partitions(24);   -- pre-create 24 months of partitions
--
-- Related files:
--   Server/Data/Scripts/audit_retention_policy.sql — HIPAA retention verification
--   src/UPACIP.DataAccess/Migrations/20260427000001_AddAuditLogPartitioningAndImmutability.cs
-- =============================================================================

-- =============================================================================
-- Function: create_audit_partitions(months_ahead)
--
-- Dynamically creates monthly RANGE partitions of audit_logs for the specified
-- number of months ahead (counting from the current month).
--
-- Idempotent: checks pg_class before creating each partition so that
-- re-execution does not raise errors or create duplicate partitions.
--
-- After creating each partition, applies the same REVOKE UPDATE/DELETE
-- permissions as the base migration to enforce immutability on new partitions
-- (US_064 AC-2, DR-016, NFR-012).
--
-- Parameters:
--   months_ahead  INTEGER  Number of future months to pre-create (default: 12).
--                           Set to 0 to only create the current month.
-- =============================================================================
CREATE OR REPLACE FUNCTION create_audit_partitions(months_ahead INTEGER DEFAULT 12)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_month          INTEGER;
    start_date       DATE;
    end_date         DATE;
    partition_name   TEXT;
    partition_exists BOOLEAN;
BEGIN
    FOR v_month IN 0..months_ahead LOOP
        -- Calculate first day of the target month.
        start_date     := date_trunc('month', CURRENT_DATE + (v_month * INTERVAL '1 month'))::DATE;
        end_date       := (start_date + INTERVAL '1 month')::DATE;
        partition_name := 'audit_logs_' || to_char(start_date, 'YYYY_MM');

        -- Check if partition already exists in pg_class (idempotency guard).
        SELECT EXISTS (
            SELECT 1
            FROM   pg_class c
            JOIN   pg_namespace n ON n.oid = c.relnamespace
            WHERE  c.relname  = partition_name
            AND    n.nspname  = 'public'
        ) INTO partition_exists;

        IF NOT partition_exists THEN
            -- Create the monthly partition using parameterised EXECUTE to prevent
            -- SQL injection from the partition_name or date strings (OWASP A03).
            EXECUTE format(
                'CREATE TABLE %I PARTITION OF audit_logs FOR VALUES FROM (%L::timestamptz) TO (%L::timestamptz)',
                partition_name,
                start_date::TEXT,
                end_date::TEXT
            );

            -- Mirror immutability permissions: revoke UPDATE/DELETE from application role.
            -- This ensures new partitions have identical permission constraints to those
            -- created by the base migration (US_064 AC-2, NFR-012).
            EXECUTE format('REVOKE UPDATE, DELETE ON %I FROM upacip_app', partition_name);
            EXECUTE format('REVOKE UPDATE, DELETE ON %I FROM PUBLIC',     partition_name);

            RAISE NOTICE 'audit_logs partition created: % (% to %)', partition_name, start_date, end_date;
        ELSE
            RAISE NOTICE 'audit_logs partition already exists, skipping: %', partition_name;
        END IF;
    END LOOP;

    RAISE NOTICE 'create_audit_partitions complete. months_ahead=%.', months_ahead;
END;
$$;

-- =============================================================================
-- Execute: pre-create 12 months of partitions from today's date.
-- Safe to call repeatedly (idempotent).
-- =============================================================================
SELECT create_audit_partitions(12);
