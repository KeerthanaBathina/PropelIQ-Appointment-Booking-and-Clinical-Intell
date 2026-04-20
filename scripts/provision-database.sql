-- =============================================================================
-- UPACIP Database Provisioning Script
-- PostgreSQL 16 — run as superuser (postgres)
-- Idempotent: safe to re-run without dropping existing data
-- =============================================================================

-- ── 1. Create application role (idempotent) ───────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT FROM pg_catalog.pg_roles WHERE rolname = 'upacip_app'
    ) THEN
        -- Least-privilege application role: no superuser, no createdb, no createrole
        CREATE ROLE upacip_app
            NOSUPERUSER
            NOCREATEDB
            NOCREATEROLE
            NOINHERIT
            LOGIN
            -- Password set via \set or passed by provisioning script
            PASSWORD 'PLACEHOLDER_CHANGE_IN_PROVISION_SCRIPT';

        RAISE NOTICE 'Role upacip_app created.';
    ELSE
        RAISE NOTICE 'Role upacip_app already exists — skipping create.';
    END IF;
END
$$;

-- ── 2. Create application database (idempotent) ───────────────────────────────
-- Cannot use DO$$ block for CREATE DATABASE; guard via shell in provision script.
-- This statement is skipped if the DB already exists (handled by provision-database.ps1).
SELECT 'Database creation handled by provision-database.ps1 idempotency check';

-- ── 3. Connect to the upacip database for schema-level grants ────────────────
-- NOTE: \connect is executed by the provision script; the following statements
--       must run after connecting to the upacip database.

-- Revoke default PUBLIC create privilege on public schema (security hardening)
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Grant CONNECT on database to the application role
GRANT CONNECT ON DATABASE upacip TO upacip_app;

-- Grant USAGE on public schema to the application role
GRANT USAGE ON SCHEMA public TO upacip_app;

-- Grant DML privileges on all current tables in the public schema
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO upacip_app;

-- Grant privileges on sequences (required for SERIAL/BIGSERIAL primary keys)
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO upacip_app;

-- Ensure future tables and sequences created by postgres are also accessible
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO upacip_app;

ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO upacip_app;

-- ── 4. Verify grants ──────────────────────────────────────────────────────────
SELECT
    grantee,
    table_schema,
    privilege_type
FROM information_schema.role_table_grants
WHERE grantee = 'upacip_app'
ORDER BY table_schema, privilege_type;

-- ── 5. pgvector extension (semantic search / AI clinical intelligence) ────────
-- IMPORTANT: The pgvector shared library (vector.dll / vector.so) must be
-- installed at the OS level BEFORE the CREATE EXTENSION step can succeed.
--
--   Windows: See scripts/provision-pgvector.sql for installation instructions.
--   Linux  : sudo apt install postgresql-<major>-pgvector
--
-- Run scripts/provision-pgvector.sql as superuser AFTER this script to create
-- the vector extension and the three embedding tables:
--   • medical_terminology_embeddings
--   • intake_template_embeddings
--   • coding_guideline_embeddings
--
-- Example (Windows PowerShell, run as Administrator):
--   $env:PGPASSWORD = "<superuser_password>"
--   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -d upacip -f scripts\provision-pgvector.sql
