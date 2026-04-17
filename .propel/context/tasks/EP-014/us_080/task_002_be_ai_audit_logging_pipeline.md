# Task - task_002_be_ai_audit_logging_pipeline

## Requirement Reference

- User Story: us_080
- Story Location: .propel/context/tasks/EP-014/us_080/us_080.md
- Acceptance Criteria:
  - AC-3: Given any AI request is processed, When the request completes, Then the full prompt, response, model version, token count, latency, and confidence score are logged in the audit trail.
  - AC-4: Given audit logs contain AI interactions, When an admin queries the AI audit log, Then they can filter by date, model version, request type, and confidence range.
- Edge Case:
  - How does the system handle AI audit log storage for high-volume document parsing? Logs are stored in a separate partitioned table with 90-day hot storage and 1-year archived storage.

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
| Library | Serilog | 4.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-S04, AIR-O06 |
| **AI Pattern** | RAG |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A (logging infrastructure, captures data from all providers) |

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

Implement a comprehensive AI audit logging pipeline (`IAiAuditService` / `AiAuditService`) that captures all AI request/response interactions per AIR-S04 and Decision #5 (CQRS for Audit Log Access). The pipeline logs the full prompt text (post-PII-redaction), model response, model version, token count (input + output), latency, confidence score, request type, patient ID correlation, and A/B experiment variant (if active) into a dedicated `ai_audit_logs` table — separate from the general-purpose `audit_logs` table — to support high-volume document parsing workloads (edge case). The table uses PostgreSQL range partitioning by `created_at` month for efficient time-based queries, with automatic partition creation and a 90-day hot storage / 1-year archived retention policy. An `AiAuditLoggingMiddleware` integrates into the AI Gateway response pipeline to capture audit data after every completed request. An admin query API provides filtered access to audit records by date range, model version, request type, and confidence score range, with cursor-based pagination for large result sets. All prompts stored in the audit trail are post-PII-redaction (referencing `PiiRedactionMiddleware` from US_074), ensuring no patient identifying information is persisted in audit logs.

## Dependent Tasks

- US_008 task_001_be_domain_entity_models — Requires AuditLog entity pattern and ApplicationDbContext.
- US_067 — Requires AI Gateway service for middleware pipeline integration.
- US_074 task_001_be_pii_redaction_pipeline — Requires PII redaction to ensure prompts are redacted before audit logging.
- US_080 task_001_be_ab_testing_framework — Requires A/B experiment variant metadata for correlation.

## Impacted Components

- **NEW** `src/UPACIP.Service/AiAudit/IAiAuditService.cs` — Interface defining LogAiInteractionAsync, QueryAuditLogsAsync methods
- **NEW** `src/UPACIP.Service/AiAudit/AiAuditService.cs` — Implementation: structured log writing, query with filtering and pagination
- **NEW** `src/UPACIP.Service/AiAudit/Models/AiAuditLogEntry.cs` — DTO: Prompt, Response, ModelVersion, TokenCount, LatencyMs, ConfidenceScore, RequestType, PatientId, AbVariant
- **NEW** `src/UPACIP.Service/AiAudit/Models/AiAuditQueryFilter.cs` — Filter DTO: DateFrom, DateTo, ModelVersion, RequestType, ConfidenceMin, ConfidenceMax, Cursor, PageSize
- **NEW** `src/UPACIP.Service/AiAudit/Models/AiAuditQueryResult.cs` — Paginated result: Items, NextCursor, TotalCount, HasMore
- **NEW** `src/UPACIP.DataAccess/Entities/AiAuditLogEntity.cs` — EF Core entity for ai_audit_logs partitioned table
- **NEW** `src/UPACIP.DataAccess/Configurations/AiAuditLogConfiguration.cs` — EF Core configuration with indexes on created_at, model_version, request_type, confidence_score
- **NEW** `src/UPACIP.Service/AiAudit/AiAuditLoggingMiddleware.cs` — AI Gateway middleware: capture prompt, response, timing, and write audit entry
- **NEW** `src/UPACIP.Api/Controllers/Admin/AiAuditController.cs` — Admin query endpoint with date, model, type, confidence filters
- **NEW** `scripts/create-ai-audit-partitions.sql` — SQL script for range partitioning by month and partition maintenance
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet for AiAuditLogEntity
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IAiAuditService, add AiAuditLoggingMiddleware to AI Gateway pipeline

## Implementation Plan

