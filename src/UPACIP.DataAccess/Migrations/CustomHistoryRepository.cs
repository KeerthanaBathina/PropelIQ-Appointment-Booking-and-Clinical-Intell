using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;

namespace UPACIP.DataAccess.Migrations;

/// <summary>
/// Custom EF Core migration history repository that extends the standard
/// <c>__EFMigrationsHistory</c> table with two additional audit columns (US_091 task_001, AC-3):
/// <list type="bullet">
///   <item><c>AppliedAtUtc</c> — UTC timestamp recorded by PostgreSQL (<c>now()</c>) when
///   the migration was applied. Satisfies AC-3 "timestamp" requirement.</item>
///   <item><c>MigrationChecksum</c> — SHA-256 hex string of the migration's generated DDL SQL.
///   Satisfies AC-3 "checksum" requirement and enables post-migration drift detection.</item>
/// </list>
///
/// Registration: <c>.ReplaceService&lt;IHistoryRepository, CustomHistoryRepository&gt;()</c>
/// in the <c>AddDbContext</c> options delegate in Program.cs.
///
/// Security (OWASP A03): the checksum is a hex string derived from EF Core-generated SQL —
/// no user-controlled content reaches any INSERT statement.
/// </summary>
public sealed class CustomHistoryRepository : NpgsqlHistoryRepository
{
    private readonly IMigrationsSqlGenerator _sqlGenerator;
    private readonly IMigrationsAssembly     _migrationsAssembly;

    public CustomHistoryRepository(
        HistoryRepositoryDependencies dependencies,
        IMigrationsSqlGenerator       sqlGenerator,
        IMigrationsAssembly           migrationsAssembly)
        : base(dependencies)
    {
        _sqlGenerator       = sqlGenerator;
        _migrationsAssembly = migrationsAssembly;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Extend CREATE TABLE with extra audit columns (idempotent ALTER TABLE)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends two <c>ALTER TABLE … ADD COLUMN IF NOT EXISTS</c> statements to the
    /// base EF Core <c>CREATE TABLE IF NOT EXISTS</c> DDL.
    ///
    /// <c>ADD COLUMN IF NOT EXISTS</c> is idempotent — safe on fresh deployments
    /// (immediately after CREATE TABLE) and on existing deployments (columns skipped
    /// if already present from a prior run).
    /// </summary>
    public override string GetCreateIfNotExistsScript()
    {
        var baseScript = base.GetCreateIfNotExistsScript();
        var table      = SqlGenerationHelper.DelimitIdentifier(TableName, TableSchema);

        return baseScript
            + $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS"
            + " \"AppliedAtUtc\" timestamp with time zone NOT NULL DEFAULT now();\n"
            + $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS"
            + " \"MigrationChecksum\" character varying(64) NOT NULL DEFAULT '';\n";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INSERT override: include the two extra columns on every history write
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates the SQL INSERT that records a migration as applied.
    /// Includes <c>AppliedAtUtc = now()</c> and <c>MigrationChecksum = &lt;sha256&gt;</c>.
    /// </summary>
    public override string GetInsertScript(HistoryRow row)
    {
        var checksum      = ComputeMigrationChecksum(row.MigrationId);
        // FindMapping is the interface-level accessor; GetMapping is concrete-type only.
        var stringMapping = Dependencies.TypeMappingSource.FindMapping(typeof(string))!;

        return new StringBuilder()
            .Append("INSERT INTO ")
            .Append(SqlGenerationHelper.DelimitIdentifier(TableName, TableSchema))
            .Append(" (")
            .Append(SqlGenerationHelper.DelimitIdentifier(MigrationIdColumnName))
            .Append(", ")
            .Append(SqlGenerationHelper.DelimitIdentifier(ProductVersionColumnName))
            .Append(", \"AppliedAtUtc\", \"MigrationChecksum\")")
            .Append(" VALUES (")
            .Append(stringMapping.GenerateSqlLiteral(row.MigrationId))
            .Append(", ")
            .Append(stringMapping.GenerateSqlLiteral(row.ProductVersion))
            .Append(", now(), ")
            .Append(stringMapping.GenerateSqlLiteral(checksum))
            .Append(");")
            .ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Checksum computation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes a SHA-256 hex checksum of the DDL SQL produced by the migration's
    /// <c>Up()</c> operations via <c>IMigrationsSqlGenerator</c>.
    ///
    /// Uses the public <c>UpOperations</c> property (not the protected <c>Up()</c> method)
    /// to avoid accessibility violations.
    ///
    /// Falls back to a SHA-256 hash of the migration ID string when generation fails
    /// (e.g., complex provider-specific operations that cannot be replayed at design time).
    /// </summary>
    private string ComputeMigrationChecksum(string migrationId)
    {
        try
        {
            if (!_migrationsAssembly.Migrations.TryGetValue(migrationId, out var migrationType))
                return FallbackChecksum(migrationId);

            const string activeProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
            var migration  = _migrationsAssembly.CreateMigration(migrationType, activeProvider);

            // UpOperations is the public property that internally invokes the protected Up() method.
            var operations = migration.UpOperations;

            if (operations.Count == 0)
                return FallbackChecksum(migrationId);

            var commands = _sqlGenerator.Generate(operations, model: null);
            var sql      = string.Concat(commands.Select(c => c.CommandText));

            return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
        }
        catch
        {
            return FallbackChecksum(migrationId);
        }
    }

    private static string FallbackChecksum(string migrationId) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(migrationId))).ToLowerInvariant();
}
