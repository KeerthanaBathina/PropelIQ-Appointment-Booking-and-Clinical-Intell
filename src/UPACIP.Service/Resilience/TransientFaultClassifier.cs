using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace UPACIP.Service.Resilience;

/// <summary>
/// Classifies exceptions as transient (retryable) or permanent (non-retryable) to drive
/// Polly retry decisions (US_095, AC-2, NFR-032, NFR-023).
///
/// <para>
/// Transient failures are temporary conditions that resolve without code changes:
/// network timeouts, serialization conflicts, temporary unavailability.
/// Permanent failures require human or configuration intervention: constraint violations,
/// authentication errors, missing resources.
/// </para>
///
/// <para>
/// Registered as <see cref="ITransientFaultClassifier"/> (Singleton). Thread-safe — all
/// methods are pure functions with no mutable state.
/// </para>
/// </summary>
public interface ITransientFaultClassifier
{
    /// <summary>Returns true when <paramref name="ex"/> is a transient database error.</summary>
    bool IsTransientDatabaseException(Exception ex);

    /// <summary>Returns true when <paramref name="ex"/> is a transient HTTP/network error.</summary>
    bool IsTransientHttpException(Exception ex);

    /// <summary>Returns true when <paramref name="ex"/> is any transient error (DB, HTTP, or IO).</summary>
    bool IsTransient(Exception ex);
}

/// <inheritdoc/>
public sealed class TransientFaultClassifier : ITransientFaultClassifier
{
    // ── PostgreSQL transient SqlState codes ───────────────────────────────────
    // Per: https://www.postgresql.org/docs/16/errcodes-appendix.html
    private static readonly HashSet<string> TransientPostgresSqlStates = new(StringComparer.Ordinal)
    {
        "57014",  // query_canceled (e.g. statement_timeout, lock_timeout)
        "40001",  // serialization_failure
        "40P01",  // deadlock_detected
        "08006",  // connection_failure
        "08001",  // sqlclient_unable_to_establish_sqlconnection
        "08004",  // sqlserver_rejected_establishment_of_sqlconnection
        "08007",  // transaction_resolution_unknown
        "53300",  // too_many_connections (connection pool exhausted)
        "53400",  // configuration_limit_exceeded
    };

    // Permanent PostgreSQL SqlState codes — MUST NOT be retried.
    private static readonly HashSet<string> PermanentPostgresSqlStates = new(StringComparer.Ordinal)
    {
        "23505",  // unique_violation
        "23503",  // foreign_key_violation
        "23502",  // not_null_violation
        "23514",  // check_violation
        "42P01",  // undefined_table
        "42703",  // undefined_column
        "42883",  // undefined_function
        "28000",  // invalid_authorization_specification (wrong password — permanent)
        "28P01",  // invalid_password
    };

    // ── Transient HTTP status codes ───────────────────────────────────────────
    private static readonly HashSet<HttpStatusCode> TransientHttpStatusCodes = new()
    {
        HttpStatusCode.RequestTimeout,          // 408
        HttpStatusCode.TooManyRequests,         // 429
        HttpStatusCode.InternalServerError,     // 500
        HttpStatusCode.BadGateway,              // 502
        HttpStatusCode.ServiceUnavailable,      // 503
        HttpStatusCode.GatewayTimeout,          // 504
    };

    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsTransientDatabaseException(Exception ex)
    {
        return ex switch
        {
            // NpgsqlException: use Npgsql's own IsTransient flag as primary signal.
            NpgsqlException npgsql when npgsql.IsTransient => true,

            // PostgresException: check SqlState explicitly.
            PostgresException pg when IsTransientSqlState(pg.SqlState) => true,

            // EF Core wraps NpgsqlException in DbUpdateException.
            // Unwrap and re-evaluate.
            Microsoft.EntityFrameworkCore.DbUpdateException dbu
                when dbu.InnerException is not null
                => IsTransientDatabaseException(dbu.InnerException),

            // Generic timeout (e.g. EF Core command timeout, CancellationToken from timeout).
            TimeoutException => true,

            _ => false,
        };
    }

    /// <inheritdoc/>
    public bool IsTransientHttpException(Exception ex)
    {
        return ex switch
        {
            // HttpRequestException with a transient status code.
            HttpRequestException http when http.StatusCode.HasValue
                && TransientHttpStatusCodes.Contains(http.StatusCode.Value) => true,

            // HttpRequestException with no status code = connection failure (transient).
            HttpRequestException http when !http.StatusCode.HasValue => true,

            // TaskCanceledException wrapping a TimeoutException = HTTP client timeout.
            TaskCanceledException tce when tce.InnerException is TimeoutException => true,

            _ => false,
        };
    }

    /// <inheritdoc/>
    public bool IsTransient(Exception ex)
    {
        if (IsTransientDatabaseException(ex)) return true;
        if (IsTransientHttpException(ex)) return true;

        return ex switch
        {
            // Network I/O error (e.g. connection reset by peer).
            IOException => true,
            // Socket-level connection errors.
            SocketException => true,
            _ => false,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsTransientSqlState(string? sqlState)
    {
        if (sqlState is null) return false;
        if (PermanentPostgresSqlStates.Contains(sqlState)) return false;
        return TransientPostgresSqlStates.Contains(sqlState);
    }
}
