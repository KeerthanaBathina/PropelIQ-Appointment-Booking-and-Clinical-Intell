using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Adds security-specific audit schema changes to support HIPAA-compliant security event
    /// tracking and efficient querying (US_065 TASK_003, DR-016, NFR-016, FR-093).
    ///
    /// Changes applied by <see cref="Up"/>:
    ///   1. Create partial (filtered) index <c>ix_audit_logs_security_events</c> on
    ///      <c>(UserId, Timestamp DESC)</c> filtered to the four security-critical action types:
    ///      <c>FailedLogin</c>, <c>AccountLocked</c>, <c>SessionReplaced</c>,
    ///      <c>AdminManualUnlock</c>.
    ///      This enables fast per-user security timeline queries for compliance reporting
    ///      without scanning the full audit log partitions.
    ///
    /// Note on <c>AdminManualUnlock</c> action value:
    ///   The <c>Action</c> column is a <c>character varying(50)</c> string (EF Core
    ///   <c>HasConversion&lt;string&gt;()</c> stores C# enum member names). Adding a new
    ///   <c>AuditAction</c> enum value does not require a schema migration — the column
    ///   already accepts any string up to 50 characters. No DDL is needed for the new value.
    ///
    /// Note on partial index and partitioned table:
    ///   PostgreSQL 11+ supports partial indexes on partitioned parent tables and
    ///   automatically propagates them to existing and future child partitions.
    ///   <c>audit_logs</c> is partitioned by RANGE on <c>Timestamp</c> (see
    ///   <c>20260427000001_AddAuditLogPartitioningAndImmutability</c>).
    ///
    /// Note on <c>CREATE INDEX CONCURRENTLY</c>:
    ///   Cannot be used inside a transaction block. Regular <c>CREATE INDEX</c> is used here.
    ///   For zero-downtime environments, run CONCURRENTLY manually after migration (NFR-021).
    ///
    /// <see cref="Down"/> drops the index to support full rollback (DR-029).
    /// </summary>
    public partial class AddSecurityAuditSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Filtered index for fast per-user security event queries (US_065 TASK_003) ──────
            // Covers FailedLogin, AccountLocked, SessionReplaced, and AdminManualUnlock.
            // Filter uses the stored string representations of the AuditAction enum values
            // (EF Core HasConversion<string>() stores the C# enum member name verbatim).
            //
            // Query patterns optimised:
            //   SELECT * FROM audit_logs WHERE "UserId" = @uid
            //     AND "Action" IN ('FailedLogin','AccountLocked','SessionReplaced','AdminManualUnlock')
            //     ORDER BY "Timestamp" DESC
            //   — Used by HIPAA security event reports and account lockout recovery verification.
            //
            // Index is created on the partitioned parent table; PostgreSQL 11+ propagates it
            // automatically to all existing and future monthly child partitions.
            migrationBuilder.Sql(@"
CREATE INDEX ix_audit_logs_security_events
    ON audit_logs (""UserId"", ""Timestamp"" DESC)
    WHERE ""Action"" IN ('FailedLogin', 'AccountLocked', 'SessionReplaced', 'AdminManualUnlock');
");

            migrationBuilder.Sql(@"
COMMENT ON INDEX ix_audit_logs_security_events IS
    'Partial index for HIPAA security event compliance queries (US_065 TASK_003, DR-016). '
    'Covers FailedLogin, AccountLocked, SessionReplaced, AdminManualUnlock action types. '
    'Propagated to all audit_logs monthly child partitions by PostgreSQL 11+.';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the filtered security events index.
            // The COMMENT is attached to the index and will be dropped automatically.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_audit_logs_security_events;");
        }
    }
}
