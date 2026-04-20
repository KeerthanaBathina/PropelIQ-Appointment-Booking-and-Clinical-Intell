namespace UPACIP.DataAccess.Seeding;

/// <summary>
/// Abstraction for development/staging data seeding strategies.
/// Implementations must be idempotent — calling <see cref="SeedAsync"/> multiple
/// times must produce the same database state each time (no duplicates).
///
/// Production guard: implementations MUST check the current environment and
/// return without action if running in Production (defense-in-depth alongside
/// the SQL-level guard in <c>scripts/seed-data.sql</c>).
/// </summary>
public interface IDataSeeder
{
    /// <summary>
    /// Seeds the database with development/QA data.
    /// Implementations are responsible for checking the environment and
    /// performing idempotent resets (e.g., TRUNCATE CASCADE) before inserting.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
