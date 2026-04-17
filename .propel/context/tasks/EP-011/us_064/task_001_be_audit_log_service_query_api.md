# Task - task_001_be_audit_log_service_query_api

## Requirement Reference

- User Story: US_064
- Story Location: .propel/context/tasks/EP-011/us_064/us_064.md
- Acceptance Criteria:
    - AC-1: **Given** any data is accessed, created, modified, or deleted, **When** the operation completes, **Then** an immutable audit log entry is created with user ID, action type, entity type, entity ID, timestamp, and IP address.
    - AC-2: **Given** audit log entries exist, **When** any user attempts to modify or delete a log entry, **Then** the operation is rejected and the attempt itself is logged.
    - AC-3: **Given** an admin queries the audit log, **When** they filter by user, action type, date range, or entity, **Then** results are returned with pagination and the underlying data remains unchanged.
- Edge Cases:
    - What happens when the audit log table grows extremely large (millions of records)? System uses table partitioning by month and indexes on user_id, entity_type, and created_at for query performance (handled by task_002_db).

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | N/A | - |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| API Framework | ASP.NET Core MVC | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Library | Serilog (Structured Logging) | 8.x |
| AI/ML | N/A | - |
| Vector Store | N/A | - |
| AI Gateway | N/A | - |
| Mobile | N/A | - |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the core `AuditService` for immutable audit log event recording and the `AuditLogController` for admin-facing audit log queries. The service provides an append-only write interface (`IAuditService.LogAsync`) used as a cross-cutting concern throughout the platform to record all data access, creation, modification, and deletion events with full user attribution (user ID, action type, entity type, entity ID, timestamp, IP address). The controller exposes a `GET /api/audit-logs` endpoint restricted to the Admin role, supporting filtering by user, action type, date range, and entity type with cursor-based pagination for efficient traversal of large datasets. Immutability is enforced at the service layer by exposing no update or delete methods and returning 405 Method Not Allowed for any such API attempts, with the violation attempt itself recorded as an audit event. This task implements the CQRS pattern per Architecture Decision #5 — the write path (`AuditService`) is separated from the read path (`AuditLogQueryService`) to avoid audit query load impacting transactional write performance.

## Dependent Tasks

- US_008 (task_001_be_domain_entity_models) — Requires `AuditLog` entity, `AuditAction` enum, and `ApplicationDbContext.AuditLogs` DbSet
- US_008 (task_002_be_efcore_configuration_migrations) — Requires `AuditLogConfiguration` with table `audit_logs`, indexes, and FK to `ApplicationUser`
- US_003 — Requires PostgreSQL 16 database infrastructure with connection pooling

## Impacted Components

- **NEW** `Server/Services/IAuditService.cs` — Interface for append-only audit log writes
- **NEW** `Server/Services/AuditService.cs` — Implementation writing immutable audit entries to `audit_logs` via EF Core
- **NEW** `Server/Services/IAuditLogQueryService.cs` — Interface for audit log read queries (CQRS read side)
- **NEW** `Server/Services/AuditLogQueryService.cs` — Implementation with filtered, paginated reads using `IQueryable` composition
- **NEW** `Server/Controllers/AuditLogController.cs` — Admin-only API controller for audit log queries
- **NEW** `Server/Models/DTOs/AuditLogQueryRequest.cs` — Query request DTO with filter parameters
- **NEW** `Server/Models/DTOs/AuditLogQueryResponse.cs` — Paginated response DTO
- **NEW** `Server/Models/DTOs/AuditLogEntryDto.cs` — Individual audit log entry DTO (read model)
- **MODIFY** `Server/Program.cs` — Register `IAuditService`, `IAuditLogQueryService` in DI container

## Implementation Plan

1. **Create AuditService (Write Side — CQRS Command)** — Implement `IAuditService` with a single method: `Task LogAsync(Guid userId, AuditAction action, string resourceType, Guid resourceId, string ipAddress, string userAgent)`. The implementation injects `ApplicationDbContext`, creates a new `AuditLog` entity with `LogId = Guid.NewGuid()`, `Timestamp = DateTime.UtcNow`, and the provided parameters, calls `_context.AuditLogs.Add(entry)` and `await _context.SaveChangesAsync()`. The service MUST NOT expose any update or delete methods — append-only by design (FR-093, NFR-012). Register as `Scoped` lifetime in DI. Add structured Serilog logging for each write: `Log.Information("AuditLog created: {LogId} {Action} {ResourceType} {ResourceId}", ...)` with correlation ID from `HttpContext.TraceIdentifier` (NFR-035).

