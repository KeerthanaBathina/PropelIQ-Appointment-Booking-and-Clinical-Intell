using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Data;
using System.Diagnostics;

namespace UPACIP.DataAccess.Seeding;

/// <summary>
/// Seeds the database by reading and executing <c>scripts/seed-data.sql</c> via a
/// raw <see cref="NpgsqlConnection"/> obtained from the application's connection pool.
///
/// The SQL script is self-idempotent (TRUNCATE CASCADE + deterministic UUIDs).
/// This seeder adds an additional application-level guard: it will not execute
/// in a Production environment (<see cref="IHostEnvironment.IsProduction()"/>).
///
/// Execution runs inside a single database transaction; failures trigger a rollback
/// so the database is never left in a partially-seeded state.
/// </summary>
public sealed class SqlFileDataSeeder : IDataSeeder
{
    private readonly ApplicationDbContext  _dbContext;
    private readonly IHostEnvironment      _environment;
    private readonly ILogger<SqlFileDataSeeder> _logger;

    public SqlFileDataSeeder(
        ApplicationDbContext dbContext,
        IHostEnvironment environment,
        ILogger<SqlFileDataSeeder> logger)
    {
        _dbContext   = dbContext;
        _environment = environment;
        _logger      = logger;
    }

    /// <inheritdoc/>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // ── Production safety guard (application layer) ───────────────────────
        // The SQL script has its own PL/pgSQL guard; this is a second line of
        // defence so the file is never even read in Production.
        if (_environment.IsProduction())
        {
            _logger.LogWarning(
                "SqlFileDataSeeder: Seeding is disabled in the Production environment. " +
                "Set ASPNETCORE_ENVIRONMENT to Development or Staging to run seeding.");
            return;
        }

        var sqlFilePath = ResolveSqlFilePath();
        _logger.LogInformation("SqlFileDataSeeder: Reading seed file from {FilePath}", sqlFilePath);

        var sql = await File.ReadAllTextAsync(sqlFilePath, cancellationToken);

        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var wasOpen    = connection.State == ConnectionState.Open;

        if (!wasOpen)
            await connection.OpenAsync(cancellationToken);

        var sw = Stopwatch.StartNew();
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction)
                {
                    // 2-minute timeout — the seed script inserts ~200 rows which is fast,
                    // but on first run (cold connection pool) allow generous headroom.
                    CommandTimeout = 120
                };

                await command.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                sw.Stop();
                _logger.LogInformation(
                    "SqlFileDataSeeder: Seed completed successfully in {ElapsedMs}ms.",
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogError(ex,
                    "SqlFileDataSeeder: Seed failed — transaction rolled back. " +
                    "Database state is unchanged.");
                throw;
            }
        }
        finally
        {
            if (!wasOpen)
                await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Locates <c>scripts/seed-data.sql</c> relative to the application content root.
    ///
    /// Search order:
    ///   1. Two levels above ContentRootPath (solution root / scripts/) — used when
    ///      running via <c>dotnet run --project src/UPACIP.Api</c> from the solution root.
    ///   2. ContentRootPath / scripts/ — used when scripts are co-located with the app
    ///      (e.g., Docker container where scripts/ is copied alongside the publish output).
    /// </summary>
    private string ResolveSqlFilePath()
    {
        const string relPath = "scripts/seed-data.sql";

        // Option 1: resolve upward from the project directory to the solution root
        var solutionRootPath = Path.GetFullPath(
            Path.Combine(_environment.ContentRootPath, "..", "..", relPath));

        if (File.Exists(solutionRootPath))
            return solutionRootPath;

        // Option 2: co-located with the app
        var localPath = Path.GetFullPath(
            Path.Combine(_environment.ContentRootPath, relPath));

        if (File.Exists(localPath))
            return localPath;

        throw new FileNotFoundException(
            $"seed-data.sql not found. Checked the following locations:\n" +
            $"  {solutionRootPath}\n" +
            $"  {localPath}\n" +
            "Ensure scripts/seed-data.sql exists at the solution root, or set ContentRootPath correctly.");
    }
}
