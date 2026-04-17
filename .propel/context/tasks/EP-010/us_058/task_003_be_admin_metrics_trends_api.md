# Task - TASK_003

## Requirement Reference

- User Story: US_058
- Story Location: .propel/context/tasks/EP-010/us_058/us_058.md
- Acceptance Criteria:
    - AC-1: **Given** the admin logs in, **When** the admin dashboard loads, **Then** it displays system metrics (active users, daily appointments, no-show rate, AI agreement rate, uptime percentage).
    - AC-2: **Given** the metrics section is displayed, **When** the admin views trends, **Then** the system shows rolling 7-day and 30-day trend charts for key metrics.
- Edge Case:
    - What happens when system metrics data is temporarily unavailable? Dashboard shows cached values with a "Data as of [timestamp]" indicator.

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
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |

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

Implement the backend API endpoints for the admin dashboard system metrics and trend data. This task creates a `GET /admin/metrics` endpoint returning current snapshot values (active users, daily appointments, no-show rate, AI agreement rate, uptime percentage) and a `GET /admin/metrics/trends` endpoint returning rolling 7-day and 30-day historical trend data. Includes Redis caching with 5-minute TTL (NFR-030), admin role authorization (NFR-011), audit logging for metrics access (NFR-012), structured logging with correlation IDs (NFR-035), and a stale-data fallback mechanism when live computation is unavailable.

## Dependent Tasks

- US_001 — Foundational — Requires backend API scaffold (.NET 8 project, middleware, auth pipeline)
- US_008 — Foundational — Requires all domain entities (User, Appointment, ExtractedData, MedicalCode) for metrics computation
- task_005_db_admin_metrics_config_schema — Requires system_metrics_snapshot table and aggregation queries

## Impacted Components

- **NEW** `Server/Controllers/AdminMetricsController.cs` — API controller with metrics and trends endpoints
- **NEW** `Server/Services/ISystemMetricsService.cs` — Interface for system metrics aggregation
- **NEW** `Server/Services/SystemMetricsService.cs` — Implementation computing metrics from domain entities
- **NEW** `Server/Models/DTOs/SystemMetricsDto.cs` — Response DTO for current metrics snapshot
- **NEW** `Server/Models/DTOs/MetricsTrendDto.cs` — Response DTO for trend data points
- **NEW** `Server/Models/DTOs/MetricsTrendRequestDto.cs` — Request model with period validation
- **MODIFY** `Server/Program.cs` — Register SystemMetricsService in DI container

## Implementation Plan

1. **Create DTOs for metrics responses** — Define `SystemMetricsDto` with properties: `ActiveUsers` (int), `DailyAppointments` (int), `NoShowRate` (decimal), `AiAgreementRate` (decimal), `UptimePercentage` (decimal), `LastUpdatedAt` (DateTime), `IsStale` (bool). Define `MetricsTrendDto` with `Date` (DateOnly), `MetricName` (string), `Value` (decimal) for each data point. Define `MetricsTrendRequestDto` with `Period` (enum: SevenDays, ThirtyDays) validated via `[EnumDataType]`.

2. **Implement SystemMetricsService** — Create service implementing `ISystemMetricsService` with two methods:
   - `GetCurrentMetricsAsync()`: Aggregates live metrics by querying domain entities:
     - Active Users: `COUNT(Users WHERE LastLoginAt > NOW() - 15 minutes)`
     - Daily Appointments: `COUNT(Appointments WHERE Date = TODAY AND Status != Cancelled)`
     - No-Show Rate: `COUNT(Appointments WHERE Status = NoShow) / COUNT(Appointments WHERE Date IN LAST 30 DAYS) * 100`
     - AI Agreement Rate: `COUNT(MedicalCodes WHERE SuggestedByAi = true AND ApprovedByUserId IS NOT NULL) / COUNT(MedicalCodes WHERE SuggestedByAi = true) * 100`
     - Uptime Percentage: Read from `system_metrics_snapshot` table (latest health check aggregation)
   - `GetTrendDataAsync(period)`: Queries `system_metrics_snapshot` table for daily aggregated values over the requested period (7 or 30 days), returning a list of `MetricsTrendDto` per metric per day.

