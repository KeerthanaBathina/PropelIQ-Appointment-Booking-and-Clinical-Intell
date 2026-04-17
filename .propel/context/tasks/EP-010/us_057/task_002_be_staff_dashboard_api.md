# Task - TASK_002

## Requirement Reference

- User Story: us_057
- Story Location: .propel/context/tasks/EP-010/us_057/us_057.md
- Acceptance Criteria:
    - AC-1: Given the staff member logs in, When the staff dashboard loads, Then it displays today's appointment schedule, arrival queue count, and pending tasks (unverified codes, flagged conflicts, intake reviews).
    - AC-2: Given the daily schedule is displayed, When appointments are listed, Then each shows patient name, time, type, status badge (color-coded), and no-show risk score.
    - AC-3: Given pending tasks are displayed, When the staff member clicks a task, Then they are navigated to the relevant screen (SCR-014 for coding, SCR-013 for conflicts, SCR-012 for parsing). [API provides TargetScreen field in PendingTaskDto]
    - AC-4: Given the dashboard is loaded, When data changes occur (new arrival, status change), Then the dashboard updates within 5 seconds without manual refresh.
- Edge Cases:
    - No appointments scheduled: API returns empty schedule array with zero counts.
    - Multiple staff viewing same patient changes: Redis cached views ensure all staff receive consistent data within 5-second cache window.

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
| ORM | Entity Framework Core | 8.x |
| Caching | Upstash Redis | 7.x |
| Database | PostgreSQL | 16.x |
| AI/ML | N/A | - |

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

Implement the backend API endpoint for the Staff Dashboard (`GET /api/staff/dashboard`) that aggregates today's appointment schedule, arrival queue summary, pending task counts, and completion statistics for the authenticated staff member. The endpoint serves cached data from Redis (5-second TTL per NFR-030/US_004 dependency) to ensure sub-second response times (NFR-004) and consistent real-time views across multiple staff sessions. The response includes appointment records with patient names, times, types, status enums, and no-show risk scores (FR-014). Pending tasks are categorized into unverified medical codes (SCR-014), flagged conflicts (SCR-013), and document intake reviews (SCR-012).

## Dependent Tasks

- US_008 (EP-DATA) — All domain entities (Appointment, Patient, QueueEntry, MedicalCode, ExtractedData, ClinicalDocument) must exist
- US_004 (EP-TECH) — Redis caching infrastructure must be operational
- task_003_db_staff_dashboard_queries (US_057) — Database views/functions for aggregated dashboard data

## Impacted Components

- **NEW** `Server/Controllers/StaffDashboardController.cs` — API controller with `GET /api/staff/dashboard` endpoint
- **NEW** `Server/Services/IStaffDashboardService.cs` — Service interface for dashboard aggregation
- **NEW** `Server/Services/StaffDashboardService.cs` — Service implementation with Redis caching and EF Core queries
- **NEW** `Server/DTOs/StaffDashboardDto.cs` — Response DTO: stats, schedule, pending tasks
- **NEW** `Server/DTOs/ScheduleAppointmentDto.cs` — Individual appointment DTO with status and risk score
- **NEW** `Server/DTOs/PendingTaskDto.cs` — Pending task DTO with category, patient, description, target screen
- **MODIFY** `Server/Program.cs` — Register `IStaffDashboardService` in DI container

## Implementation Plan

