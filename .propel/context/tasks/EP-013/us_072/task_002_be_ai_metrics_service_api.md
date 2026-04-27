# Task - task_002_be_ai_metrics_service_api

## Requirement Reference

- User Story: us_072
- Story Location: .propel/context/tasks/EP-013/us_072/us_072.md
- Acceptance Criteria:
  - AC-1: Given the admin opens the AI monitoring dashboard, When it loads, Then it displays current AI-human agreement rate for medical coding with a target indicator (>98%).
  - AC-2: Given data extraction results are tracked, When the dashboard renders metrics, Then it shows precision and recall rates for data extraction with a target indicator (>95%).
  - AC-3: Given AI latency is monitored, When the dashboard displays latency, Then it shows P50 and P95 latencies for intake (<1s), document parsing (<30s), and medical coding (<5s).
  - AC-4: Given any metric drops below its target threshold, When the daily calculation runs, Then the system generates an alert with the metric name, current value, target value, and trend direction.
- Edge Case:
  - What happens when insufficient data exists for meaningful accuracy calculations? Dashboard shows "Insufficient data" with minimum sample size requirement displayed.
  - How does the system handle metrics from different time periods? Dashboard supports daily, weekly, and monthly views with date range selectors.

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
| Backend | Entity Framework Core | 8.x |
| Backend | Npgsql.EntityFrameworkCore.PostgreSQL | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |

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

Implement the backend service layer and REST API endpoints for the AI accuracy monitoring dashboard. This includes: (1) an `IAiMetricsService` that aggregates accuracy metrics from `MedicalCode` and `ExtractedData` entities, calculates latency percentiles from AI Gateway request logs, and computes trend direction; (2) a daily background job (`AiMetricsCalculationJob`) that runs the aggregation, persists results to the metrics tables created in task_001, and generates alerts when thresholds are breached per AC-4; (3) REST API endpoints under `/api/admin/ai-metrics` returning metric summaries, time-series data, and alert lists for the frontend dashboard.

## Dependent Tasks

- US_072 task_001_be_ai_metrics_schema — Requires AI metrics entity models and database schema.
- US_008 task_001_be_domain_entity_models — Requires `MedicalCode` and `ExtractedData` entities as source data for accuracy calculations.
- US_067 — Requires AI Gateway service for latency metrics collection (request/response timing logs).

## Impacted Components

- **NEW** `src/UPACIP.Service/AiMetrics/IAiMetricsService.cs` — Service interface for metrics aggregation and retrieval
- **NEW** `src/UPACIP.Service/AiMetrics/AiMetricsService.cs` — Service implementation: accuracy calculation, latency aggregation, trend computation
- **NEW** `src/UPACIP.Service/AiMetrics/AiMetricsCalculationJob.cs` — Background job for daily metrics aggregation and alert generation
- **NEW** `src/UPACIP.Service/AiMetrics/Dtos/AiMetricsSummaryDto.cs` — DTO for dashboard summary response
- **NEW** `src/UPACIP.Service/AiMetrics/Dtos/AiMetricsTimeSeriesDto.cs` — DTO for time-series chart data
- **NEW** `src/UPACIP.Service/AiMetrics/Dtos/AiMetricAlertDto.cs` — DTO for alert list response
- **NEW** `src/UPACIP.Api/Controllers/Admin/AiMetricsController.cs` — REST API controller with admin-only authorization
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register `IAiMetricsService`, `AiMetricsCalculationJob` in DI container

## Implementation Plan

1. **Define DTOs for API responses**: Create `AiMetricsSummaryDto` containing: `CodingAgreementRate` (double), `ExtractionPrecision` (double), `ExtractionRecall` (double), `CodingAgreementTarget` (double), `ExtractionTarget` (double), `CodingSampleSize` (int), `ExtractionSampleSize` (int), `MinSampleSize` (int, constant = 30), latency sub-object with P50/P95 per operation type, and `Alerts` list. Create `AiMetricsTimeSeriesDto` with `Date`, `MetricType`, `Value` arrays for chart rendering. Create `AiMetricAlertDto` with alert details.

2. **Implement `IAiMetricsService` interface**: Define methods: `Task<AiMetricsSummaryDto> GetCurrentSummaryAsync()`, `Task<AiMetricsTimeSeriesDto> GetTimeSeriesAsync(DateTime startDate, DateTime endDate, string granularity)`, `Task<List<AiMetricAlertDto>> GetActiveAlertsAsync()`, `Task RunDailyAggregationAsync()`.

3. **Implement accuracy calculation logic**: In `AiMetricsService`, calculate AI-human agreement rate by querying `MedicalCode` entities where `SuggestedByAi == true` and comparing `CodeValue` against staff-approved values (`ApprovedByUserId != null`). Agreement rate = (matching codes / total approved codes) × 100. For extraction accuracy, calculate precision (true positives / predicted positives) and recall (true positives / actual positives) from `ExtractedData` entities using `FlaggedForReview` and `VerifiedByUserId` fields. Return "Insufficient data" indicator when sample size < 30 (edge case).

4. **Implement latency aggregation logic**: Query AI Gateway request logs (timestamps stored during AI inference calls) grouped by `AiOperationType`. Calculate P50 and P95 using percentile computation over the daily request durations. Store results in `AiLatencyMetric` entities.

5. **Implement trend direction calculation**: Compare current day's metric value against previous day's value. If delta > +1%, trend = `Up`. If delta < -1%, trend = `Down`. Otherwise, trend = `Stable`. Attach trend to each metric in the summary response.

