# Task - TASK_001_DB_AI_COST_TRACKING_SCHEMA

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

Create the database schema and EF Core migrations to support AI cost tracking, budget configuration, and daily cost summaries. This task establishes the persistence layer required by the cost monitoring service (task_002) and the AI Gateway integration hooks (task_004). Tables store per-request AI cost logs, configurable budget thresholds per provider, and pre-aggregated daily cost summaries for efficient dashboard queries and threshold checks.

## Dependent Tasks

- US_004 (EP-TECH) - Requires Redis caching infrastructure
- US_067 task completion - Requires AI Gateway service scaffold with provider abstraction

## Impacted Components

- **NEW** - `AiRequestLog` entity model (Server project, Data/Entities)
- **NEW** - `AiCostBudgetConfig` entity model (Server project, Data/Entities)
- **NEW** - `AiCostDailySummary` entity model (Server project, Data/Entities)
- **MODIFY** - `ApplicationDbContext` (Server project, Data) - Register new DbSets
- **NEW** - EF Core migration for AI cost tracking tables

## Implementation Plan

1. **Define `AiRequestLog` entity** — Stores individual AI request cost data including provider, request type, token counts (input/output), estimated cost, cost source (actual vs. approximate via rate card), and correlation to the originating request. Uses UUID primary key, timestamp indexing, and enum mappings for provider and request type.

2. **Define `AiCostBudgetConfig` entity** — Stores per-provider daily budget thresholds with alert toggle. Supports rate card pricing (cost per 1K input tokens, cost per 1K output tokens) used as fallback when provider cost data is unavailable. Uses unique constraint on provider name.

3. **Define `AiCostDailySummary` entity** — Pre-aggregated daily cost rollup by provider and request type. Stores total input tokens, total output tokens, total estimated cost, and record count. Unique composite index on (summary_date, provider, request_type) prevents duplicate aggregations.

4. **Register entities in `ApplicationDbContext`** — Add `DbSet<AiRequestLog>`, `DbSet<AiCostBudgetConfig>`, `DbSet<AiCostDailySummary>` to the context. Configure entity relationships, indexes, enums, and constraints via Fluent API.

5. **Generate and validate EF Core migration** — Create code-first migration with rollback support. Verify migration applies cleanly against a fresh database and rolls back without data loss.

6. **Seed default budget configuration** — Insert default rate card entries for OpenAI GPT-4o-mini ($0.15/1M input, $0.60/1M output) with $5.00/day budget threshold, and Anthropic Claude 3.5 Sonnet ($3.00/1M input, $15.00/1M output) with $20.00/day budget threshold. Both entries seeded with `alert_enabled = true`.

## Current Project State

- [Placeholder — to be updated during execution based on dependent task completion]

```text
Server/
├── Data/
│   ├── Entities/
│   │   ├── Patient.cs
│   │   ├── Appointment.cs
│   │   ├── AuditLog.cs
│   │   └── ... (existing entities)
│   ├── ApplicationDbContext.cs
│   └── Migrations/
└── ...
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Data/Entities/AiRequestLog.cs | Entity for per-request AI cost tracking with provider, token counts, estimated cost |
| CREATE | Server/Data/Entities/AiCostBudgetConfig.cs | Entity for per-provider budget thresholds and rate card pricing |
| CREATE | Server/Data/Entities/AiCostDailySummary.cs | Entity for pre-aggregated daily cost rollup by provider and request type |
| MODIFY | Server/Data/ApplicationDbContext.cs | Register new DbSets and Fluent API configuration for AI cost entities |
| CREATE | Server/Data/Migrations/{timestamp}_AddAiCostTrackingTables.cs | EF Core migration for new tables, indexes, and constraints |
| CREATE | Server/Data/Seed/AiCostBudgetConfigSeed.cs | Default rate card and budget threshold seed data |

## External References

- [EF Core 8 Migrations — Official Docs](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [PostgreSQL 16 CREATE TABLE](https://www.postgresql.org/docs/16/sql-createtable.html)
- [EF Core Value Conversions for Enums](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [OpenAI Pricing Reference](https://openai.com/api/pricing/) — GPT-4o-mini: $0.15/1M input, $0.60/1M output
- [Anthropic Claude Pricing](https://www.anthropic.com/pricing) — Claude 3.5 Sonnet: $3.00/1M input, $15.00/1M output

## Build Commands

- `dotnet ef migrations add AddAiCostTrackingTables --project Server`
- `dotnet ef database update --project Server`
- `dotnet build Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Migration applies cleanly to fresh database
- [ ] Migration rolls back without errors
- [ ] Seed data inserts successfully
- [ ] All foreign key and unique constraints enforced at DB level

## Implementation Checklist

- [ ] Create `AiRequestLog` entity with UUID PK, provider enum, request_type enum, input_tokens (int), output_tokens (int), estimated_cost (decimal), cost_source enum (Actual/Approximate), correlation_id (Guid), created_at (timestamp with timezone)
- [ ] Create `AiCostBudgetConfig` entity with provider (unique), daily_budget_threshold (decimal), alert_enabled (bool), cost_per_1k_input_tokens (decimal), cost_per_1k_output_tokens (decimal), updated_at (timestamp)
- [ ] Create `AiCostDailySummary` entity with summary_date (date), provider enum, request_type enum, total_input_tokens (long), total_output_tokens (long), total_estimated_cost (decimal), request_count (int), created_at (timestamp) — unique composite index on (summary_date, provider, request_type)
- [ ] Register all three DbSets in `ApplicationDbContext` with Fluent API configuration (indexes, enum conversions, precision for decimal fields)
- [ ] Generate EF Core migration `AddAiCostTrackingTables` with rollback support (Down method)
- [ ] Add seed data for default provider rate cards (GPT-4o-mini, Claude 3.5 Sonnet) with initial daily budget thresholds