1. **Define DTOs** for the API response:
   - `StaffDashboardDto`: contains `Stats` (today's count, queue count, pending count, completed count), `Schedule` (list of `ScheduleAppointmentDto`), `PendingTasks` (list of `PendingTaskDto`).
   - `ScheduleAppointmentDto`: `AppointmentId`, `PatientId`, `PatientName`, `AppointmentTime`, `AppointmentType`, `Status` (enum string), `NoShowRiskScore` (int 0-100).
   - `PendingTaskDto`: `TaskId`, `Category` (enum: `CodeApproval`, `ConflictResolution`, `DocumentReview`), `PatientId`, `PatientName`, `Description`, `TargetScreen` (string: `SCR-014`, `SCR-013`, `SCR-012`).

2. **Create `IStaffDashboardService` interface** with method `Task<StaffDashboardDto> GetDashboardAsync(Guid staffUserId, CancellationToken ct)`.

3. **Implement `StaffDashboardService`**:
   - **Cache-first pattern**: Check Redis for key `staff:dashboard:{userId}:{today}`. If hit, deserialize and return. If miss, query DB, serialize to Redis with 5-second TTL, return.
   - **Today's Schedule query**: EF Core query on `Appointments` where `appointment_time` is today, ordered by time ascending. Join `Patient` for name. Include `no_show_risk_score` field.
   - **Queue Summary query**: Count `QueueEntry` records where `status` is `waiting` or `in_visit` for today.
   - **Pending Tasks aggregation**:
     - Unverified codes: `MedicalCode` where `approved_by_user_id` is null and `suggested_by_ai` is true → `CodeApproval` category, target `SCR-014`.
     - Flagged conflicts: `ExtractedData` where `flagged_for_review` is true and `verified_by_user_id` is null → `ConflictResolution` category, target `SCR-013`.
     - Document reviews: `ClinicalDocument` where `processing_status` is `completed` and not yet reviewed → `DocumentReview` category, target `SCR-012`.
   - **Stats computation**: Aggregate counts from above queries.
   - **Completed count**: Count appointments today where `status` is `completed`.

4. **Create `StaffDashboardController`**:
   - `[Authorize(Roles = "Staff")]` attribute for RBAC (NFR-011).
   - `[HttpGet("api/staff/dashboard")]` endpoint.
   - Extract `userId` from JWT claims.
   - Call `IStaffDashboardService.GetDashboardAsync(userId, ct)`.
   - Return `Ok(result)` with proper HTTP status codes (200, 401, 403, 500).
   - Implement idempotent GET per NFR-034.

5. **Register service** in `Program.cs` DI: `builder.Services.AddScoped<IStaffDashboardService, StaffDashboardService>()`.

6. **Error handling**: Return structured `ProblemDetails` for errors. Log with correlation ID (NFR-035). Do not expose PII in error responses (NFR-017).

**Focus on how to implement:**
- Use `IDistributedCache` or `IConnectionMultiplexer` (StackExchange.Redis) for Redis interaction
- Use EF Core `AsNoTracking()` for read-only queries (performance)
- Use `CancellationToken` propagation on all async paths
- Apply `[ProducesResponseType]` attributes for OpenAPI documentation (NFR-038)
- Validate that appointment status enum maps to: Scheduled, Confirmed, Arrived, InVisit, Completed, Cancelled, NoShow, Waitlisted

## Current Project State

```
[Placeholder — to be updated based on dependent task completion]
Server/
├── Controllers/
│   └── StaffDashboardController.cs  (NEW)
├── Services/
│   ├── IStaffDashboardService.cs    (NEW)
│   └── StaffDashboardService.cs     (NEW)
├── DTOs/
│   ├── StaffDashboardDto.cs         (NEW)
│   ├── ScheduleAppointmentDto.cs    (NEW)
│   └── PendingTaskDto.cs            (NEW)
└── Program.cs                       (MODIFY)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/StaffDashboardController.cs | Staff-authorized GET endpoint returning aggregated dashboard data |
| CREATE | Server/Services/IStaffDashboardService.cs | Service interface for dashboard data aggregation |
| CREATE | Server/Services/StaffDashboardService.cs | Cache-first implementation with Redis (5s TTL) and EF Core queries |
| CREATE | Server/DTOs/StaffDashboardDto.cs | Response DTO with Stats, Schedule, PendingTasks sections |
| CREATE | Server/DTOs/ScheduleAppointmentDto.cs | Appointment DTO with status enum, no-show risk score |
| CREATE | Server/DTOs/PendingTaskDto.cs | Pending task DTO with category, patient info, target screen |
| MODIFY | Server/Program.cs | Register IStaffDashboardService in DI container |

## External References

- [ASP.NET Core 8 — Minimal APIs and Controllers](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [EF Core 8 — Querying Data](https://learn.microsoft.com/en-us/ef/core/querying/)
- [StackExchange.Redis — .NET Client](https://stackexchange.github.io/StackExchange.Redis/)
- [ASP.NET Core — Distributed Caching](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0)
- [ASP.NET Core — Authorization with Roles](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-8.0)

## Build Commands

- `cd Server && dotnet restore` — Restore NuGet packages
- `cd Server && dotnet build` — Build backend project
- `cd Server && dotnet run` — Start API server

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] `GET /api/staff/dashboard` returns 200 with correct DTO shape
- [ ] Response includes today's appointments ordered by time ascending
- [ ] Each appointment includes patient name, time, type, status, no-show risk score
- [ ] Pending tasks categorized correctly (CodeApproval, ConflictResolution, DocumentReview)
- [ ] Redis cache hit returns data within <100ms
- [ ] Redis cache miss queries DB and caches with 5-second TTL
- [ ] Unauthorized request (no token) returns 401
- [ ] Forbidden request (Patient role) returns 403
- [ ] Empty schedule returns empty array with zero counts (not error)
- [ ] Error responses use ProblemDetails format without PII
- [ ] Correlation ID present in logs for all requests (NFR-035)

## Implementation Checklist

- [ ] Define `StaffDashboardDto`, `ScheduleAppointmentDto`, `PendingTaskDto` DTOs
- [ ] Create `IStaffDashboardService` interface with `GetDashboardAsync` method
- [ ] Implement `StaffDashboardService` with Redis cache-first pattern (5-second TTL)
- [ ] Implement today's schedule EF Core query with Patient join and no-show risk score
- [ ] Implement queue summary count query (waiting + in_visit QueueEntry records)
- [ ] Implement pending tasks aggregation: unverified codes, flagged conflicts, document reviews
- [ ] Implement completed-today count from appointments
- [ ] Create `StaffDashboardController` with `[Authorize(Roles = "Staff")]` and `GET` endpoint
- [ ] Register `IStaffDashboardService` in DI (Program.cs)
- [ ] Add `[ProducesResponseType]` attributes for OpenAPI documentation
- [ ] Implement structured error handling with ProblemDetails and correlation ID logging