3. **Add Redis caching layer** — Wrap `GetCurrentMetricsAsync()` with Redis cache using key `admin:metrics:current` and 5-minute TTL (per NFR-030). On cache miss, compute from DB and cache. On computation failure (DB timeout), return last cached value with `IsStale = true` and `LastUpdatedAt` from cache timestamp. Wrap `GetTrendDataAsync()` with key `admin:metrics:trends:{period}` and 5-minute TTL.

4. **Create AdminMetricsController** — Implement two endpoints:
   - `GET /api/admin/metrics` → Returns `SystemMetricsDto` (200 OK)
   - `GET /api/admin/metrics/trends?period=7d|30d` → Returns `List<MetricsTrendDto>` (200 OK), default period = 7d
   - Apply `[Authorize(Roles = "Admin")]` attribute for RBAC (NFR-011)
   - Return `403 Forbidden` for non-admin users

5. **Add audit logging** — Log each metrics access via the Audit Service with action type `data_access`, resource type `system_metrics`, and admin user attribution (NFR-012). Use Serilog structured logging with correlation ID from `HttpContext.TraceIdentifier` (NFR-035).

6. **Add input validation and error handling** — Validate `period` query parameter accepts only `7d` or `30d` values; return `400 Bad Request` for invalid input (NFR-018). Implement global error handling returning standardized error responses without PII (NFR-017).

7. **Register service in DI** — Add `ISystemMetricsService` / `SystemMetricsService` as scoped service in `Program.cs`. Ensure Redis `IDistributedCache` is already registered from infrastructure setup.

## Current Project State

- Project is in planning phase. No `Server/` folder exists yet.
- Backend scaffold will be established by US_001 (dependency).
- Placeholder to be updated during task execution based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Controllers/AdminMetricsController.cs` | API controller with GET /admin/metrics and GET /admin/metrics/trends endpoints |
| CREATE | `Server/Services/ISystemMetricsService.cs` | Interface for metrics aggregation and trend computation |
| CREATE | `Server/Services/SystemMetricsService.cs` | Service computing metrics from Appointments, Users, MedicalCodes entities |
| CREATE | `Server/Models/DTOs/SystemMetricsDto.cs` | Response DTO for current metrics snapshot |
| CREATE | `Server/Models/DTOs/MetricsTrendDto.cs` | Response DTO for daily trend data points |
| CREATE | `Server/Models/DTOs/MetricsTrendRequestDto.cs` | Request model with period enum validation |
| MODIFY | `Server/Program.cs` | Register SystemMetricsService in DI container |

## External References

- [ASP.NET Core 8 Web API Documentation](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [ASP.NET Core Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-8.0)
- [IDistributedCache with Redis](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0)
- [Entity Framework Core 8 Query Performance](https://learn.microsoft.com/en-us/ef/core/performance/)
- [Serilog Structured Logging](https://serilog.net/)

## Build Commands

- Refer to applicable technology stack specific build commands in `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] GET /admin/metrics returns all 5 metric values with correct types
- [ ] GET /admin/metrics/trends?period=7d returns 7 days of trend data per metric
- [ ] GET /admin/metrics/trends?period=30d returns 30 days of trend data per metric
- [ ] Invalid period parameter returns 400 Bad Request
- [ ] Non-admin user receives 403 Forbidden
- [ ] Redis cache returns cached value on subsequent calls within 5-minute TTL
- [ ] Stale data fallback returns IsStale=true with LastUpdatedAt when DB unavailable
- [ ] Audit log entry created for each metrics access with admin attribution

## Implementation Checklist

- [ ] Create `SystemMetricsDto`, `MetricsTrendDto`, and `MetricsTrendRequestDto` DTOs
- [ ] Implement `ISystemMetricsService` interface with `GetCurrentMetricsAsync` and `GetTrendDataAsync` methods
- [ ] Implement `SystemMetricsService` aggregating metrics from User, Appointment, MedicalCode entities
- [ ] Add Redis caching with 5-minute TTL and stale-data fallback on computation failure
- [ ] Create `AdminMetricsController` with `[Authorize(Roles = "Admin")]` guard and two GET endpoints
- [ ] Add audit logging for metrics access with correlation ID and admin user attribution
- [ ] Add input validation for period parameter and standardized error responses
- [ ] Register `ISystemMetricsService` in DI container in `Program.cs`
