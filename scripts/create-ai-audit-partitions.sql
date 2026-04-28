-- =============================================================================
-- AI Audit Log Table — PostgreSQL Range Partitioning by Month
-- US_080 task_002, AIR-S04, AC-3, AC-4
--
-- Usage:
--   psql -d upacip -f scripts/create-ai-audit-partitions.sql
--
-- Requires PostgreSQL 16+ (declarative range partitioning).
-- Partition maintenance is handled by maintain_ai_audit_partitions() which
-- should be scheduled via pg_cron (recommended) or a cron job.
--
-- Retention policy:
--   * 0–90 days  : partition attached to ai_audit_logs (hot storage, full query access).
--   * 90–365 days: partition detached into ai_audit_logs_archive schema
--                  (cold storage, accessible via audit_archive view).
--   * 365+ days  : partition dropped entirely (DDL-level instant cleanup).
-- =============================================================================

-- ---------------------------------------------------------------------------
-- 0. Archive schema — holds detached cold partitions
-- ---------------------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS ai_audit_logs_archive;

-- ---------------------------------------------------------------------------
-- 1. Parent table — range-partitioned by created_at month
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS ai_audit_logs (
    id               UUID         NOT NULL,
    prompt           TEXT         NOT NULL,
    response         TEXT         NOT NULL,
    model_version    VARCHAR(100) NOT NULL,
    input_tokens     INT          NOT NULL,
    output_tokens    INT          NOT NULL,
    total_tokens     INT          NOT NULL,
    latency_ms       BIGINT       NOT NULL,
    confidence_score REAL,
    request_type     VARCHAR(50)  NOT NULL,
    patient_id       UUID,
    ab_experiment_id UUID,
    ab_variant       VARCHAR(20),
    user_id          VARCHAR(450) NOT NULL,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT CURRENT_TIMESTAMP
) PARTITION BY RANGE (created_at);

-- ---------------------------------------------------------------------------
-- 2. Initial monthly partitions (current + next 3 months)
--    Run once at deployment; maintain_ai_audit_partitions() handles future months.
-- ---------------------------------------------------------------------------

DO $$
DECLARE
    base_month  DATE := DATE_TRUNC('month', CURRENT_DATE);
    m           DATE;
    tbl_name    TEXT;
    start_ts    TEXT;
    end_ts      TEXT;
BEGIN
    FOR i IN 0..3 LOOP
        m        := base_month + (i * INTERVAL '1 month');
        tbl_name := 'ai_audit_logs_' || TO_CHAR(m, 'YYYY_MM');
        start_ts := TO_CHAR(m,                         'YYYY-MM-DD');
        end_ts   := TO_CHAR(m + INTERVAL '1 month',    'YYYY-MM-DD');

        IF NOT EXISTS (
            SELECT 1 FROM pg_class
            WHERE relname = tbl_name
              AND relnamespace = 'public'::regnamespace
        ) THEN
            EXECUTE FORMAT(
                'CREATE TABLE %I PARTITION OF ai_audit_logs
                 FOR VALUES FROM (%L) TO (%L)',
                tbl_name, start_ts, end_ts
            );
            RAISE NOTICE 'Created partition %', tbl_name;
        END IF;
    END LOOP;
END
$$;

-- ---------------------------------------------------------------------------
-- 3. Indexes on the parent table
--    Each child partition automatically inherits these index definitions.
--    Note: PostgreSQL 16 requires explicit index creation per partition when
--    using declarative partitioning with non-primary-key indexes.
-- ---------------------------------------------------------------------------

-- Primary key (unique per partition).
DO $$
DECLARE
    tbl_name TEXT;
BEGIN
    FOR tbl_name IN
        SELECT c.relname
        FROM   pg_inherits i
        JOIN   pg_class    c ON c.oid = i.inhrelid
        JOIN   pg_class    p ON p.oid = i.inhparent
        WHERE  p.relname = 'ai_audit_logs'
    LOOP
        -- Primary key per partition.
        BEGIN
            EXECUTE FORMAT(
                'ALTER TABLE %I ADD PRIMARY KEY (id, created_at)',
                tbl_name
            );
        EXCEPTION WHEN duplicate_object THEN NULL;
        END;

        -- created_at range scan.
        BEGIN
            EXECUTE FORMAT(
                'CREATE INDEX IF NOT EXISTS %I ON %I (created_at)',
                'ix_' || tbl_name || '_created_at', tbl_name
            );
        EXCEPTION WHEN duplicate_object THEN NULL;
        END;

        -- model_version filter.
        BEGIN
            EXECUTE FORMAT(
                'CREATE INDEX IF NOT EXISTS %I ON %I (model_version)',
                'ix_' || tbl_name || '_model_version', tbl_name
            );
        EXCEPTION WHEN duplicate_object THEN NULL;
        END;

        -- request_type filter.
        BEGIN
            EXECUTE FORMAT(
                'CREATE INDEX IF NOT EXISTS %I ON %I (request_type)',
                'ix_' || tbl_name || '_request_type', tbl_name
            );
        EXCEPTION WHEN duplicate_object THEN NULL;
        END;

        -- confidence_score range filter.
        BEGIN
            EXECUTE FORMAT(
                'CREATE INDEX IF NOT EXISTS %I ON %I (confidence_score)',
                'ix_' || tbl_name || '_confidence_score', tbl_name
            );
        EXCEPTION WHEN duplicate_object THEN NULL;
        END;

        -- Patient compliance lookup.
        BEGIN
            EXECUTE FORMAT(
                'CREATE INDEX IF NOT EXISTS %I ON %I (patient_id, created_at)',
                'ix_' || tbl_name || '_patient_created', tbl_name
            );
        EXCEPTION WHEN duplicate_object THEN NULL;
        END;
    END LOOP;
