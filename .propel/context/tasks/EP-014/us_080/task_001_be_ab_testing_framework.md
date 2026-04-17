# Task - task_001_be_ab_testing_framework

## Requirement Reference

- User Story: us_080
- Story Location: .propel/context/tasks/EP-014/us_080/us_080.md
- Acceptance Criteria:
  - AC-1: Given a new model version is deployed, When A/B testing is configured, Then a configurable percentage of requests are routed to the new model while the rest use the current model.
  - AC-2: Given A/B testing is active, When results are collected, Then the system tracks accuracy, latency, and cost metrics separately for each model version.
- Edge Case:
  - What happens when A/B results show the new model is significantly worse? Admin can immediately terminate the A/B test and route 100% of traffic back to the current model.

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
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| AI Gateway | Custom .NET Service with Polly | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-O10 |
| **AI Pattern** | Hybrid |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | OpenAI GPT-4o-mini (primary), Anthropic Claude 3.5 Sonnet (fallback) |

### **CRITICAL: AI Implementation Requirement (AI Tasks Only)**

**IF AI Impact = Yes:**

- **MUST** reference prompt templates from Prompt Template Path during implementation
- **MUST** implement guardrails for input sanitization and output validation
- **MUST** enforce token budget limits per AIR-O01 requirements
- **MUST** implement fallback logic for low-confidence responses
- **MUST** log all prompts/responses for audit (redact PII)
- **MUST** handle model failures gracefully (timeout, rate limit, 5xx errors)

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement an A/B testing framework (`IAbTestingService` / `AbTestingService`) in the UPACIP.Service project that enables controlled traffic splitting between AI model versions per AIR-O10 and TR-014. The framework integrates with the AI Gateway to route a configurable percentage of requests to a candidate model version while the remainder uses the current (control) model. Each A/B test experiment is defined with: a unique experiment ID, control model identifier, candidate model identifier, traffic split percentage (0–100), start/end dates, and status (Active, Paused, Terminated, Completed). Request routing uses a deterministic hash of the user ID to ensure session-consistent assignment (a given user always hits the same variant during an experiment). The framework tracks three core metrics per variant: accuracy (correctness of AI output as rated by downstream validators), latency (end-to-end inference time in milliseconds), and cost (token usage × per-token rate). An `AbTestingMiddleware` integrates into the AI Gateway pipeline to intercept requests, assign variants, and record metrics. Admin endpoints allow creating, pausing, and terminating experiments — with immediate termination routing 100% of traffic back to the control model (edge case). Experiment results are stored in PostgreSQL and queryable via an admin API.

## Dependent Tasks

- US_067 — Requires AI Gateway service for request routing and provider abstraction.
- US_008 task_001_be_domain_entity_models — Requires AuditLog entity for logging experiment assignments.

## Impacted Components

- **NEW** `src/UPACIP.Service/AiTesting/IAbTestingService.cs` — Interface defining CreateExperimentAsync, GetActiveExperimentAsync, AssignVariantAsync, RecordMetricAsync, TerminateExperimentAsync, GetExperimentResultsAsync
- **NEW** `src/UPACIP.Service/AiTesting/AbTestingService.cs` — Implementation: deterministic variant assignment, metric recording, experiment lifecycle management
- **NEW** `src/UPACIP.Service/AiTesting/Models/AbExperiment.cs` — Experiment definition: Id, ControlModel, CandidateModel, TrafficSplitPercentage, Status, StartDate, EndDate
- **NEW** `src/UPACIP.Service/AiTesting/Models/AbVariantAssignment.cs` — Per-request assignment: ExperimentId, UserId, AssignedVariant (Control/Candidate), Timestamp
- **NEW** `src/UPACIP.Service/AiTesting/Models/AbMetricRecord.cs` — Per-request metric: ExperimentId, Variant, Accuracy, LatencyMs, TokensUsed, EstimatedCost, RequestType
- **NEW** `src/UPACIP.Service/AiTesting/Models/AbExperimentResult.cs` — Aggregated results: ControlMetrics, CandidateMetrics, SampleSizes, StatisticalSignificance
- **NEW** `src/UPACIP.Service/AiTesting/AbTestingMiddleware.cs` — AI Gateway middleware: intercept request, assign variant, route to model, record metrics
- **NEW** `src/UPACIP.DataAccess/Entities/AbExperimentEntity.cs` — EF Core entity for ab_experiments table
- **NEW** `src/UPACIP.DataAccess/Entities/AbMetricRecordEntity.cs` — EF Core entity for ab_metric_records table
- **NEW** `src/UPACIP.DataAccess/Configurations/AbExperimentConfiguration.cs` — EF Core configuration with indexes
- **NEW** `src/UPACIP.DataAccess/Configurations/AbMetricRecordConfiguration.cs` — EF Core configuration with indexes on ExperimentId, Variant, CreatedAt
- **NEW** `src/UPACIP.Api/Controllers/Admin/AbTestingController.cs` — Admin endpoints for experiment CRUD and results
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet for AbExperiment and AbMetricRecord
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IAbTestingService, add AbTestingMiddleware to AI Gateway pipeline

