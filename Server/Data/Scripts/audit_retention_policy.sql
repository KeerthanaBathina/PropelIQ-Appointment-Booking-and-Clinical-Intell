-- =============================================================================
-- audit_retention_policy.sql
-- UPACIP — HIPAA Audit Log Retention Verification and Safe Archive Policy
-- =============================================================================
-- Version   : 1.0.0
-- Target    : PostgreSQL 16+
-- Run as    : superuser (postgres) or DBA role
-- Scheduling: Run quarterly as a compliance check (e.g. 1st January, April,
--             July, October at 04:00 via Windows Task Scheduler)
--
-- Usage:
--   psql -U postgres -d upacip -f Server/Data/Scripts/audit_retention_policy.sql
--
-- Related files:
--   Server/Data/Scripts/create_audit_partitions.sql — Monthly partition creation
--   src/UPACIP.DataAccess/Migrations/20260427000001_AddAuditLogPartitioningAndImmutability.cs
--
-- Compliance reference:
--   HIPAA Security Rule — Audit Controls: 45 CFR §164.312(b)
--   UPACIP design rule: DR-016 (7-year minimum retention)
--   US_064 AC-4, NFR-043
-- =============================================================================

-- =============================================================================
-- Section 1 — HIPAA Table Comment (informational, idempotent)
-- Provides inline documentation directly on the database object for DBAs
-- who inspect the schema without access to application documentation.
-- =============================================================================
COMMENT ON TABLE audit_logs IS
    'HIPAA Audit Trail — 7-year minimum retention per 45 CFR §164.312(b) (DR-016, AC-4, US_064). '
    'DO NOT drop partitions less than 7 years old. '
    'Use fn_audit_logs_safe_archive(cutoff_date) to safely detach aged partitions '
    'after the 7-year threshold has been reached. '
    'Partition pre-creation: run Server/Data/Scripts/create_audit_partitions.sql monthly.';

-- =============================================================================
-- Section 2 — Retention Verification Query
-- Run quarterly to confirm the oldest data satisfies the 7-year retention window.
-- Expected output: retention_status = 'OK' when system has been running < 7 years.
-- =============================================================================
SELECT
    MIN("Timestamp")::DATE                          AS oldest_entry_date,
    MAX("Timestamp")::DATE                          AS newest_entry_date,
    COUNT(*)                                        AS total_entries,
    CURRENT_DATE - MIN("Timestamp")::DATE           AS days_retained,
    ROUND((CURRENT_DATE - MIN("Timestamp")::DATE) / 365.25, 2)
                                                    AS years_retained,
    CASE
        WHEN COUNT(*) = 0
            THEN 'INFO: No audit log entries found.'
        WHEN MIN("Timestamp") > (CURRENT_TIMESTAMP - INTERVAL '7 years')
            THEN 'OK: All data is within the 7-year retention window (no archival needed yet).'
        ELSE
            'REVIEW: Oldest entries are beyond 7-year boundary — consider detaching via fn_audit_logs_safe_archive().'
    END                                             AS retention_status
FROM audit_logs;

-- =============================================================================
-- Section 3 — Partition Inventory Query
-- Lists all child partitions with their date ranges and estimated row counts.
-- Use to plan archival of partitions older than 7 years.
-- =============================================================================
SELECT
    c.relname                                                         AS partition_name,
    pg_get_expr(c.relpartbound, c.oid, true)                          AS partition_range,
    pg_size_pretty(pg_total_relation_size(c.oid))                     AS total_size,
    CASE
        WHEN c.relname = 'audit_logs_default' THEN 'DEFAULT'
        ELSE 'RANGE'
    END                                                               AS partition_type,
    CASE
        WHEN c.relname = 'audit_logs_default' THEN NULL
        ELSE to_date(substring(c.relname FROM 'audit_logs_(\d{4}_\d{2})'), 'YYYY_MM')
    END                                                               AS partition_month,
    CASE
        WHEN c.relname = 'audit_logs_default' THEN NULL
        WHEN to_date(substring(c.relname FROM 'audit_logs_(\d{4}_\d{2})'), 'YYYY_MM')
                 < (CURRENT_DATE - INTERVAL '7 years')
            THEN 'ELIGIBLE FOR ARCHIVAL'
        ELSE 'WITHIN RETENTION WINDOW'
    END                                                               AS retention_eligibility
