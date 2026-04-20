using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UPACIP.Api.Models;

namespace UPACIP.Api.Middleware;

/// <summary>
/// Catches all unhandled exceptions in the pipeline and returns a structured JSON
/// ErrorResponse. Internal exception details (stack traces, inner messages) are
/// never surfaced to the client to prevent information disclosure (OWASP A05).
///
/// Database constraint violations are mapped to HTTP semantics:
///   - PostgreSQL 23505 (unique_violation)     → 409 Conflict
///   - PostgreSQL 23503 (foreign_key_violation) → 400 Bad Request
///   - PostgreSQL 23514 (check_violation)       → 400 Bad Request
///   - DbUpdateConcurrencyException             → 409 Conflict
/// </summary>
public sealed class GlobalExceptionHandlerMiddleware
{
    // PostgreSQL SQLSTATE codes
    private const string UniqueViolation      = "23505";
    private const string ForeignKeyViolation  = "23503";
    private const string CheckViolation       = "23514";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var correlationId = GetCorrelationId(context);
            _logger.LogWarning(
                ex,
                "Optimistic concurrency conflict. CorrelationId: {CorrelationId} Path: {Path}",
                correlationId, context.Request.Path);

            await WriteErrorResponseAsync(context,
                statusCode: (int)HttpStatusCode.Conflict,
                message:    "Conflict",
                detail:     "The record was modified by another user. Retrieve the latest version and retry.",
                correlationId: correlationId);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx)
        {
            var correlationId = GetCorrelationId(context);
            _logger.LogWarning(
                ex,
                "Database constraint violation SqlState={SqlState} Constraint={Constraint} CorrelationId={CorrelationId}",
                pgEx.SqlState, pgEx.ConstraintName, correlationId);

            var (statusCode, message) = pgEx.SqlState switch
            {
                UniqueViolation     => ((int)HttpStatusCode.Conflict,   "Duplicate record"),
                ForeignKeyViolation => ((int)HttpStatusCode.BadRequest,  "Referenced record does not exist"),
                CheckViolation      => ((int)HttpStatusCode.BadRequest,  "Data validation failed"),
                _                   => ((int)HttpStatusCode.BadRequest,  "Database constraint violation")
            };

            await WriteErrorResponseAsync(context,
                statusCode:    statusCode,
                message:       message,
                detail:        pgEx.ConstraintName,
                correlationId: correlationId);
        }
        catch (Exception ex)
        {
            var correlationId = GetCorrelationId(context);
            _logger.LogError(
                ex,
                "Unhandled exception. CorrelationId: {CorrelationId} Path: {Path}",
                correlationId, context.Request.Path);

            await WriteErrorResponseAsync(context,
                statusCode:    (int)HttpStatusCode.InternalServerError,
                message:       "An unexpected error occurred. Please try again later.",
                detail:        null,
                correlationId: correlationId);
        }
    }

    private static string GetCorrelationId(HttpContext context)
        => context.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
           ?? Guid.NewGuid().ToString();

    private static async Task WriteErrorResponseAsync(
        HttpContext context,
        int statusCode,
        string message,
        string? detail,
        string correlationId)
    {
        context.Response.StatusCode  = statusCode;
        context.Response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            StatusCode    = statusCode,
            Message       = message,
            Detail        = detail,
            CorrelationId = correlationId,
            Timestamp     = DateTimeOffset.UtcNow
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(errorResponse, SerializerOptions));
    }
}

public static class GlobalExceptionHandlerMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
        => app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
}