## Implementation Plan

1. **Define experiment and metric models**: Create `AbExperiment` with: `Guid Id`, `string ControlModelId` (e.g., "gpt-4o-mini-2024-07-18"), `string CandidateModelId` (e.g., "gpt-4o-mini-2025-01-31"), `int TrafficSplitPercentage` (0–100, percentage routed to candidate), `AbExperimentStatus Status` (enum: Active, Paused, Terminated, Completed), `DateTime StartDate`, `DateTime? EndDate`, `string Description`, `string CreatedByUserId`. Create `AbVariantAssignment` with: `Guid ExperimentId`, `string UserId`, `AbVariant AssignedVariant` (enum: Control, Candidate), `DateTime AssignedAt`. Create `AbMetricRecord` with: `Guid Id`, `Guid ExperimentId`, `AbVariant Variant`, `float? Accuracy` (0–1, null if not yet evaluated), `long LatencyMs`, `int TokensUsed` (input + output), `decimal EstimatedCost` (tokens × per-token rate), `string RequestType` (e.g., "document-parsing", "conversational-intake", "medical-coding"), `DateTime CreatedAt`. Create `AbExperimentResult` with: `Guid ExperimentId`, `AbVariantMetrics ControlMetrics`, `AbVariantMetrics CandidateMetrics`, `int ControlSampleSize`, `int CandidateSampleSize`, `bool IsStatisticallySignificant` (p < 0.05 based on sample sizes). Create `AbVariantMetrics` with: `float MeanAccuracy`, `float MedianLatencyMs`, `float P95LatencyMs`, `decimal TotalCost`, `decimal AverageCostPerRequest`.

2. **Define EF Core entities and configurations**: Create `AbExperimentEntity` mapped to `ab_experiments` table with columns matching `AbExperiment` fields. Create `AbMetricRecordEntity` mapped to `ab_metric_records` table. Configure `AbExperimentConfiguration`: unique index on `Status` filtered to Active (only one active experiment at a time), index on `StartDate`. Configure `AbMetricRecordConfiguration`: composite index on `(ExperimentId, Variant, CreatedAt)` for efficient metric queries, index on `ExperimentId` alone for join lookups. Add `DbSet<AbExperimentEntity>` and `DbSet<AbMetricRecordEntity>` to `ApplicationDbContext`.

3. **Define `IAbTestingService` interface**: Methods:
   - `Task<AbExperiment> CreateExperimentAsync(AbExperiment experiment, CancellationToken ct)` — Creates and activates a new experiment (deactivates any existing active experiment first).
   - `Task<AbExperiment?> GetActiveExperimentAsync(CancellationToken ct)` — Returns the currently active experiment or null.
   - `Task<AbVariant> AssignVariantAsync(Guid experimentId, string userId, CancellationToken ct)` — Deterministically assigns a user to Control or Candidate.
   - `Task RecordMetricAsync(AbMetricRecord metric, CancellationToken ct)` — Records a per-request metric data point.
   - `Task TerminateExperimentAsync(Guid experimentId, CancellationToken ct)` — Immediately sets status to Terminated and routes all traffic to control.
   - `Task<AbExperimentResult> GetExperimentResultsAsync(Guid experimentId, CancellationToken ct)` — Aggregates metrics into comparison results.

4. **Implement deterministic variant assignment**: In `AssignVariantAsync`, compute a deterministic hash: `hash = SHA256(experimentId + userId)`. Take the first 4 bytes as a uint32 and compute `percentage = hash % 100`. If `percentage < experiment.TrafficSplitPercentage`, assign `Candidate`; otherwise assign `Control`. This ensures: (a) the same user always gets the same variant within an experiment (session consistency); (b) different experiments produce different assignments for the same user; (c) no Redis/DB state needed for assignment — purely computational. Cache the active experiment in Redis (`ab:active-experiment`, 60s TTL) to avoid per-request DB queries.

