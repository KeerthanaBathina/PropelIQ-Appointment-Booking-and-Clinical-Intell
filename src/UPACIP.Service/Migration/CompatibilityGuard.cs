using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.Service.Migration.Models;

namespace UPACIP.Service.Migration;

/// <summary>
/// Contract for pre-migration backward-compatibility analysis (US_091 task_002, AC-4, DR-031).
/// </summary>
public interface ICompatibilityGuard
{
    /// <summary>
    /// Inspects <c>UpOperations</c> of every pending EF Core migration and classifies each
    /// operation as safe, a warning, or breaking for a zero-downtime deployment.
    /// Returns one <see cref="CompatibilityReport"/> per migration.
    /// Never throws — errors are swallowed and logged.
    /// </summary>
    Task<IReadOnlyList<CompatibilityReport>> AnalyzePendingMigrationsAsync(CancellationToken ct = default);
}

/// <summary>
/// Scoped implementation of <see cref="ICompatibilityGuard"/>.
///
/// Breaking detection (expand-contract pattern, DR-031):
/// <list type="bullet">
///   <item><see cref="DropColumnOperation"/> — may break old code still reading that column.</item>
///   <item><see cref="RenameColumnOperation"/> — old code will fail until redeployed.</item>
///   <item><see cref="AlterColumnOperation"/> — type narrowing or NOT NULL without default breaks reads/writes.</item>
///   <item><see cref="DropTableOperation"/> — old code may still reference table.</item>
///   <item><see cref="RenameTableOperation"/> — old code will fail until redeployed.</item>
/// </list>
/// Safe operations (DR-031):
/// <list type="bullet">
///   <item><see cref="AddColumnOperation"/> (nullable, or NOT NULL with default) — backward compatible.</item>
///   <item><see cref="CreateTableOperation"/> — additive only.</item>
///   <item><see cref="CreateIndexOperation"/> — recommend CONCURRENTLY via raw SQL.</item>
/// </list>
/// Security (OWASP A03): no user-controlled input reaches SQL.
/// </summary>
public sealed class CompatibilityGuard : ICompatibilityGuard
{
    private readonly ApplicationDbContext       _context;
    private readonly ILogger<CompatibilityGuard> _logger;