1. **Define AI audit log entity and DTO models**: Create `AiAuditLogEntry` with: `Guid Id`, `string Prompt` (post-PII-redaction text), `string Response` (AI model output), `string ModelVersion` (e.g., "gpt-4o-mini-2024-07-18"), `int InputTokens`, `int OutputTokens`, `int TotalTokens`, `long LatencyMs`, `float? ConfidenceScore` (0–1, null for requests without confidence), `string RequestType` (e.g., "document-parsing", "conversational-intake", "medical-coding", "rag-retrieval"), `Guid? PatientId` (correlation for compliance review per AIR-S04), `string? AbExperimentId` (from task_001, null if no active experiment), `string? AbVariant` ("Control" or "Candidate", null if no experiment), `string UserId`, `DateTime CreatedAt`. Create `AiAuditLogEntity` EF Core entity mirroring the DTO with snake_case column mapping.

2. **Create partitioned table schema**: Create `scripts/create-ai-audit-partitions.sql` with PostgreSQL range partitioning:
   ```sql
   CREATE TABLE ai_audit_logs (
       id UUID NOT NULL,
       prompt TEXT NOT NULL,
       response TEXT NOT NULL,
       model_version VARCHAR(100) NOT NULL,
       input_tokens INT NOT NULL,
       output_tokens INT NOT NULL,
       total_tokens INT NOT NULL,
       latency_ms BIGINT NOT NULL,
       confidence_score REAL,
       request_type VARCHAR(50) NOT NULL,
       patient_id UUID,
       ab_experiment_id UUID,
       ab_variant VARCHAR(20),
       user_id VARCHAR(450) NOT NULL,
       created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
   ) PARTITION BY RANGE (created_at);
   ```
   Create initial monthly partitions for the current and next 3 months. Add a cron-based partition creation script that generates the next month's partition on the 25th of each month. Add indexes: `created_at` (range scans), `model_version` (filter), `request_type` (filter), `confidence_score` (range filter), composite `(patient_id, created_at)` for patient-correlated queries. Each partition inherits indexes.

3. **Implement retention policy (edge case)**: Add two SQL scripts for retention management:
   - **Hot storage (90 days)**: Partitions older than 90 days are detached from the parent table and moved to an `ai_audit_logs_archive` schema using `ALTER TABLE ... DETACH PARTITION`. Archived partitions remain queryable via a view union for compliance queries.
   - **Cold delete (1 year)**: Partitions older than 1 year are dropped entirely using `DROP TABLE`. This avoids row-by-row DELETE operations, leveraging partition-level DDL for instant cleanup.
   Create a scheduled SQL function `maintain_ai_audit_partitions()` that: (a) creates the next month's partition if it doesn't exist; (b) detaches partitions older than 90 days into archive; (c) drops partitions older than 1 year. Document that this function should be called by a database cron (pg_cron) or Windows Task Scheduler.

4. **Configure EF Core entity mapping**: Create `AiAuditLogConfiguration` implementing `IEntityTypeConfiguration<AiAuditLogEntity>`. Map to `ai_audit_logs` table (EF Core treats it as a regular table — partitioning is transparent). Configure indexes on `CreatedAt`, `ModelVersion`, `RequestType`, `ConfidenceScore`. Set `Prompt` and `Response` as `text` columns (no length limit). Register `DbSet<AiAuditLogEntity> AiAuditLogs` in `ApplicationDbContext`.

5. **Define `IAiAuditService` interface**: Methods:
   - `Task LogAiInteractionAsync(AiAuditLogEntry entry, CancellationToken ct)` — Writes a single audit log entry to the database.
   - `Task<AiAuditQueryResult> QueryAuditLogsAsync(AiAuditQueryFilter filter, CancellationToken ct)` — Queries audit logs with filtering and cursor-based pagination.

6. **Implement `AiAuditService`**: In `LogAiInteractionAsync`, map the `AiAuditLogEntry` DTO to `AiAuditLogEntity`, assign a new GUID, set `CreatedAt = DateTime.UtcNow`, and save via `DbContext.AddAsync` + `SaveChangesAsync`. Use fire-and-forget pattern: enqueue the write to a `Channel<AiAuditLogEntry>` (bounded, capacity 1000) and process in a background `BackgroundService` consumer to avoid blocking the AI request pipeline. If the channel is full (back-pressure), log a warning via Serilog and drop the entry (audit logging must never block AI responses). In `QueryAuditLogsAsync`, build an EF Core `IQueryable<AiAuditLogEntity>` with conditional filters: `DateFrom`/`DateTo` on `CreatedAt`, `ModelVersion` exact match, `RequestType` exact match, `ConfidenceMin`/`ConfidenceMax` range on `ConfidenceScore`. Implement cursor-based pagination using `CreatedAt` + `Id` as the cursor key (cursor = base64-encoded `{CreatedAt}|{Id}`). Return `PageSize` items (default 50, max 200) ordered by `CreatedAt DESC`, with `NextCursor` and `HasMore` flag.

