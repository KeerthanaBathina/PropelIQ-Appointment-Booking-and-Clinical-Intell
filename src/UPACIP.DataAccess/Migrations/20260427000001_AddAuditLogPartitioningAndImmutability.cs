using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Converts the <c>audit_logs</c> table to a PostgreSQL 16 range-partitioned table on
    /// <c>"Timestamp"</c>, adds composite performance indexes, enforces database-level
    /// immutability via BEFORE UPDATE/DELETE triggers, documents the 7-year HIPAA retention
    /// requirement, and restricts the application role to INSERT/SELECT only (US_064 AC-2,
    /// AC-4, DR-016, NFR-012).
    ///
    /// Changes applied by <see cref="Up"/>:
    ///   1. Drop FK &amp; indexes from existing <c>audit_logs</c>.
    ///   2. Rename <c>audit_logs</c> → <c>audit_logs_old</c> (data preserved for migration).
    ///   3. Create new <c>audit_logs</c> PARTITION BY RANGE (&quot;Timestamp&quot;).
    ///      PK becomes (<c>LogId</c>, <c>Timestamp</c>) to satisfy PostgreSQL's requirement
    ///      that the partition key be part of every unique constraint.
    ///   4. Create 13 monthly partitions: 2026-04 through 2027-04, plus a default partition.
    ///   5. Migrate all rows from <c>audit_logs_old</c> to the new partitioned table.
    ///   6. Drop <c>audit_logs_old</c>.
    ///   7. Re-add FK <c>audit_logs.UserId → asp_net_users.Id ON DELETE SET NULL</c>.
    ///   8. Create composite indexes for query performance.
    ///   9. Create <c>fn_audit_logs_prevent_modification()</c> trigger function.
    ///  10. Attach BEFORE UPDATE and BEFORE DELETE triggers (inherited by all child partitions).
    ///  11. Add COMMENT for HIPAA retention documentation.
    ///  12. Revoke UPDATE/DELETE from application role <c>upacip_app</c> and PUBLIC.
    ///
    /// <see cref="Down"/> reverses all changes including data restoration and permission restoration.
    ///
    /// NOTE: <c>CREATE INDEX CONCURRENTLY</c> cannot be used inside a transaction block.
    ///       Regular <c>CREATE INDEX</c> is used here. For live production environments
    ///       requiring zero-downtime index creation, use the <c>create_audit_partitions.sql</c>
    ///       script which runs outside a transaction (NFR-021).
    /// </summary>
    public partial class AddAuditLogPartitioningAndImmutability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Step 1: Drop FK and existing indexes from the current audit_logs ─────────────
            // Constraints and indexes belong to the table; they will not follow a rename
            // automatically in the way we need, so we drop them and recreate on the new table.
            migrationBuilder.Sql(@"
ALTER TABLE audit_logs DROP CONSTRAINT IF EXISTS ""FK_audit_logs_asp_net_users_UserId"";
DROP INDEX IF EXISTS ix_audit_logs_user_id_timestamp;
DROP INDEX IF EXISTS ix_audit_logs_action_timestamp;
DROP INDEX IF EXISTS ix_audit_logs_timestamp;
DROP INDEX IF EXISTS ix_audit_logs_entity_type_timestamp;
");

            // ── Step 2: Rename existing table (preserves all data during this migration) ─────
            migrationBuilder.Sql(@"ALTER TABLE audit_logs RENAME TO audit_logs_old;");

            // ── Step 3: Create partitioned audit_logs ────────────────────────────────────────
            // PK must include the partition key (""Timestamp"") per PostgreSQL 16 requirement.
            // Existing application code uses LogId as the logical identifier; the composite PK
            // does not break EF Core queries since LogId is still unique across all partitions.
            migrationBuilder.Sql(@"
CREATE TABLE audit_logs (
    ""LogId""        uuid                       NOT NULL,
    ""UserId""       uuid,
    ""Action""       character varying(50)      NOT NULL,
    ""ResourceType"" character varying(100)     NOT NULL,
    ""ResourceId""   uuid,
    ""Timestamp""    timestamp with time zone   NOT NULL,
    ""IpAddress""    character varying(45)      NOT NULL,
    ""UserAgent""    character varying(500)     NOT NULL,
    CONSTRAINT ""PK_audit_logs"" PRIMARY KEY (""LogId"", ""Timestamp"")
) PARTITION BY RANGE (""Timestamp"");
");

            // ── Step 4: Monthly partitions — 2026-04 through 2027-04 (13 months) ─────────────
            // Naming convention: audit_logs_YYYY_MM
            // Partition ranges use UTC boundaries (00:00:00+00 = start of month in UTC).
            migrationBuilder.Sql(@"
CREATE TABLE audit_logs_2026_04 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-04-01 00:00:00+00') TO ('2026-05-01 00:00:00+00');

CREATE TABLE audit_logs_2026_05 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-05-01 00:00:00+00') TO ('2026-06-01 00:00:00+00');

CREATE TABLE audit_logs_2026_06 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-06-01 00:00:00+00') TO ('2026-07-01 00:00:00+00');

CREATE TABLE audit_logs_2026_07 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-07-01 00:00:00+00') TO ('2026-08-01 00:00:00+00');

CREATE TABLE audit_logs_2026_08 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-08-01 00:00:00+00') TO ('2026-09-01 00:00:00+00');

CREATE TABLE audit_logs_2026_09 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-09-01 00:00:00+00') TO ('2026-10-01 00:00:00+00');

CREATE TABLE audit_logs_2026_10 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-10-01 00:00:00+00') TO ('2026-11-01 00:00:00+00');

CREATE TABLE audit_logs_2026_11 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-11-01 00:00:00+00') TO ('2026-12-01 00:00:00+00');

CREATE TABLE audit_logs_2026_12 PARTITION OF audit_logs
    FOR VALUES FROM ('2026-12-01 00:00:00+00') TO ('2027-01-01 00:00:00+00');

CREATE TABLE audit_logs_2027_01 PARTITION OF audit_logs
    FOR VALUES FROM ('2027-01-01 00:00:00+00') TO ('2027-02-01 00:00:00+00');

CREATE TABLE audit_logs_2027_02 PARTITION OF audit_logs
    FOR VALUES FROM ('2027-02-01 00:00:00+00') TO ('2027-03-01 00:00:00+00');

CREATE TABLE audit_logs_2027_03 PARTITION OF audit_logs
    FOR VALUES FROM ('2027-03-01 00:00:00+00') TO ('2027-04-01 00:00:00+00');

CREATE TABLE audit_logs_2027_04 PARTITION OF audit_logs
    FOR VALUES FROM ('2027-04-01 00:00:00+00') TO ('2027-05-01 00:00:00+00');

-- Default partition: catches any timestamp outside the defined monthly ranges.
-- Required to prevent INSERT failures on out-of-range timestamps (e.g. historical backfills
-- or future dates beyond 2027-04 until the next partition creation run).
CREATE TABLE audit_logs_default PARTITION OF audit_logs DEFAULT;
");

            // ── Step 5: Migrate all existing data ────────────────────────────────────────────
            // Rows from audit_logs_old will be routed to the correct monthly partition
            // by PostgreSQL partition routing based on their Timestamp value.
            migrationBuilder.Sql(@"
INSERT INTO audit_logs (""LogId"", ""UserId"", ""Action"", ""ResourceType"", ""ResourceId"", ""Timestamp"", ""IpAddress"", ""UserAgent"")
SELECT ""LogId"", ""UserId"", ""Action"", ""ResourceType"", ""ResourceId"", ""Timestamp"", ""IpAddress"", ""UserAgent""
FROM audit_logs_old;
");

            // ── Step 6: Drop old table (data now safely in partitioned table) ────────────────
            migrationBuilder.Sql(@"DROP TABLE audit_logs_old;");

            // ── Step 7: Re-add FK on new partitioned table (supported PG 12+) ────────────────
            migrationBuilder.Sql(@"
ALTER TABLE audit_logs
    ADD CONSTRAINT ""FK_audit_logs_asp_net_users_UserId""
    FOREIGN KEY (""UserId"") REFERENCES asp_net_users (""Id"") ON DELETE SET NULL;
");

            // ── Step 8: Composite indexes for AuditLogQueryService query patterns ─────────────
            // Indexes on the parent partitioned table are automatically propagated to all
            // child partitions by PostgreSQL 16 (no per-partition CREATE INDEX needed).
            // NOTE: CONCURRENTLY is not permitted inside a transaction block — use regular
            //       CREATE INDEX here. For zero-downtime environments, run CONCURRENTLY
            //       manually after migration using create_audit_partitions.sql (NFR-021).
            migrationBuilder.Sql(@"
-- User + timestamp: primary query pattern for per-user audit trail views (AC-3).
CREATE INDEX ix_audit_logs_user_id_timestamp
    ON audit_logs (""UserId"", ""Timestamp"" DESC);

-- Entity type + timestamp: supports filtering by ResourceType with date ordering (AC-3).
CREATE INDEX ix_audit_logs_entity_type_timestamp
    ON audit_logs (""ResourceType"", ""Timestamp"" DESC);

-- Action + timestamp: supports filtering by action type with date ordering (AC-3).
CREATE INDEX ix_audit_logs_action_timestamp
    ON audit_logs (""Action"", ""Timestamp"" DESC);

-- Timestamp only: pure date-range queries and partition pruning (TR-013).
CREATE INDEX ix_audit_logs_timestamp
    ON audit_logs (""Timestamp"" DESC);
");

            // ── Step 9: Immutability trigger function ─────────────────────────────────────────
            // ERRCODE 55000 = object_not_in_prerequisite_state — appropriate for immutability
            // violations since the audit log is not in a state that allows modification.
            // This enforces AC-2 at the database level independent of the application layer.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION fn_audit_logs_prevent_modification()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION
        'Audit log entries are immutable. UPDATE and DELETE operations are prohibited per HIPAA compliance (DR-016, NFR-012).'
        USING ERRCODE = '55000';
    RETURN NULL;
END;
$$;
");

            // ── Step 10: Attach BEFORE triggers to parent table ───────────────────────────────
            // Triggers defined on the parent partitioned table are automatically inherited by
            // all existing and future child partitions (PostgreSQL 10+ behaviour).
            // INSERT is intentionally NOT restricted — append-only writes must succeed (AC-1).
            migrationBuilder.Sql(@"
CREATE TRIGGER trg_audit_logs_prevent_update
    BEFORE UPDATE ON audit_logs
    FOR EACH ROW EXECUTE FUNCTION fn_audit_logs_prevent_modification();

CREATE TRIGGER trg_audit_logs_prevent_delete
    BEFORE DELETE ON audit_logs
    FOR EACH ROW EXECUTE FUNCTION fn_audit_logs_prevent_modification();
");

            // ── Step 11: HIPAA retention documentation ────────────────────────────────────────
            migrationBuilder.Sql(@"
COMMENT ON TABLE audit_logs IS
    'HIPAA Audit Trail — 7-year minimum retention per 45 CFR §164.312(b) (DR-016, AC-4, US_064). '
    'DO NOT drop partitions less than 7 years old. '
    'Use fn_audit_logs_safe_archive(cutoff_date) in Server/Data/Scripts/audit_retention_policy.sql '
    'to safely detach aged partitions after the 7-year threshold is reached.';
");

            // ── Step 12: Revoke UPDATE/DELETE from application role (third layer of defence) ──
            // Layer 1: IAuditLogService interface exposes no update/delete methods (service layer).
            // Layer 2: BEFORE triggers raise EXCEPTION on any UPDATE/DELETE attempt (DB trigger).
            // Layer 3: PostgreSQL REVOKE prevents the application role from even attempting DML.
            //
            // NOTE: The provision-database.sql script grants full DML on ALL TABLES. These
            //       explicit REVOKEs override that grant specifically for audit_logs and its
            //       partitions. If provision-database.sql is re-run, these REVOKEs must be
            //       re-applied (run this migration again or execute the REVOKE statements manually).
            migrationBuilder.Sql(@"
-- Parent table
REVOKE UPDATE, DELETE ON audit_logs FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs FROM PUBLIC;
GRANT INSERT, SELECT ON audit_logs TO upacip_app;

-- Child partitions (REVOKE must be applied explicitly to each partition)
REVOKE UPDATE, DELETE ON audit_logs_2026_04 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2026_05 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2026_06 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2026_07 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2026_08 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2026_09 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2026_10 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2026_11 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2026_12 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2027_01 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2027_02 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2027_03 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_2027_04 FROM upacip_app;
REVOKE UPDATE, DELETE ON audit_logs_default FROM upacip_app;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ── Down Step 1: Restore permissions first (before dropping triggers) ─────────────
            migrationBuilder.Sql(@"
GRANT UPDATE, DELETE ON audit_logs TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2026_04 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2026_05 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2026_06 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2026_07 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2026_08 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2026_09 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2026_10 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2026_11 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2026_12 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2027_01 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2027_02 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2027_03 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_2027_04 TO upacip_app;
GRANT UPDATE, DELETE ON audit_logs_default TO upacip_app;
");

            // ── Down Step 2: Drop triggers ─────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS trg_audit_logs_prevent_delete ON audit_logs;
DROP TRIGGER IF EXISTS trg_audit_logs_prevent_update ON audit_logs;
");

            // ── Down Step 3: Drop trigger function ────────────────────────────────────────────
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS fn_audit_logs_prevent_modification();");

            // ── Down Step 4: Create non-partitioned restore table ─────────────────────────────
            migrationBuilder.Sql(@"
CREATE TABLE audit_logs_restore (
    ""LogId""        uuid                       NOT NULL,
    ""UserId""       uuid,
    ""Action""       character varying(50)      NOT NULL,
    ""ResourceType"" character varying(100)     NOT NULL,
    ""ResourceId""   uuid,
    ""Timestamp""    timestamp with time zone   NOT NULL,
    ""IpAddress""    character varying(45)      NOT NULL,
    ""UserAgent""    character varying(500)     NOT NULL
);
");

            // ── Down Step 5: Copy all data from partitioned table ─────────────────────────────
            migrationBuilder.Sql(@"
INSERT INTO audit_logs_restore
SELECT ""LogId"", ""UserId"", ""Action"", ""ResourceType"", ""ResourceId"", ""Timestamp"", ""IpAddress"", ""UserAgent""
FROM audit_logs;
");

            // ── Down Step 6: Drop FK from partitioned table before dropping the table ─────────
            migrationBuilder.Sql(@"
ALTER TABLE audit_logs DROP CONSTRAINT IF EXISTS ""FK_audit_logs_asp_net_users_UserId"";
");

            // ── Down Step 7: Drop partitioned table (cascades to all child partitions) ─────────
            migrationBuilder.Sql(@"DROP TABLE audit_logs;");

            // ── Down Step 8: Promote restore table to canonical audit_logs ────────────────────
            migrationBuilder.Sql(@"ALTER TABLE audit_logs_restore RENAME TO audit_logs;");

            // ── Down Step 9: Restore PK and FK constraints ────────────────────────────────────
            migrationBuilder.Sql(@"
ALTER TABLE audit_logs
    ADD CONSTRAINT ""PK_audit_logs"" PRIMARY KEY (""LogId"");

ALTER TABLE audit_logs
    ADD CONSTRAINT ""FK_audit_logs_asp_net_users_UserId""
    FOREIGN KEY (""UserId"") REFERENCES asp_net_users (""Id"") ON DELETE SET NULL;
");

            // ── Down Step 10: Recreate original indexes ───────────────────────────────────────
            migrationBuilder.Sql(@"
CREATE INDEX ix_audit_logs_user_id_timestamp  ON audit_logs (""UserId"",  ""Timestamp"");
CREATE INDEX ix_audit_logs_action_timestamp   ON audit_logs (""Action"",  ""Timestamp"");
CREATE INDEX ix_audit_logs_timestamp          ON audit_logs (""Timestamp"" DESC);
");

            // ── Down Step 11: Restore original permissions ────────────────────────────────────
            migrationBuilder.Sql(@"GRANT SELECT, INSERT, UPDATE, DELETE ON audit_logs TO upacip_app;");
        }
    }
}