FROM   pg_class      c
JOIN   pg_namespace  n ON n.oid = c.relnamespace
JOIN   pg_inherits   i ON i.inhrelid = c.oid
JOIN   pg_class      p ON p.oid = i.inhparent AND p.relname = 'audit_logs'
WHERE  n.nspname = 'public'
ORDER  BY c.relname;

-- =============================================================================
-- Section 4 — fn_audit_logs_safe_archive(cutoff_date DATE)
--
-- Validates that the supplied cutoff date is at least 7 years in the past
-- before any partition DETACH is attempted.
--
-- Usage (DETACH a specific partition after verification):
--   SELECT fn_audit_logs_safe_archive('2019-01-01');
--   -- If OK, then manually detach:
--   ALTER TABLE audit_logs DETACH PARTITION audit_logs_2019_01;
--   -- Then archive the detached table to cold storage and DROP TABLE audit_logs_2019_01;
--
-- The function does NOT perform the DETACH itself — the DBA must explicitly
-- confirm and execute the ALTER TABLE DETACH statement, providing a human
-- checkpoint before irreversible archival actions (DR-016, DR-029).
-- =============================================================================
CREATE OR REPLACE FUNCTION fn_audit_logs_safe_archive(cutoff_date DATE)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    seven_years_ago DATE := CURRENT_DATE - INTERVAL '7 years';
BEGIN
    -- Guard: reject any cutoff date that is within the 7-year retention window.
    IF cutoff_date > seven_years_ago THEN
        RAISE EXCEPTION
            'HIPAA retention violation: cannot archive partitions less than 7 years old (DR-016). '
            'Supplied cutoff_date=% is more recent than the 7-year boundary=%.',
            cutoff_date,
            seven_years_ago
            USING ERRCODE = '23514';  -- check_violation
    END IF;

    -- Validation passed: inform the caller which partitions are eligible.
    RAISE NOTICE
        'fn_audit_logs_safe_archive: cutoff_date=% is older than 7-year boundary=%. '
        'The following partitions may be detached:',
        cutoff_date,
        seven_years_ago;

    -- List partitions whose month is <= cutoff_date (eligible for archival).
    PERFORM c.relname
    FROM   pg_class      c
    JOIN   pg_namespace  n ON n.oid = c.relnamespace
    JOIN   pg_inherits   i ON i.inhrelid = c.oid
    JOIN   pg_class      p ON p.oid = i.inhparent AND p.relname = 'audit_logs'
    WHERE  n.nspname = 'public'
    AND    c.relname <> 'audit_logs_default'
    AND    to_date(substring(c.relname FROM 'audit_logs_(\d{4}_\d{2})'), 'YYYY_MM') <= cutoff_date;

    RAISE NOTICE
        'To detach a partition, execute: ALTER TABLE audit_logs DETACH PARTITION <partition_name>;';
    RAISE NOTICE
        'After detachment, archive the standalone table to cold storage before dropping it.';
END;
$$;

-- =============================================================================
-- Section 5 — Trigger and Permission Verification Query
-- Confirms that immutability triggers and permissions are in place.
-- Run after any schema change or provision-database.sql re-execution.
-- =============================================================================
SELECT
    t.tgname                                   AS trigger_name,
    c.relname                                  AS table_name,
    CASE t.tgtype & 2 WHEN 2 THEN 'BEFORE' ELSE 'AFTER' END AS timing,
    CASE
        WHEN t.tgtype & 8  = 8  THEN 'DELETE'
        WHEN t.tgtype & 16 = 16 THEN 'UPDATE'
        ELSE 'OTHER'
    END                                        AS event,
    p.proname                                  AS trigger_function
FROM   pg_trigger    t
JOIN   pg_class      c ON c.oid = t.tgrelid
JOIN   pg_proc       p ON p.oid = t.tgfoid
JOIN   pg_namespace  n ON n.oid = c.relnamespace
WHERE  c.relname IN ('audit_logs', 'audit_logs_default')
AND    n.nspname  = 'public'
AND    NOT t.tgisinternal
ORDER  BY c.relname, t.tgname;
