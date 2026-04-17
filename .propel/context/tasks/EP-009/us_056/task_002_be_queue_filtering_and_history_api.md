# Task - TASK_002

## Requirement Reference

- User Story: US_056
- Story Location: .propel/context/tasks/EP-009/us_056/us_056.md
- Acceptance Criteria:
    - AC-1: Given the queue dashboard is displayed, When the staff member selects a provider filter, Then the queue shows only entries for the selected provider.
    - AC-2: Given the queue dashboard is displayed, When the staff member applies multiple filters (provider + appointment type + status), Then filters are combined with AND logic and results update instantly.
    - AC-3: Given the staff member navigates to queue history, When they select a date range, Then the system displays historical queue data including average wait times, no-show counts, and patient throughput.
    - AC-4: Given queue history data is available, When the staff member requests an export, Then the system generates a CSV report with queue metrics for the selected period.
- Edge Cases:
    - When no queue entries match the applied filters, the API returns an empty array with HTTP 200 (not 404).
    - When queue history is requested for dates before system deployment, the API returns an empty result set with a `availableDateRange` field indicating the earliest available date.

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
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| ORM | Entity Framework Core | 8.x |
| Caching | Upstash Redis | 7.x |
| Database | PostgreSQL | 16.x |
| AI/ML | N/A | - |
| Vector Store | N/A | - |
| AI Gateway | N/A | - |
| Mobile | N/A | - |

**Note**: All code and libraries MUST be compatible with versions above.

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

Implement backend API endpoints for queue filtering and queue history analytics. This includes extending the existing `GET /api/queue/today` endpoint with optional filter query parameters (provider, appointmentType, status) applying AND logic, creating a new `GET /api/queue/history` endpoint returning aggregated historical queue metrics for a date range, and creating a `GET /api/queue/history/export` endpoint that generates and returns a CSV file with queue analytics data. All endpoints require Staff role authorization and implement structured logging with correlation IDs.

## Dependent Tasks

- US_053 tasks (Real-Time Arrival Queue Dashboard) — Requires the base QueueController and QueueService with `GET /api/queue/today` endpoint.
- US_008 tasks (Core Domain Entity Models) — Requires QueueEntry, Appointment EF Core entity models and DbContext configuration.

## Impacted Components

- **Modify**: `QueueController` — Add filter query parameters to GET /api/queue/today; add GET /api/queue/history and GET /api/queue/history/export endpoints (Server/Controllers/)
- **Modify**: `IQueueService` / `QueueService` — Add `GetFilteredQueueAsync`, `GetQueueHistoryAsync`, `ExportQueueHistoryAsync` methods (Server/Services/)
- **New**: `QueueFilterDto` — DTO for filter parameters: ProviderId, AppointmentType, Status (Server/DTOs/)
- **New**: `QueueHistoryRequestDto` — DTO for history request: StartDate, EndDate (Server/DTOs/)
- **New**: `QueueHistoryResponseDto` — DTO for history response: Metrics list, AvailableDateRange (Server/DTOs/)
- **New**: `QueueMetricsDto` — DTO for daily metrics: Date, AvgWaitTimeMinutes, NoShowCount, PatientThroughput (Server/DTOs/)
- **New**: `CsvExportService` / `ICsvExportService` — Generic CSV export helper service (Server/Services/)
- **Modify**: `IQueueRepository` / `QueueRepository` — Add filtered query and history aggregation methods (Server/Repositories/)

## Implementation Plan

1. **Create DTOs** for filter parameters (`QueueFilterDto` with nullable ProviderId, AppointmentType, Status properties), history request (`QueueHistoryRequestDto` with StartDate, EndDate and validation attributes), history response (`QueueHistoryResponseDto` with list of `QueueMetricsDto` and `AvailableDateRange`), and daily metrics (`QueueMetricsDto` with Date, AvgWaitTimeMinutes, NoShowCount, PatientThroughput).
2. **Extend QueueRepository** with `GetFilteredQueueEntriesAsync(QueueFilterDto filters)` method. Build EF Core LINQ query that conditionally applies `.Where()` clauses for each non-null filter parameter using AND composition. Include `.Include(q => q.Appointment).ThenInclude(a => a.Patient)` for related data.
3. **Add QueueRepository history method** `GetQueueHistoryAsync(DateTime startDate, DateTime endDate)` using EF Core GroupBy on `QueueEntry.CreatedAt.Date` to aggregate daily metrics: `AVG(wait_time_minutes)`, `COUNT(status == no_show)`, `COUNT(DISTINCT appointment_id)`. Return `IEnumerable<QueueMetricsDto>`.
4. **Add QueueRepository method** `GetAvailableDateRangeAsync()` to return the earliest and latest QueueEntry dates for the `availableDateRange` field.
5. **Extend QueueService** with `GetFilteredQueueAsync(QueueFilterDto)` that delegates to the repository and maps entities to response DTOs. Add `GetQueueHistoryAsync(QueueHistoryRequestDto)` that calls repository, handles empty results, and includes `availableDateRange`. Add `ExportQueueHistoryAsync(QueueHistoryRequestDto)` that fetches history data and converts to CSV bytes.
6. **Create CsvExportService** with a generic `GenerateCsv<T>(IEnumerable<T> data)` method using reflection to build headers from property names and rows from property values. Return `byte[]` for the CSV content.
7. **Extend QueueController**: Add optional `[FromQuery]` parameters (providerId, appointmentType, status) to existing `GET /api/queue/today`. Add `[HttpGet("history")]` endpoint accepting `[FromQuery] QueueHistoryRequestDto` returning `QueueHistoryResponseDto`. Add `[HttpGet("history/export")]` endpoint returning `FileContentResult` with `text/csv` MIME type and `Content-Disposition: attachment` header.
8. **Add input validation and error handling**: Validate date range (startDate <= endDate, max 365-day range), sanitize filter string inputs to prevent injection, return HTTP 400 for invalid parameters. Apply `[Authorize(Roles = "Staff,Admin")]` on all endpoints. Add structured logging with correlation IDs via Serilog.