2. **Create Request/Response DTOs (Read Side)** — Create `AuditLogQueryRequest` with `[FromQuery]` parameters: `UserId` (Guid?, optional), `ActionType` (AuditAction?, optional), `EntityType` (string?, optional), `EntityId` (Guid?, optional), `StartDate` (DateTime?, optional), `EndDate` (DateTime?, optional), `Cursor` (Guid?, optional — last `LogId` for cursor-based pagination), `PageSize` (int, default 50, max 200). Create `AuditLogEntryDto` mapping: `LogId`, `UserId`, `UserEmail` (joined from User), `Action`, `ResourceType`, `ResourceId`, `Timestamp`, `IpAddress`, `UserAgent`. Create `AuditLogQueryResponse` with `Items` (list of `AuditLogEntryDto`), `NextCursor` (Guid?), `TotalCount` (long), `HasMore` (bool).

3. **Create AuditLogQueryService (Read Side — CQRS Query)** — Implement `IAuditLogQueryService` with `Task<AuditLogQueryResponse> QueryAsync(AuditLogQueryRequest request)`. Build an `IQueryable<AuditLog>` pipeline with conditional `.Where()` clauses for each filter parameter. For `StartDate`/`EndDate`, filter on `Timestamp` column using `>=` and `<=` comparisons. For cursor-based pagination, filter `WHERE log_id > @cursor ORDER BY log_id` using keyset pagination for O(1) seek performance regardless of offset depth. Execute `CountAsync()` for `TotalCount` (optionally cached). Project to `AuditLogEntryDto` using `.Select()` with a join to `User` table for `UserEmail`. Use `AsNoTracking()` for read-only queries to avoid EF change tracking overhead. Parameterize all filters to prevent SQL injection (NFR-018).

4. **Create AuditLogController** — Create `[ApiController]` with `[Route("api/audit-logs")]` and `[Authorize(Roles = "Admin")]`. Implement `GET /` endpoint accepting `[FromQuery] AuditLogQueryRequest`, calling `IAuditLogQueryService.QueryAsync()`, returning `AuditLogQueryResponse`. The controller MUST NOT expose PUT, PATCH, or DELETE endpoints. Override `OnActionExecuting` or add a custom action filter that intercepts any non-GET HTTP method and returns `405 Method Not Allowed` with body `{ "error": "MethodNotAllowed", "message": "Audit log entries are immutable and cannot be modified or deleted." }`. When a 405 is returned, call `IAuditService.LogAsync()` to record the violation attempt with `action = DataModify` or `DataDelete`, `resourceType = "AuditLog"`, and the requesting user's ID and IP (AC-2). Apply `[ProducesResponseType]` attributes for 200, 400, 401, 403, 405 status codes for OpenAPI documentation (NFR-038).

5. **Implement IP Address Extraction** — Create `IClientInfoAccessor` / `ClientInfoAccessor` that extracts the client IP address from `HttpContext.Connection.RemoteIpAddress` with support for `X-Forwarded-For` header when behind IIS reverse proxy. Extract `User-Agent` from `HttpContext.Request.Headers["User-Agent"]`. Register as `Scoped` in DI. Both `AuditService` and controllers use this accessor to populate `ip_address` and `user_agent` fields consistently. Sanitize the `X-Forwarded-For` value to prevent header injection (take only the first IP, validate format).

6. **Implement Audit Middleware for Cross-Cutting Logging** — Create `AuditLoggingMiddleware` that can be applied to controller actions via `[ServiceFilter(typeof(AuditLoggingActionFilter))]` or as a global action filter. The filter intercepts successful state-changing requests (POST, PUT, PATCH, DELETE) and calls `IAuditService.LogAsync()` with the action type derived from HTTP method mapping: POST → `DataModify`, PUT/PATCH → `DataModify`, DELETE → `DataDelete`. GET requests on sensitive endpoints (patient data, clinical documents) map to `DataAccess`. Extract `resourceType` and `resourceId` from route values or response body. This enables other services across the platform to automatically log audit events without manual `IAuditService` calls in every controller.

7. **Service Registration and Configuration** — Register `IAuditService` / `AuditService` as `Scoped` in `Program.cs`. Register `IAuditLogQueryService` / `AuditLogQueryService` as `Scoped`. Register `IClientInfoAccessor` / `ClientInfoAccessor` as `Scoped`. Register `AuditLoggingActionFilter` as `Scoped`. Add configuration section `AuditSettings` in `appsettings.json` with `QueryMaxPageSize: 200`, `QueryDefaultPageSize: 50`, `RetentionYears: 7` (configurable per NFR-043). Bind configuration using `IOptions<AuditSettings>`.

8. **Structured Error Handling and Logging** — Implement error handling in `AuditService.LogAsync()`: if the DB write fails, do NOT throw and break the calling operation — catch the exception, log it as `Log.Error("Failed to write audit log: {Error}", ex)`, and re-queue the entry to Redis via `IAuditQueueService` (implemented in task_003). This ensures audit write failures never impact transactional workflows. All structured logs include `CorrelationId` from `HttpContext.TraceIdentifier` (NFR-035). PII fields (`ip_address`, `user_agent`) are stored in the audit log but MUST NOT appear in application logs (NFR-017).

## Current Project State