5. **Implement metric recording**: In `RecordMetricAsync`, write the `AbMetricRecord` to the database via EF Core. Calculate `EstimatedCost` from `TokensUsed` using configurable per-token rates stored in `appsettings.json`: `{ "AiCostRates": { "gpt-4o-mini": { "InputPer1MTokens": 0.15, "OutputPer1MTokens": 0.60 }, "claude-3.5-sonnet": { "InputPer1MTokens": 3.00, "OutputPer1MTokens": 15.00 } } }`. Use `IOptions<Dictionary<string, AiCostRate>>` for rate lookup. Batch metric inserts using `AddRangeAsync` if multiple metrics arrive within the same request scope.

6. **Implement experiment results aggregation**: In `GetExperimentResultsAsync`, query `AbMetricRecord` records grouped by `Variant`. Calculate per-variant: `MeanAccuracy` (average of non-null Accuracy values), `MedianLatencyMs` (middle value), `P95LatencyMs` (95th percentile), `TotalCost` (sum), `AverageCostPerRequest` (total / count). For `IsStatisticallySignificant`, use a simplified sample size check: require minimum 30 samples per variant and compute a basic z-test for accuracy difference. Return `AbExperimentResult` with both variant metrics and sample sizes.

7. **Implement `AbTestingMiddleware`**: Create an AI Gateway middleware that intercepts outbound AI requests. On each request: (a) call `GetActiveExperimentAsync` — if null, pass through unchanged (no active experiment); (b) extract `userId` from request context; (c) call `AssignVariantAsync` to determine variant; (d) if `Candidate`, override the model ID in the request to `CandidateModelId`; if `Control`, use `ControlModelId`; (e) start a `Stopwatch` before forwarding to the provider; (f) after response, record metric: latency from stopwatch, token count from response metadata, estimated cost, request type from request metadata; (g) add `X-Ab-Experiment: {experimentId}` and `X-Ab-Variant: {variant}` response headers for observability. The middleware executes after prompt sanitization and PII redaction but before the HTTP call to the AI provider.

