# Task - TASK_002_BE_AI_COST_MONITORING

## Requirement Reference

- User Story: us_071
- Story Location: .propel/context/tasks/EP-012/us_071/us_071.md
- Acceptance Criteria:
    - AC-1: **Given** AI requests are processed, **When** the daily aggregation runs, **Then** the system calculates total token usage and estimated cost by provider and request type.
    - AC-2: **Given** AI cost exceeds the configured budget threshold, **When** the threshold is breached, **Then** the system generates an alert notification to administrators.
- Edge Case:
    - What happens when cost data is unavailable from a provider's API? System estimates cost using token count × configured rate card; estimates are flagged as "approximate."

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
| Backend | .NET (ASP.NET Core Web API) | 8.x |
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Caching | Upstash Redis | 7.x |
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

Implement the backend service for daily AI cost aggregation, budget threshold monitoring, and administrator alert notifications. This service reads per-request cost logs (populated by the AI Gateway), aggregates them into daily summaries by provider and request type, compares totals against configurable budget thresholds, and dispatches alert notifications when thresholds are breached. Includes fallback cost estimation using rate card when provider cost data is unavailable.

## Dependent Tasks

- task_001_db_ai_cost_tracking_schema — Requires AI cost tracking tables and seed data
- US_067 (EP-012) — Requires AI Gateway service foundation
- US_004 (EP-TECH) — Requires Redis infrastructure

## Impacted Components

- **NEW** - `IAiCostAggregationService` interface (Server project, Services/AiCost)
- **NEW** - `AiCostAggregationService` implementation (Server project, Services/AiCost)
- **NEW** - `IAiCostAlertService` interface (Server project, Services/AiCost)
- **NEW** - `AiCostAlertService` implementation (Server project, Services/AiCost)
- **NEW** - `AiCostAggregationJob` background worker (Server project, BackgroundJobs)
- **MODIFY** - `Program.cs` / DI registration — Register new services and hosted background job

## Implementation Plan

1. **Implement `IAiCostAggregationService`** — Service that queries `AiRequestLog` entries for a given date range, groups by provider and request type, calculates total token usage (input + output) and total estimated cost. When cost_source is `Approximate`, applies rate card pricing from `AiCostBudgetConfig` (token count × cost_per_1k_tokens / 1000). Persists results to `AiCostDailySummary` table using upsert logic (insert or update if summary already exists for the date/provider/request_type combination).

2. **Implement cost estimation fallback** — When a request log entry has cost_source = `Approximate`, the service reads the matching provider's rate card from `AiCostBudgetConfig` and calculates: `estimated_cost = (input_tokens × cost_per_1k_input / 1000) + (output_tokens × cost_per_1k_output / 1000)`. The daily summary flags entries that include approximate estimates.

3. **Implement `IAiCostAlertService`** — Service that compares the current day's aggregated cost per provider against `daily_budget_threshold` from `AiCostBudgetConfig`. When threshold is breached and `alert_enabled = true`, generates a structured admin notification with: provider name, threshold amount, actual cost, percentage over threshold, and timestamp. Alert notifications are dispatched via the existing notification infrastructure (see EP-005).

4. **Implement `AiCostAggregationJob` as `BackgroundService`** — Hosted service that runs daily cost aggregation. Configurable schedule via `appsettings.json` (default: daily at 00:05 UTC). On each run: (a) aggregate previous day's costs, (b) check budget thresholds, (c) dispatch alerts if breached. Includes Serilog structured logging with correlation ID for each run.

5. **Implement real-time threshold check** — In addition to the daily batch, provide a method that can be called after each AI request to check if the running daily total has crossed the threshold (for near-real-time alerting). Uses Redis to cache the running daily cost total with 5-minute TTL to avoid excessive DB reads.

6. **Register services in DI container** — Add service registrations and background job configuration in `Program.cs`. Configure schedule from `appsettings.json` under `AiCost:AggregationSchedule`.

### Pseudocode: Daily Cost Aggregation