- [Placeholder — to be updated based on completion of dependent tasks US_008 and US_003]

```text
Server/
├── Controllers/
├── Services/
├── Models/
│   ├── DTOs/
│   └── Entities/
│       ├── AuditLog.cs
│       └── Enums/
│           └── AuditAction.cs
├── Data/
│   ├── ApplicationDbContext.cs
│   └── Configurations/
│       └── AuditLogConfiguration.cs
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IAuditService.cs | Append-only audit log write interface with `LogAsync` method |
| CREATE | Server/Services/AuditService.cs | Implementation writing immutable audit entries via EF Core with Redis fallback on failure |
| CREATE | Server/Services/IAuditLogQueryService.cs | CQRS read-side interface for filtered, paginated audit log queries |
| CREATE | Server/Services/AuditLogQueryService.cs | Implementation with IQueryable composition, cursor pagination, AsNoTracking |
| CREATE | Server/Controllers/AuditLogController.cs | Admin-only GET endpoint with 405 enforcement for mutation attempts |
| CREATE | Server/Models/DTOs/AuditLogQueryRequest.cs | Query DTO with UserId, ActionType, EntityType, StartDate, EndDate, Cursor, PageSize |
| CREATE | Server/Models/DTOs/AuditLogQueryResponse.cs | Paginated response with Items, NextCursor, TotalCount, HasMore |
| CREATE | Server/Models/DTOs/AuditLogEntryDto.cs | Read-model DTO mapping AuditLog entity fields plus joined UserEmail |
| CREATE | Server/Services/IClientInfoAccessor.cs | Interface for extracting client IP and User-Agent from HttpContext |
| CREATE | Server/Services/ClientInfoAccessor.cs | Implementation with X-Forwarded-For support and header injection prevention |
| CREATE | Server/Filters/AuditLoggingActionFilter.cs | Cross-cutting action filter for automatic audit logging on state-changing requests |
| CREATE | Server/Models/Configuration/AuditSettings.cs | Configuration POCO for audit query limits and retention settings |
| MODIFY | Server/Program.cs | Register IAuditService, IAuditLogQueryService, IClientInfoAccessor, AuditLoggingActionFilter, AuditSettings |
| MODIFY | Server/appsettings.json | Add AuditSettings configuration section |

## External References

- [ASP.NET Core 8 Web API Controller Documentation](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [Entity Framework Core 8 Querying — AsNoTracking](https://learn.microsoft.com/en-us/ef/core/querying/tracking#no-tracking-queries)
- [ASP.NET Core Action Filters](https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/filters?view=aspnetcore-8.0)
- [Keyset Pagination with EF Core](https://learn.microsoft.com/en-us/ef/core/querying/pagination#keyset-pagination)
- [ASP.NET Core Options Pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0)
- [Serilog Structured Logging for .NET](https://serilog.net/)
- [HIPAA Audit Log Requirements — 45 CFR §164.312(b)](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] AuditService.LogAsync creates immutable entries with all required fields (AC-1)
- [ ] No update/delete methods exposed on IAuditService interface
- [ ] PUT/PATCH/DELETE on /api/audit-logs returns 405 and logs the attempt (AC-2)
- [ ] GET /api/audit-logs returns filtered, paginated results for Admin role (AC-3)
- [ ] Non-Admin roles receive 403 Forbidden on query endpoint
- [ ] Cursor-based pagination traverses full dataset without offset degradation
- [ ] PII (ip_address, user_agent) not leaked to application logs (NFR-017)
- [ ] Structured logs include CorrelationId for all audit operations (NFR-035)
- [ ] DB write failure in AuditService does not break calling transaction

## Implementation Checklist

- [ ] Create `IAuditService` / `AuditService` with append-only `LogAsync` method writing to `audit_logs` via EF Core (FR-093, NFR-012, AC-1)
- [ ] Create `AuditLogQueryRequest`, `AuditLogQueryResponse`, and `AuditLogEntryDto` DTOs with validation attributes and cursor pagination fields
- [ ] Create `IAuditLogQueryService` / `AuditLogQueryService` with `IQueryable` filter composition, cursor-based keyset pagination, and `AsNoTracking` (AC-3, TR-013)
- [ ] Create `AuditLogController` with Admin-only `GET /api/audit-logs`, 405 enforcement on mutations, and violation attempt logging (AC-2, AC-3)
- [ ] Create `IClientInfoAccessor` / `ClientInfoAccessor` with `X-Forwarded-For` parsing and header injection prevention (AC-1, NFR-018)
- [ ] Create `AuditLoggingActionFilter` for cross-cutting automatic audit logging on state-changing HTTP methods
- [ ] Register all services, filter, and `AuditSettings` configuration in `Program.cs` with `IOptions<AuditSettings>` binding (NFR-040, NFR-043)
- [ ] Implement graceful error handling in `AuditService` — catch DB failures, log error, delegate to Redis queue (task_003), never break calling operation