7. **Implement `AiAuditLoggingMiddleware`**: Create an AI Gateway middleware positioned after the LLM call and content filter but before the response is returned. On each completed AI request: (a) capture the prompt text from the request context (post-PII-redaction — the middleware reads the redacted version, not the original); (b) capture the response text from the AI provider; (c) extract model version from the provider response metadata; (d) extract token counts (input + output) from the provider usage metadata; (e) calculate latency from the request-scoped stopwatch (started by `AbTestingMiddleware` or initialized here if no A/B test active); (f) extract confidence score from the response if present (some request types produce confidence scores, others don't); (g) extract request type from request metadata; (h) extract patient ID from request context if available; (i) extract A/B experiment and variant from response headers if present; (j) construct `AiAuditLogEntry` and call `IAiAuditService.LogAiInteractionAsync`. The middleware must handle exceptions gracefully — if audit logging fails, log the failure via Serilog but do not fail the AI request.

8. **Implement admin query API**: Create `AiAuditController` with `[Authorize(Roles = "Admin")]` at route `/api/admin/ai-audit`:
   - `GET /api/admin/ai-audit` — Query parameters: `dateFrom` (ISO 8601), `dateTo` (ISO 8601), `modelVersion` (string), `requestType` (string), `confidenceMin` (float), `confidenceMax` (float), `cursor` (string, opaque), `pageSize` (int, default 50, max 200). Returns paginated `AiAuditQueryResult` with audit entries, next cursor, and total count.
   - `GET /api/admin/ai-audit/{id}` — Returns a single audit log entry by ID for detailed inspection.
   - `GET /api/admin/ai-audit/summary` — Returns aggregated summary: total requests per model version, average latency per request type, average confidence per request type, total tokens consumed per model version. Date range filter required. Uses GROUP BY queries for efficiency.
   Validate that `dateFrom` <= `dateTo`, `confidenceMin` <= `confidenceMax`, `pageSize` within bounds. Return 400 for invalid filter combinations.

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
│   │   │       ├── RateLimitAdminController.cs       ← from US_079
│   │   │       └── AbTestingController.cs            ← from task_001
│   │   ├── Middleware/
│   │   │   └── AiRateLimitingMiddleware.cs            ← from US_079
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── AiSafety/
│   │   │   ├── IPromptInjectionDetector.cs            ← from US_079
│   │   │   ├── PromptSanitizationMiddleware.cs        ← from US_079
│   │   │   ├── IRagAccessControlFilter.cs             ← from US_079
│   │   │   ├── IContentFilterService.cs               ← from US_079
│   │   │   ├── ContentFilterMiddleware.cs             ← from US_079
│   │   │   ├── IAiRateLimiter.cs                      ← from US_079
│   │   │   ├── IPiiRedactionService.cs                ← from US_074
│   │   │   ├── PiiRedactionMiddleware.cs              ← from US_074
│   │   │   └── Models/
│   │   ├── AiTesting/
│   │   │   ├── IAbTestingService.cs                   ← from task_001
│   │   │   ├── AbTestingService.cs                    ← from task_001
│   │   │   ├── AbTestingMiddleware.cs                 ← from task_001
│   │   │   └── Models/
│   │   │       ├── AbExperiment.cs
│   │   │       ├── AbMetricRecord.cs
│   │   │       └── AbExperimentResult.cs
│   │   ├── Caching/
│   │   ├── VectorSearch/
│   │   └── Rag/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── AuditLog.cs                            ← from US_008
│       │   ├── AbExperimentEntity.cs                  ← from task_001
│       │   ├── AbMetricRecordEntity.cs                ← from task_001
│       │   └── ...
│       └── Configurations/
├── Server/
│   └── AI/
│       └── AiGatewayService.cs                        ← from US_067
├── app/
├── config/
└── scripts/
```

> Assumes US_008 (AuditLog entity), US_067 (AI Gateway), US_074 (PII redaction), and task_001 (A/B testing framework) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/AiAudit/IAiAuditService.cs | Interface: LogAiInteractionAsync, QueryAuditLogsAsync |
| CREATE | src/UPACIP.Service/AiAudit/AiAuditService.cs | Channel-based async writer, cursor-paginated query, background consumer |
| CREATE | src/UPACIP.Service/AiAudit/Models/AiAuditLogEntry.cs | DTO: Prompt, Response, ModelVersion, TokenCount, LatencyMs, ConfidenceScore, PatientId |
| CREATE | src/UPACIP.Service/AiAudit/Models/AiAuditQueryFilter.cs | Filter DTO: DateFrom, DateTo, ModelVersion, RequestType, ConfidenceMin, ConfidenceMax, Cursor |
| CREATE | src/UPACIP.Service/AiAudit/Models/AiAuditQueryResult.cs | Paginated result: Items, NextCursor, TotalCount, HasMore |
| CREATE | src/UPACIP.DataAccess/Entities/AiAuditLogEntity.cs | EF Core entity mapped to ai_audit_logs partitioned table |
| CREATE | src/UPACIP.DataAccess/Configurations/AiAuditLogConfiguration.cs | Indexes on created_at, model_version, request_type, confidence_score |
| CREATE | src/UPACIP.Service/AiAudit/AiAuditLoggingMiddleware.cs | AI Gateway middleware: capture post-PII-redacted prompt, response, timing |
| CREATE | src/UPACIP.Api/Controllers/Admin/AiAuditController.cs | Admin query endpoint: filter, paginate, summary aggregation |
| CREATE | scripts/create-ai-audit-partitions.sql | Range partitioning DDL, retention functions, partition maintenance |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet for AiAuditLogEntity |
| MODIFY | src/UPACIP.Api/Program.cs | Register IAiAuditService, add AiAuditLoggingMiddleware, register background consumer |

## External References

- [PostgreSQL Table Partitioning — Range](https://www.postgresql.org/docs/16/ddl-partitioning.html)
- [PostgreSQL — Partition Maintenance](https://www.postgresql.org/docs/16/ddl-partitioning.html#DDL-PARTITIONING-DECLARATIVE-MAINTENANCE)
- [System.Threading.Channels — Bounded Channel](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)
- [ASP.NET Core BackgroundService](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)
- [EF Core — Cursor-Based Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination#keyset-pagination)
- [Serilog Structured Logging](https://serilog.net/)
- [HIPAA Audit Trail Requirements](https://www.hhs.gov/hipaa/for-professionals/security/guidance/index.html)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Generate EF Core migration for AI audit log table
dotnet ef migrations add AddAiAuditLogs --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Apply partition creation script
psql -d upacip -f scripts/create-ai-audit-partitions.sql

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
- [ ] AI audit log entries contain post-PII-redacted prompt text (no patient identifying information)
- [ ] Log entries capture model version, input/output token counts, latency, and confidence score
- [ ] Partitioned table creates monthly partitions and routes inserts to correct partition
- [ ] Admin query endpoint filters correctly by date range, model version, request type, and confidence range
- [ ] Cursor-based pagination returns consistent results across pages
- [ ] Summary endpoint returns aggregated metrics per model version and request type
- [ ] Channel-based writer does not block AI request pipeline when under back-pressure
- [ ] Retention script correctly detaches 90+ day partitions and drops 1+ year partitions

## Implementation Checklist

- [ ] Create `AiAuditLogEntry`, `AiAuditQueryFilter`, and `AiAuditQueryResult` models in `src/UPACIP.Service/AiAudit/Models/`
- [ ] Create `AiAuditLogEntity` EF Core entity and `AiAuditLogConfiguration` with indexes
- [ ] Create `scripts/create-ai-audit-partitions.sql` with range partitioning, initial partitions, and maintenance function
- [ ] Define `IAiAuditService` interface with `LogAiInteractionAsync` and `QueryAuditLogsAsync` methods
- [ ] Implement `AiAuditService` with Channel-based async writer and BackgroundService consumer
- [ ] Implement cursor-based paginated query with conditional filtering
- [ ] Implement `AiAuditLoggingMiddleware` capturing post-PII-redacted data from AI Gateway pipeline
- [ ] Implement `AiAuditController` with query, detail, and summary admin endpoints
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-S04 and AIR-O06 requirements are met (audit logging and caching)
