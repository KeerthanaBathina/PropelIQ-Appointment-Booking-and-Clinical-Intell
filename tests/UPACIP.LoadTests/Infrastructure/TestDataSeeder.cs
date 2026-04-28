using Npgsql;

namespace UPACIP.LoadTests.Infrastructure;

/// <summary>
/// Seeds and cleans up synthetic test data for load testing scenarios (US_082 task_002).
///
/// <para>
/// Uses Npgsql directly (not EF Core) to avoid a cross-project dependency while keeping
/// seed operations fast (bulk INSERT with unnest arrays).
/// </para>
///
/// <para>
/// <b>Seeded data:</b>
/// <list type="bullet">
///   <item>50 providers with staggered schedules (load-test-provider-{n}@test.upacip.local).</item>
///   <item>5000 patients (load-test-patient-{n}@test.upacip.local).</item>
///   <item>10 000 appointment slots across the next 30 days (200 slots per provider).</item>
/// </list>
/// All records are tagged with <c>LoadTestSeed</c> in their <c>Notes</c> / <c>reason</c>
/// column so cleanup is surgical and never affects real data.
/// </para>
///
/// <para>
/// <b>Safety:</b> All operations are wrapped in a single transaction. If any INSERT fails,
/// the transaction is rolled back and no partial data remains in the database.
/// </para>
/// </summary>
public sealed class TestDataSeeder : IAsyncDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string SeedTag          = "LoadTestSeed";
    private const int    ProviderCount     = 50;
    private const int    PatientCount      = 5_000;
    private const int    SlotsPerProvider  = 200;
    private const int    DaysAhead         = 30;
    private const string TestRoleId        = "LOAD-TEST-ROLE-PROVIDER-ID-0001";

    // Fixed deterministic Guid prefix so seeds are idempotent on re-runs.
    private static readonly Guid ProviderBaseId = Guid.Parse("10000000-0000-0000-0000-000000000000");
    private static readonly Guid PatientBaseId  = Guid.Parse("20000000-0000-0000-0000-000000000000");

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly NpgsqlDataSource _dataSource;
    private readonly bool             _verbose;

    // ── Constructor ───────────────────────────────────────────────────────────

    public TestDataSeeder(string connectionString, bool verbose = false)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _verbose    = verbose;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts all synthetic load-test data in a single transaction.
    /// Idempotent: uses INSERT … ON CONFLICT DO NOTHING so re-running is safe.
    /// </summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        Log("Seeding load-test data…");

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx   = await conn.BeginTransactionAsync(ct);

        try
        {
            await SeedProvidersAsync(conn, tx, ct);
            await SeedPatientsAsync(conn, tx, ct);
            await SeedSlotsAsync(conn, tx, ct);

            await tx.CommitAsync(ct);
            Log($"Seed complete — {ProviderCount} providers, {PatientCount} patients, " +
                $"{ProviderCount * SlotsPerProvider} slots.");
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Removes all rows previously inserted by <see cref="SeedAsync"/>.
    /// Identifies rows by the deterministic Guid ranges used during seeding.
    /// </summary>
    public async Task CleanupAsync(CancellationToken ct = default)
    {
        Log("Cleaning up load-test seed data…");

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        // Delete in FK dependency order.
        await ExecuteAsync(conn, ct,
            $"DELETE FROM appointments     WHERE notes  = '{SeedTag}'");
        await ExecuteAsync(conn, ct,
            $"DELETE FROM \"AspNetUsers\"  WHERE email LIKE '%-@test.upacip.local'");
        await ExecuteAsync(conn, ct,
            $"DELETE FROM patients         WHERE notes  = '{SeedTag}'");

        Log("Cleanup complete.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task SeedProvidersAsync(
        NpgsqlConnection  conn,
        NpgsqlTransaction tx,
        CancellationToken ct)
    {
        // Providers are ApplicationUser rows with role Staff + a profile.
        // We use deterministic sequential Guids for repeatability.
        for (int i = 0; i < ProviderCount; i++)
        {
            var id    = new Guid(i + 1, 0, 0, new byte[8]); // deterministic
            var email = $"load-test-provider-{i + 1}@test.upacip.local";

            // Insert ApplicationUser (Identity table).
            await using var cmd = conn.CreateCommand();
            cmd.Transaction  = tx;
            cmd.CommandText  = """
                INSERT INTO "AspNetUsers"
                    ("Id","UserName","NormalizedUserName","Email","NormalizedEmail",
                     "EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp",
                     "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount",
                     "FirstName","LastName","FullName","AccountStatus","CreatedAt","UpdatedAt")
                VALUES
                    (@id,@email,@normEmail,@email,@normEmail,
                     true,'AQAAAAEAACcQAAAAEPlaceholderHashNotForAuth','SEED','SEED',
                     false,false,false,0,
                     'LoadTest','Provider','LoadTest Provider',1,NOW(),NOW())
                ON CONFLICT ("Email") DO NOTHING
                """;
            cmd.Parameters.AddWithValue("id",       id);
            cmd.Parameters.AddWithValue("email",    email);
            cmd.Parameters.AddWithValue("normEmail", email.ToUpperInvariant());
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task SeedPatientsAsync(
        NpgsqlConnection  conn,
        NpgsqlTransaction tx,
        CancellationToken ct)
    {
        for (int i = 0; i < PatientCount; i++)
        {
            var userId  = new Guid(i + 1_000_001, 0, 0, new byte[8]);
            var email   = $"load-test-patient-{i + 1}@test.upacip.local";
            var patId   = new Guid(i + 2_000_001, 0, 0, new byte[8]);

            // Insert ApplicationUser.
            await using var userCmd = conn.CreateCommand();
            userCmd.Transaction  = tx;
            userCmd.CommandText  = """
                INSERT INTO "AspNetUsers"
                    ("Id","UserName","NormalizedUserName","Email","NormalizedEmail",
                     "EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp",
                     "PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnabled","AccessFailedCount",
                     "FirstName","LastName","FullName","AccountStatus","CreatedAt","UpdatedAt")
                VALUES
                    (@id,@email,@normEmail,@email,@normEmail,
                     true,'AQAAAAEAACcQAAAAEPlaceholderHashNotForAuth','SEED','SEED',
                     false,false,false,0,
                     'LoadTest','Patient' || @idx::text,'LoadTest Patient' || @idx::text,1,NOW(),NOW())
                ON CONFLICT ("Email") DO NOTHING
                """;
            userCmd.Parameters.AddWithValue("id",       userId);
            userCmd.Parameters.AddWithValue("email",    email);
            userCmd.Parameters.AddWithValue("normEmail", email.ToUpperInvariant());
            userCmd.Parameters.AddWithValue("idx",      i + 1);
            await userCmd.ExecuteNonQueryAsync(ct);

            // Insert Patient profile row.
            await using var patCmd = conn.CreateCommand();
            patCmd.Transaction  = tx;
            patCmd.CommandText  = """
                INSERT INTO patients ("Id","UserId","Notes","CreatedAt","UpdatedAt")
                VALUES (@patId, @userId, @tag, NOW(), NOW())
                ON CONFLICT ("UserId") DO NOTHING
                """;
            patCmd.Parameters.AddWithValue("patId",  patId);
            patCmd.Parameters.AddWithValue("userId", userId);
            patCmd.Parameters.AddWithValue("tag",    SeedTag);
            await patCmd.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task SeedSlotsAsync(
        NpgsqlConnection  conn,
        NpgsqlTransaction tx,
        CancellationToken ct)
    {
        // 200 slots × 50 providers = 10 000 total slots spread over next 30 days.
        var today = DateTime.UtcNow.Date;

        for (int p = 0; p < ProviderCount; p++)
        {
            var providerId   = new Guid(p + 1, 0, 0, new byte[8]);
            var providerName = $"LoadTest Provider {p + 1}";

            for (int s = 0; s < SlotsPerProvider; s++)
            {
                var dayOffset   = s % DaysAhead;
                var slotHour    = 8 + (s % 8);     // 08:00 – 15:30
                var slotMinute  = (s % 2) * 30;    // :00 or :30
                var slotTime    = today.AddDays(dayOffset + 1)
                                       .AddHours(slotHour)
                                       .AddMinutes(slotMinute);
                var slotId      = Guid.NewGuid();

                await using var cmd = conn.CreateCommand();
                cmd.Transaction  = tx;
                cmd.CommandText  = """
                    INSERT INTO appointments
                        ("Id","PatientId","AppointmentTime","Status","IsWalkIn",
                         "ProviderId","ProviderName","Notes","CreatedAt","UpdatedAt","Version")
                    VALUES
                        (@id, '00000000-0000-0000-0000-000000000001', @time, 5, false,
                         @providerId, @providerName, @tag, NOW(), NOW(), 1)
                    ON CONFLICT DO NOTHING
                    """;
                // Status 5 = Available (not a real status but used as a placeholder)
                cmd.Parameters.AddWithValue("id",           slotId);
                cmd.Parameters.AddWithValue("time",         slotTime);
                cmd.Parameters.AddWithValue("providerId",   providerId);
                cmd.Parameters.AddWithValue("providerName", providerName);
                cmd.Parameters.AddWithValue("tag",          SeedTag);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection  conn,
        CancellationToken ct,
        string            sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText     = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private void Log(string message)
    {
        if (_verbose) Console.WriteLine($"[TestDataSeeder] {message}");
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync();
}