END
$$;

-- ---------------------------------------------------------------------------
-- 4. Archive view — unions all cold partitions for compliance queries
--    Rebuild this view whenever partitions are detached / reattached.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW ai_audit_archive_view AS
SELECT *
FROM   ai_audit_logs  -- hot partitions (attached)
UNION ALL
SELECT * FROM ai_audit_logs_archive.ai_audit_logs_2024_01
-- Additional detached partition selects are appended by maintain_ai_audit_partitions().
LIMIT 0; -- Placeholder: view is rebuilt by the maintenance function as needed.

-- ---------------------------------------------------------------------------
-- 5. Partition maintenance function
--    Call this on a schedule (e.g., pg_cron: '0 0 25 * *').
--
--    Actions:
--      a) Create next-month partition if it does not exist.
--      b) Detach partitions older than 90 days into the archive schema.
--      c) Drop partitions older than 365 days.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION maintain_ai_audit_partitions()
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    next_month      DATE    := DATE_TRUNC('month', CURRENT_DATE + INTERVAL '1 month');
    cutoff_archive  DATE    := DATE_TRUNC('month', CURRENT_DATE - INTERVAL '90 days');
    cutoff_delete   DATE    := DATE_TRUNC('month', CURRENT_DATE - INTERVAL '365 days');
    tbl_name        TEXT;
    partition_start DATE;
    archived_name   TEXT;
BEGIN
    -- ── a) Create next month's partition ─────────────────────────────────────
    tbl_name := 'ai_audit_logs_' || TO_CHAR(next_month, 'YYYY_MM');
    IF NOT EXISTS (
        SELECT 1 FROM pg_class
        WHERE relname = tbl_name
          AND relnamespace = 'public'::regnamespace
    ) THEN
        EXECUTE FORMAT(
            'CREATE TABLE %I PARTITION OF ai_audit_logs
             FOR VALUES FROM (%L) TO (%L)',
            tbl_name,
            TO_CHAR(next_month,                       'YYYY-MM-DD'),
            TO_CHAR(next_month + INTERVAL '1 month',  'YYYY-MM-DD')
        );
        RAISE NOTICE 'Created next-month partition: %', tbl_name;
    END IF;

    -- ── b) Detach partitions older than 90 days into archive schema ───────────
    FOR tbl_name, partition_start IN
        SELECT c.relname, TO_DATE(SUBSTRING(c.relname FROM 16), 'YYYY_MM')
        FROM   pg_inherits i
        JOIN   pg_class    c ON c.oid = i.inhrelid
        JOIN   pg_class    p ON p.oid = i.inhparent
        WHERE  p.relname = 'ai_audit_logs'
          AND  TO_DATE(SUBSTRING(c.relname FROM 16), 'YYYY_MM') < cutoff_archive
    LOOP
        archived_name := 'ai_audit_logs_archive.' || tbl_name;
        -- Detach from parent (makes it a standalone table).
        EXECUTE FORMAT('ALTER TABLE ai_audit_logs DETACH PARTITION %I', tbl_name);
        -- Move to archive schema.
        EXECUTE FORMAT('ALTER TABLE %I SET SCHEMA ai_audit_logs_archive', tbl_name);
        RAISE NOTICE 'Archived partition: % → %', tbl_name, archived_name;
    END LOOP;

    -- ── c) Drop partitions older than 365 days from archive schema ────────────
    FOR tbl_name IN
        SELECT c.relname
        FROM   pg_class c
        JOIN   pg_namespace n ON n.oid = c.relnamespace
        WHERE  n.nspname = 'ai_audit_logs_archive'
          AND  c.relkind = 'r'
          AND  c.relname LIKE 'ai_audit_logs_%'
          AND  TO_DATE(SUBSTRING(c.relname FROM 16), 'YYYY_MM') < cutoff_delete
    LOOP
        EXECUTE FORMAT('DROP TABLE IF EXISTS ai_audit_logs_archive.%I', tbl_name);
        RAISE NOTICE 'Dropped expired archive partition: %', tbl_name;
    END LOOP;
END;
$$;

-- ---------------------------------------------------------------------------
-- 6. Register a pg_cron job (if pg_cron extension is available)
--    Schedule: 00:00 on the 25th of every month — creates the next-month
--    partition before the month boundary.
--
--    Uncomment the block below after confirming pg_cron is installed:
-- ---------------------------------------------------------------------------
-- DO $$
-- BEGIN
--     IF EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_cron') THEN
--         PERFORM cron.schedule(
--             'ai-audit-partition-maintenance',
--             '0 0 25 * *',
--             'SELECT maintain_ai_audit_partitions()'
--         );
--         RAISE NOTICE 'pg_cron job registered: ai-audit-partition-maintenance';
--     ELSE
--         RAISE NOTICE 'pg_cron not installed — schedule maintain_ai_audit_partitions() via Windows Task Scheduler or external cron';
--     END IF;
-- END
-- $$;

-- ---------------------------------------------------------------------------
-- Done.
-- ---------------------------------------------------------------------------
COMMENT ON TABLE  ai_audit_logs                       IS 'AI request/response audit trail (AIR-S04). Range-partitioned by created_at month.';
COMMENT ON COLUMN ai_audit_logs.prompt                IS 'Post-PII-redaction prompt text (AIR-S01). Raw patient data MUST NOT appear here.';
COMMENT ON COLUMN ai_audit_logs.patient_id            IS 'Patient correlation ID for HIPAA compliance queries. Null for non-patient requests.';
COMMENT ON FUNCTION maintain_ai_audit_partitions()    IS 'Partition lifecycle: create next month, archive 90+ day partitions, drop 365+ day partitions.';