```pseudocode
function AggregateDailyCosts(targetDate):
    requestLogs = db.AiRequestLogs
        .Where(log => log.CreatedAt.Date == targetDate)
        .GroupBy(log => (log.Provider, log.RequestType))

    for each group in requestLogs:
        totalInputTokens = group.Sum(log => log.InputTokens)
        totalOutputTokens = group.Sum(log => log.OutputTokens)
        totalCost = group.Sum(log => log.EstimatedCost)

        // Recalculate approximate entries using rate card
        approxLogs = group.Where(log => log.CostSource == Approximate)
        for each log in approxLogs:
            rateCard = db.AiCostBudgetConfig.First(c => c.Provider == log.Provider)
            log.EstimatedCost = (log.InputTokens * rateCard.CostPer1kInput / 1000)
                              + (log.OutputTokens * rateCard.CostPer1kOutput / 1000)

        summary = UpsertDailySummary(targetDate, group.Key, totals)

    // Threshold check
    for each provider in distinctProviders:
        config = db.AiCostBudgetConfig.First(c => c.Provider == provider)
        dailyTotal = db.AiCostDailySummary
            .Where(s => s.SummaryDate == targetDate && s.Provider == provider)
            .Sum(s => s.TotalEstimatedCost)

        if dailyTotal > config.DailyBudgetThreshold AND config.AlertEnabled:
            alertService.SendBudgetBreachAlert(provider, config.Threshold, dailyTotal)
```

## Current Project State

- [Placeholder — to be updated during execution based on dependent task completion]

```text
Server/
├── Services/
│   └── ... (existing services)
├── BackgroundJobs/
│   └── ... (existing background workers)
├── Data/
│   ├── Entities/
│   │   ├── AiRequestLog.cs         (from task_001)
│   │   ├── AiCostBudgetConfig.cs   (from task_001)
│   │   └── AiCostDailySummary.cs   (from task_001)
│   └── ApplicationDbContext.cs
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/AiCost/IAiCostAggregationService.cs | Interface for daily cost aggregation operations |
| CREATE | Server/Services/AiCost/AiCostAggregationService.cs | Implementation: aggregate token usage, calculate costs, upsert daily summaries |
| CREATE | Server/Services/AiCost/IAiCostAlertService.cs | Interface for budget threshold alert operations |
| CREATE | Server/Services/AiCost/AiCostAlertService.cs | Implementation: threshold comparison, admin alert dispatch |
| CREATE | Server/BackgroundJobs/AiCostAggregationJob.cs | Hosted BackgroundService for scheduled daily aggregation |
| MODIFY | Server/Program.cs | Register AiCost services and AiCostAggregationJob hosted service in DI |
| MODIFY | Server/appsettings.json | Add AiCost:AggregationSchedule configuration section |

## External References

- [.NET 8 BackgroundService — Official Docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0)
- [EF Core 8 GroupBy Queries](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators#groupby)
- [Serilog Structured Logging](https://serilog.net/)
- [OpenAI Token Usage API Response](https://platform.openai.com/docs/api-reference/chat/object) — `usage.prompt_tokens`, `usage.completion_tokens`

## Build Commands

- `dotnet build Server/`
- `dotnet test Server.Tests/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Daily aggregation correctly sums token usage grouped by provider and request type
- [ ] Approximate cost recalculation produces correct values from rate card
- [ ] Budget threshold breach triggers alert notification
- [ ] Background job runs on configured schedule
- [ ] Redis-cached running total invalidates correctly

## Implementation Checklist

- [ ] Create `IAiCostAggregationService` with `AggregateDailyCostsAsync(DateOnly targetDate)` and `GetRunningDailyCostAsync(string provider)` methods
- [ ] Implement `AiCostAggregationService` — query `AiRequestLog`, group by provider/request_type, calculate totals, apply rate card fallback for approximate entries, upsert to `AiCostDailySummary`
- [ ] Create `IAiCostAlertService` with `CheckBudgetThresholdsAsync(DateOnly targetDate)` and `SendBudgetBreachAlertAsync(provider, threshold, actualCost)` methods
- [ ] Implement `AiCostAlertService` — compare daily totals against `AiCostBudgetConfig` thresholds, generate structured admin notifications with provider, threshold, actual cost, and percentage exceeded
- [ ] Implement `AiCostAggregationJob` as `BackgroundService` — configurable schedule (default daily 00:05 UTC), runs aggregation then threshold check, structured Serilog logging with correlation ID
- [ ] Implement real-time threshold check using Redis-cached running daily cost total (5-min TTL) to support near-real-time alerting after each AI request
- [ ] Register all services and hosted job in `Program.cs` DI container; add `AiCost` configuration section to `appsettings.json`