**Focus on how to implement:**

- Use `[FromQuery]` attribute binding for filter parameters on existing queue endpoint. Nullable types allow optional filters.
- Use EF Core conditional `.Where()` chaining: `if (filters.ProviderId.HasValue) query = query.Where(q => q.Appointment.ProviderId == filters.ProviderId.Value);`
- Use EF Core `GroupBy` with aggregate projections for history metrics. Use raw SQL via `FromSqlRaw` if GroupBy translation is limited.
- Use `[Authorize(Roles = "Staff,Admin")]` attribute for role-based access control per NFR-011.
- Return `File(csvBytes, "text/csv", $"queue-history-{startDate:yyyyMMdd}-{endDate:yyyyMMdd}.csv")` for CSV export.
- Use `FluentValidation` or Data Annotations for request DTO validation (StartDate required, EndDate required, EndDate >= StartDate).
- Apply Redis caching (5-minute TTL per NFR-030) on queue history results using `IDistributedCache`.
- Log all queue access events via AuditService for HIPAA compliance (NFR-012).

## Current Project State

- [Placeholder — to be updated based on completion of dependent US_053 tasks]

```text
Server/
├── Controllers/
│   └── QueueController.cs      # Existing from US_053
├── Services/
│   ├── IQueueService.cs         # Existing interface
│   └── QueueService.cs          # Existing implementation
├── Repositories/
│   ├── IQueueRepository.cs      # Existing interface
│   └── QueueRepository.cs       # Existing implementation
├── DTOs/
├── Models/
│   └── QueueEntry.cs            # From US_008
└── ...
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/DTOs/QueueFilterDto.cs | Filter parameters DTO with nullable ProviderId, AppointmentType, Status |
| CREATE | Server/DTOs/QueueHistoryRequestDto.cs | History request DTO with StartDate, EndDate, validation attributes |
| CREATE | Server/DTOs/QueueHistoryResponseDto.cs | History response DTO with metrics list and available date range |
| CREATE | Server/DTOs/QueueMetricsDto.cs | Daily metrics DTO: Date, AvgWaitTimeMinutes, NoShowCount, PatientThroughput |
| CREATE | Server/Services/ICsvExportService.cs | CSV export service interface |
| CREATE | Server/Services/CsvExportService.cs | Generic CSV generation from IEnumerable using reflection |
| MODIFY | Server/Repositories/IQueueRepository.cs | Add GetFilteredQueueEntriesAsync, GetQueueHistoryAsync, GetAvailableDateRangeAsync |
| MODIFY | Server/Repositories/QueueRepository.cs | Implement filtered query with conditional WHERE, GroupBy aggregation for history |
| MODIFY | Server/Services/IQueueService.cs | Add GetFilteredQueueAsync, GetQueueHistoryAsync, ExportQueueHistoryAsync |
| MODIFY | Server/Services/QueueService.cs | Implement filter delegation, history aggregation, CSV export orchestration |
| MODIFY | Server/Controllers/QueueController.cs | Add filter params to GET /today; add GET /history and GET /history/export endpoints |

## External References

- [ASP.NET Core Web API Query Parameters](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-8.0)
- [EF Core 8 GroupBy Translation](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators#groupby)
- [ASP.NET Core FileContentResult](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.filecontentresult?view=aspnetcore-8.0)
- [Serilog Structured Logging](https://serilog.net/)
- [FluentValidation for ASP.NET Core](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [Redis IDistributedCache in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] GET /api/queue/today?providerId={id} returns only entries for specified provider
- [ ] GET /api/queue/today?providerId={id}&status=waiting combines filters with AND logic
- [ ] GET /api/queue/today with no filters returns all entries (backward compatible)
- [ ] GET /api/queue/history?startDate=&endDate= returns aggregated metrics
- [ ] GET /api/queue/history with pre-deployment dates returns empty with availableDateRange
- [ ] GET /api/queue/history/export returns valid CSV file with correct Content-Type header
- [ ] Invalid date range (startDate > endDate) returns HTTP 400
- [ ] Unauthorized access (non-Staff role) returns HTTP 403
- [ ] All endpoints log access events via AuditService

## Implementation Checklist

- [ ] Create QueueFilterDto, QueueHistoryRequestDto, QueueHistoryResponseDto, QueueMetricsDto DTOs
- [ ] Extend IQueueRepository and QueueRepository with filtered query method using conditional WHERE clauses
- [ ] Add history aggregation method to QueueRepository with EF Core GroupBy for daily metrics
- [ ] Add GetAvailableDateRangeAsync to repository for earliest/latest queue entry dates
- [ ] Create ICsvExportService and CsvExportService for generic CSV byte array generation
- [ ] Extend IQueueService and QueueService with filter, history, and export methods
- [ ] Add filter query parameters to existing GET /api/queue/today endpoint in QueueController
- [ ] Add GET /api/queue/history and GET /api/queue/history/export endpoints with authorization and validation