6. **Implement daily aggregation background job**: Create `AiMetricsCalculationJob` as an `IHostedService` using `PeriodicTimer` (24-hour interval, configurable start time). On each tick: call accuracy calculation, latency aggregation, persist `AiAccuracyMetric` and `AiLatencyMetric` records. Then evaluate each metric against `AiMetricThreshold` entries; if value < threshold and `IsEnabled`, create `AiMetricAlert` record with metric name, current value, target value, and trend direction per AC-4.

7. **Implement REST API controller**: Create `AiMetricsController` at route `api/admin/ai-metrics` with `[Authorize(Roles = "Admin")]`. Endpoints: `GET /summary` (returns `AiMetricsSummaryDto`), `GET /time-series?startDate=&endDate=&granularity=` (returns `AiMetricsTimeSeriesDto`, supports daily/weekly/monthly per edge case), `GET /alerts` (returns active alerts), `PUT /alerts/{id}/acknowledge` (marks alert acknowledged).

8. **Register services in DI**: Add `services.AddScoped<IAiMetricsService, AiMetricsService>()` and `services.AddHostedService<AiMetricsCalculationJob>()` in `Program.cs`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   └── UPACIP.Service.csproj
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── BaseEntity.cs
│       │   ├── MedicalCode.cs
│       │   ├── ExtractedData.cs
│       │   ├── AiAccuracyMetric.cs    ← from task_001
│       │   ├── AiLatencyMetric.cs     ← from task_001
│       │   ├── AiMetricThreshold.cs   ← from task_001
│       │   └── AiMetricAlert.cs       ← from task_001
│       └── Enums/
│           ├── AiMetricType.cs        ← from task_001
│           ├── AiOperationType.cs     ← from task_001
│           └── TrendDirection.cs      ← from task_001
├── app/
└── scripts/
```

> Assumes task_001 (AI metrics schema) is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/AiMetrics/IAiMetricsService.cs | Service interface with GetCurrentSummary, GetTimeSeries, GetActiveAlerts, RunDailyAggregation |
| CREATE | src/UPACIP.Service/AiMetrics/AiMetricsService.cs | Implementation: accuracy calc from MedicalCode/ExtractedData, latency aggregation, trend |
| CREATE | src/UPACIP.Service/AiMetrics/AiMetricsCalculationJob.cs | IHostedService for daily metrics aggregation and threshold-based alert generation |
| CREATE | src/UPACIP.Service/AiMetrics/Dtos/AiMetricsSummaryDto.cs | DTO: agreement rate, precision, recall, latencies, sample sizes, alerts |
| CREATE | src/UPACIP.Service/AiMetrics/Dtos/AiMetricsTimeSeriesDto.cs | DTO: date-series arrays for chart data with granularity support |
| CREATE | src/UPACIP.Service/AiMetrics/Dtos/AiMetricAlertDto.cs | DTO: alert details with metric name, current/target values, trend |
| CREATE | src/UPACIP.Api/Controllers/Admin/AiMetricsController.cs | Admin-authorized REST endpoints: GET summary, GET time-series, GET alerts, PUT acknowledge |
| MODIFY | src/UPACIP.Api/Program.cs | Register IAiMetricsService and AiMetricsCalculationJob in DI container |

## External References

- [ASP.NET Core Background Services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0)
- [ASP.NET Core Web API Controllers](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [EF Core Querying](https://learn.microsoft.com/en-us/ef/core/querying/)
- [Role-based Authorization in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-8.0)
- [Percentile Calculation Algorithms](https://en.wikipedia.org/wiki/Percentile)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for UPACIP.Service and UPACIP.Api projects
- [ ] `IAiMetricsService` interface defines all 4 methods matching the implementation plan
- [ ] `AiMetricsService` correctly queries `MedicalCode` for AI-human agreement rate calculation
- [ ] `AiMetricsService` correctly queries `ExtractedData` for precision/recall calculation
- [ ] `AiMetricsService` returns "Insufficient data" indicator when sample size < 30
- [ ] `AiMetricsCalculationJob` generates `AiMetricAlert` records when metrics breach thresholds
- [ ] `AiMetricsController` enforces `[Authorize(Roles = "Admin")]` on all endpoints
- [ ] Time-series endpoint supports `daily`, `weekly`, `monthly` granularity parameters

## Implementation Checklist

- [x] Create `AiMetricsSummaryDto`, `AiMetricsTimeSeriesDto`, `AiMetricAlertDto` DTOs with all required properties
- [x] Define `IAiMetricsService` interface with `GetCurrentSummaryAsync`, `GetTimeSeriesAsync`, `GetActiveAlertsAsync`, `RunDailyAggregationAsync`
- [x] Implement accuracy calculation: AI-human agreement rate from `MedicalCode` (AI-suggested vs staff-approved), precision/recall from `ExtractedData` (flagged vs verified)
- [x] Implement latency aggregation: P50/P95 percentile calculation from AI Gateway request logs grouped by operation type
- [x] Implement trend direction: compare current vs previous day metric values with ±1% threshold
- [x] Implement `AiMetricsCalculationJob` as `IHostedService` with daily `PeriodicTimer`, threshold evaluation, and `AiMetricAlert` generation per AC-4
- [x] Create `AiMetricsController` with admin-authorized endpoints: `GET /summary`, `GET /time-series`, `GET /alerts`, `PUT /alerts/{id}/acknowledge`
- [x] Register `IAiMetricsService` and `AiMetricsCalculationJob` in `Program.cs` DI container