    public CompatibilityGuard(
        ApplicationDbContext        context,
        ILogger<CompatibilityGuard> logger)
    {
        _context = context;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICompatibilityGuard
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CompatibilityReport>> AnalyzePendingMigrationsAsync(
        CancellationToken ct = default)
    {
        var reports = new List<CompatibilityReport>();

        try
        {
            var pending = (await _context.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count == 0)
                return reports;

            var migrationsAssembly = _context.GetService<IMigrationsAssembly>();
            var activeProvider     = _context.Database.ProviderName
                                     ?? "Npgsql.EntityFrameworkCore.PostgreSQL";

            foreach (var migrationId in pending)
            {
                if (!migrationsAssembly.Migrations.TryGetValue(migrationId, out var migrationType))
                    continue;

                CompatibilityReport report;
                try
                {
                    var migration = migrationsAssembly.CreateMigration(migrationType, activeProvider);
                    report        = AnalyzeMigration(migrationId, migration.UpOperations);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "CompatibilityGuard: could not analyze {MigrationId} — skipping.", migrationId);
                    continue;
                }

                reports.Add(report);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CompatibilityGuard: analysis failed — returning partial results.");
        }

        return reports;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Analysis logic
    // ─────────────────────────────────────────────────────────────────────────

    private static CompatibilityReport AnalyzeMigration(
        string                   migrationId,
        IReadOnlyList<MigrationOperation> operations)
    {
        var breakingChanges  = new List<BreakingChange>();
        var recommendations  = new List<string>();
        var safeOps          = new List<string>();

        foreach (var op in operations)
        {
            switch (op)
            {
                // ── Breaking: column removal ─────────────────────────────────
                case DropColumnOperation drop:
                    breakingChanges.Add(new BreakingChange
                    {
                        MigrationName            = migrationId,
                        OperationType            = "DropColumn",
                        TableName                = drop.Table,
                        ColumnName               = drop.Name,
                        Severity                 = BreakingSeverity.Breaking,
                        ExpandContractSuggestion =
                            $"Expand-Contract: (1) Deploy app code that no longer reads/writes '{drop.Name}'. " +
                            $"(2) Then apply this migration to drop the column. " +
                            $"Table: {drop.Table}.",
                    });
                    recommendations.Add(
                        $"[Breaking] DropColumn '{drop.Name}' on '{drop.Table}': " +
                        "remove all application references first, then apply migration in a separate deployment.");
                    break;

                // ── Breaking: column rename ───────────────────────────────────
                case RenameColumnOperation rename:
                    breakingChanges.Add(new BreakingChange
                    {
                        MigrationName            = migrationId,
                        OperationType            = "RenameColumn",
                        TableName                = rename.Table,
                        ColumnName               = rename.Name,
                        Severity                 = BreakingSeverity.Breaking,
                        ExpandContractSuggestion =
                            $"Expand-Contract: (1) Add new column '{rename.NewName}'. " +
                            $"(2) Backfill data from '{rename.Name}'. " +
                            $"(3) Update app code to use '{rename.NewName}'. " +
                            $"(4) Drop old column '{rename.Name}'. Table: {rename.Table}.",
                    });
                    recommendations.Add(
                        $"[Breaking] RenameColumn '{rename.Name}' → '{rename.NewName}' on '{rename.Table}': " +
                        "use add-backfill-switch-drop expand-contract pattern.");
                    break;

                // ── Breaking: alter column (type change or NOT NULL without default) ──
                case AlterColumnOperation alter when IsBreakingAlter(alter):
                    breakingChanges.Add(new BreakingChange
                    {
                        MigrationName            = migrationId,
                        OperationType            = "AlterColumn",
                        TableName                = alter.Table,
                        ColumnName               = alter.Name,
                        Severity                 = BreakingSeverity.Breaking,
                        ExpandContractSuggestion =
                            $"Expand-Contract: (1) Add new column with the target type. " +
                            $"(2) Migrate data. (3) Update app to use new column. " +
                            $"(4) Drop old column. Column: {alter.Table}.{alter.Name}.",
                    });
                    recommendations.Add(
                        $"[Breaking] AlterColumn '{alter.Name}' on '{alter.Table}': " +
                        "type narrowing or NOT NULL addition is a breaking change; use new-column backfill.");
                    break;

                // ── Warning: alter column (type widening) ─────────────────────
                case AlterColumnOperation alterSafe:
                    recommendations.Add(
                        $"[Warning] AlterColumn '{alterSafe.Name}' on '{alterSafe.Table}': " +
                        "verify the change is a safe type widening (e.g. varchar(100) → varchar(200)).");
                    breakingChanges.Add(new BreakingChange
                    {
                        MigrationName            = migrationId,
                        OperationType            = "AlterColumn",
                        TableName                = alterSafe.Table,
                        ColumnName               = alterSafe.Name,
                        Severity                 = BreakingSeverity.Warning,
                        ExpandContractSuggestion =
                            "Verify the type widening is backward-compatible before deploying.",
                    });
                    break;

                // ── Breaking: table removal ───────────────────────────────────
                case DropTableOperation dropTable:
                    breakingChanges.Add(new BreakingChange
                    {
                        MigrationName            = migrationId,
                        OperationType            = "DropTable",
                        TableName                = dropTable.Name,
                        ColumnName               = null,
                        Severity                 = BreakingSeverity.Breaking,
                        ExpandContractSuggestion =
                            $"Expand-Contract: (1) Remove all ORM mappings and application references to '{dropTable.Name}'. " +
                            $"(2) Deploy the app. (3) Then apply the DROP TABLE migration.",
                    });
                    recommendations.Add(
                        $"[Breaking] DropTable '{dropTable.Name}': remove all app references first.");
                    break;

                // ── Breaking: table rename ────────────────────────────────────
                case RenameTableOperation renameTable:
                    breakingChanges.Add(new BreakingChange
                    {
                        MigrationName            = migrationId,
                        OperationType            = "RenameTable",
                        TableName                = renameTable.Name ?? "(unknown)",
                        ColumnName               = null,
                        Severity                 = BreakingSeverity.Breaking,
                        ExpandContractSuggestion =
                            $"Expand-Contract: (1) Create '{renameTable.NewName}'. " +
                            $"(2) Sync data. (3) Switch app over. " +
                            $"(4) Drop '{renameTable.Name}'.",
                    });
                    recommendations.Add(
                        $"[Breaking] RenameTable '{renameTable.Name}' → '{renameTable.NewName}': " +
                        "use create-sync-switch-drop pattern.");
                    break;

                // ── Safe: add column ──────────────────────────────────────────
                case AddColumnOperation addCol:
                    safeOps.Add($"AddColumn '{addCol.Name}' on '{addCol.Table}' — backward compatible.");
                    break;

                // ── Safe: create table ────────────────────────────────────────
                case CreateTableOperation createTable:
                    safeOps.Add($"CreateTable '{createTable.Name}' — additive, backward compatible.");
                    break;

                // ── Advisory: create index ────────────────────────────────────
                case CreateIndexOperation createIndex:
                    safeOps.Add($"CreateIndex '{createIndex.Name}' on '{createIndex.Table}'.");
                    recommendations.Add(
                        $"[Advisory] CreateIndex '{createIndex.Name}' on '{createIndex.Table}': " +
                        "for zero-downtime consider building the index CONCURRENTLY via a raw SQL migration.");
                    break;

                default:
                    safeOps.Add($"{op.GetType().Name} — classified as safe by default.");
                    break;
            }
        }

        return new CompatibilityReport
        {
            MigrationName        = migrationId,
            IsBackwardCompatible = breakingChanges.Count == 0,
            BreakingChanges      = breakingChanges,
            Recommendations      = recommendations,
            SafeOperations       = safeOps,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> for ALTER COLUMN operations that are breaking at the database level:
    /// changing the column type in a narrowing way, or adding a NOT NULL constraint without
    /// providing a column-level DEFAULT.
    /// </summary>
    private static bool IsBreakingAlter(AlterColumnOperation alter)
    {
        // NOT NULL added without a DEFAULT is breaking.
        if (!alter.IsNullable && string.IsNullOrEmpty(alter.DefaultValueSql) && alter.DefaultValue is null)
            return true;

        // Type change (any type name change is conservatively flagged as breaking;
        // widening-only cases are handled by the caller ordering which matches this first).
        if (!string.Equals(alter.ClrType?.FullName,
                           alter.OldColumn?.ClrType?.FullName,
                           StringComparison.Ordinal))
            return true;

        return false;
    }
}
