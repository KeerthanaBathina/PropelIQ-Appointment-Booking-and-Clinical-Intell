using System.Net;
using System.Text.Json;
using FluentValidation;
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

    /// <summary>
    /// User-friendly messages per HTTP status code (AC-3: no internal details exposed).
    /// The {0} placeholder in the 500 entry is replaced with the correlation ID at runtime.
    /// </summary>
    private static readonly Dictionary<int, string> UserFriendlyMessages = new()
    {
        [400] = "The request contains invalid data. Please check the fields below.",
        [401] = "Authentication is required to access this resource.",
        [403] = "You do not have permission to perform this action.",
        [404] = "The requested resource was not found.",
        [409] = "A conflict occurred with the current state of the resource.",
        [429] = "Too many requests. Please try again later.",
        [499] = "The request was cancelled.",
        [500] = "An unexpected error occurred. Please contact support with reference ID: {0}.",
    };

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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — 499 is a non-standard but widely-accepted code.
            // No logging: these are benign and would pollute alerting dashboards (AC-3).
            context.Response.StatusCode = 499;
            return;
        }
        catch (ValidationException ex)
        {
            var correlationId = GetCorrelationId(context);
            _logger.LogWarning(
                "Validation failed. CorrelationId: {CorrelationId} Path: {Path} Errors: {ErrorCount}",
                correlationId, context.Request.Path, ex.Errors.Count());

            // Build field-level error map — field names and expected formats only (AC-4).
            // ex.Message is deliberately excluded from the response body (AC-3, EC-1).
            var validationErrors = ex.Errors
                .GroupBy(e => e.PropertyName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            await WriteErrorResponseAsync(context,
                statusCode:       (int)HttpStatusCode.BadRequest,
                message:          UserFriendlyMessages[400],
                detail:           null,
                correlationId:    correlationId,
                validationErrors: validationErrors);
        }
        catch (UnauthorizedAccessException ex)
        {
            var correlationId = GetCorrelationId(context);
            _logger.LogWarning(
                ex,
                "Unauthorized access. CorrelationId: {CorrelationId} Path: {Path}",
                correlationId, context.Request.Path);

            await WriteErrorResponseAsync(context,
                statusCode:    (int)HttpStatusCode.Unauthorized,
                message:       UserFriendlyMessages[401],
                detail:        null,
                correlationId: correlationId);
        }
        catch (KeyNotFoundException ex)
        {
            var correlationId = GetCorrelationId(context);
            _logger.LogWarning(
                ex,
                "Resource not found. CorrelationId: {CorrelationId} Path: {Path}",
                correlationId, context.Request.Path);

            await WriteErrorResponseAsync(context,
                statusCode:    (int)HttpStatusCode.NotFound,
                message:       UserFriendlyMessages[404],
                detail:        null,
                correlationId: correlationId);
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
                message:       string.Format(UserFriendlyMessages[500], correlationId),
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
        string correlationId,
        IDictionary<string, string[]>? validationErrors = null)
    {
        context.Response.StatusCode  = statusCode;
        context.Response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            StatusCode       = statusCode,
            Message          = message,
            Detail           = detail,
            CorrelationId    = correlationId,
            Timestamp        = DateTimeOffset.UtcNow,
            ValidationErrors = validationErrors
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