8. **Implement admin API endpoints and experiment termination (edge case)**: Create `AbTestingController` with `[Authorize(Roles = "Admin")]` at route `/api/admin/ab-tests`:
   - `POST /api/admin/ab-tests` — Body: experiment definition. Validates: TrafficSplitPercentage 1–99 (not 0 or 100 — those aren't A/B tests), CandidateModelId differs from ControlModelId. Returns 201 with experiment details.
   - `GET /api/admin/ab-tests/{id}/results` — Returns aggregated metrics comparison. Includes sample sizes and statistical significance flag.
   - `POST /api/admin/ab-tests/{id}/terminate` — Immediately sets status to Terminated, clears Redis cache, and logs termination in audit trail. All subsequent requests route to control model. Returns 200 with termination confirmation.
   - `POST /api/admin/ab-tests/{id}/pause` — Pauses the experiment (routes all traffic to control without discarding data). Returns 200.
   - `GET /api/admin/ab-tests` — Lists all experiments with status filter query parameter. Returns paginated list.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── Admin/
│   │   │       ├── KnowledgeBaseRefreshController.cs ← from US_078
│   │   │       └── RateLimitAdminController.cs       ← from US_079
│   │   ├── Middleware/
│   │   │   └── AiRateLimitingMiddleware.cs            ← from US_079
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── AiSafety/
│   │   │   ├── IPromptInjectionDetector.cs            ← from US_079 task_001
│   │   │   ├── PromptInjectionDetector.cs             ← from US_079 task_001
│   │   │   ├── PromptSanitizationMiddleware.cs        ← from US_079 task_001
│   │   │   ├── IRagAccessControlFilter.cs             ← from US_079 task_002
│   │   │   ├── RagAccessControlFilter.cs              ← from US_079 task_002
│   │   │   ├── IContentFilterService.cs               ← from US_079 task_002
│   │   │   ├── ContentFilterService.cs                ← from US_079 task_002
│   │   │   ├── ContentFilterMiddleware.cs             ← from US_079 task_002
│   │   │   ├── IAiRateLimiter.cs                      ← from US_079 task_003
│   │   │   ├── AiRateLimiter.cs                       ← from US_079 task_003
│   │   │   ├── IPiiRedactionService.cs                ← from US_074
│   │   │   ├── PiiRedactionService.cs                 ← from US_074
│   │   │   ├── MedicalTermAllowlist.cs                ← from US_074
│   │   │   └── Models/
│   │   ├── Caching/
│   │   ├── VectorSearch/
│   │   └── Rag/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── AuditLog.cs                            ← from US_008
│       │   └── ...
│       └── Configurations/
│           ├── AuditLogConfiguration.cs               ← from US_008
│           └── ...
├── Server/
│   └── AI/
│       └── AiGatewayService.cs                        ← from US_067
├── app/
├── config/
└── scripts/
```

> Assumes US_067 (AI Gateway), US_008 (domain entities + AuditLog), US_079 (safety controls), and US_074 (PII redaction) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/AiTesting/IAbTestingService.cs | Interface: CreateExperimentAsync, AssignVariantAsync, RecordMetricAsync, TerminateExperimentAsync, GetExperimentResultsAsync |
| CREATE | src/UPACIP.Service/AiTesting/AbTestingService.cs | Deterministic hash-based variant assignment, metric recording, experiment lifecycle |
| CREATE | src/UPACIP.Service/AiTesting/Models/AbExperiment.cs | Experiment DTO: ControlModel, CandidateModel, TrafficSplitPercentage, Status |
| CREATE | src/UPACIP.Service/AiTesting/Models/AbVariantAssignment.cs | Assignment DTO: ExperimentId, UserId, AssignedVariant |
| CREATE | src/UPACIP.Service/AiTesting/Models/AbMetricRecord.cs | Metric DTO: Accuracy, LatencyMs, TokensUsed, EstimatedCost, RequestType |
| CREATE | src/UPACIP.Service/AiTesting/Models/AbExperimentResult.cs | Aggregated results: ControlMetrics, CandidateMetrics, SampleSizes, StatisticalSignificance |
| CREATE | src/UPACIP.Service/AiTesting/AbTestingMiddleware.cs | AI Gateway middleware: variant assignment, model override, metric recording |
| CREATE | src/UPACIP.DataAccess/Entities/AbExperimentEntity.cs | EF Core entity for ab_experiments table |
| CREATE | src/UPACIP.DataAccess/Entities/AbMetricRecordEntity.cs | EF Core entity for ab_metric_records table |
| CREATE | src/UPACIP.DataAccess/Configurations/AbExperimentConfiguration.cs | Indexes: filtered unique on Status=Active, StartDate |
| CREATE | src/UPACIP.DataAccess/Configurations/AbMetricRecordConfiguration.cs | Composite index: (ExperimentId, Variant, CreatedAt) |
| CREATE | src/UPACIP.Api/Controllers/Admin/AbTestingController.cs | Admin CRUD: create, terminate, pause, results, list experiments |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet for AbExperiment, AbMetricRecord |
| MODIFY | src/UPACIP.Api/Program.cs | Register IAbTestingService, add AbTestingMiddleware, bind AiCostRates config |

## External References

- [A/B Testing Statistical Significance — Sample Size Calculator](https://www.evanmiller.org/ab-testing/sample-size.html)
- [SHA-256 Deterministic Hashing in .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256)
- [ASP.NET Core Middleware Pipeline](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware)
- [EF Core — Filtered Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-filter)
- [OpenAI API — Model Versioning](https://platform.openai.com/docs/models)
- [Polly — Context and DelegatingHandler](https://github.com/App-vNext/Polly)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build DataAccess project (entities + migrations)
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Generate EF Core migration for A/B testing tables
dotnet ef migrations add AddAbTestingTables --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Build full solution
dotnet build UPACIP.sln

# Run API project
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for UPACIP.Service, UPACIP.DataAccess, and UPACIP.Api projects
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] EF Core migration creates ab_experiments and ab_metric_records tables with correct indexes
- [ ] Deterministic variant assignment returns the same variant for the same userId + experimentId
- [ ] TrafficSplitPercentage correctly routes approximate percentage to candidate (verified with 1000+ test hashes)
- [ ] Metrics record accuracy, latency, and cost separately per variant
- [ ] Admin terminate endpoint immediately routes 100% traffic to control model
- [ ] Admin pause endpoint stops routing to candidate without discarding collected metrics
- [ ] Only one experiment can be Active at a time (filtered unique index enforced)
- [ ] X-Ab-Experiment and X-Ab-Variant headers present on responses during active experiment

## Implementation Checklist

- [ ] Create `AbExperiment`, `AbVariantAssignment`, `AbMetricRecord`, and `AbExperimentResult` models in `src/UPACIP.Service/AiTesting/Models/`
- [ ] Create EF Core entities and configurations for ab_experiments and ab_metric_records tables
- [ ] Define `IAbTestingService` interface with experiment lifecycle and metric methods
- [ ] Implement deterministic SHA-256 hash-based variant assignment with Redis-cached active experiment
- [ ] Implement metric recording with configurable per-token cost rates from appsettings
- [ ] Implement experiment results aggregation with mean accuracy, P95 latency, total cost per variant
- [ ] Implement `AbTestingMiddleware` with model override, stopwatch timing, and metric recording
- [ ] Implement `AbTestingController` with create, terminate, pause, results, and list endpoints
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-O10 requirements are met (A/B testing with metric tracking)
