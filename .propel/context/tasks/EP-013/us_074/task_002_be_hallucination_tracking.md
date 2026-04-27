# Task - task_002_be_hallucination_tracking

## Requirement Reference

- User Story: us_074
- Story Location: .propel/context/tasks/EP-013/us_074/us_074.md
- Acceptance Criteria:
  - AC-1: Given AI generates medical justifications, When staff verify results, Then the system tracks hallucination rate (data not supported by source documents) with a target of <5%.
  - AC-2: Given hallucination rate exceeds 5%, When the daily calculation runs, Then the system generates a critical alert and recommends model review.
- Edge Case:
  - What happens when a hallucination is detected after staff has already approved the data? System creates a retroactive alert and marks the affected entries for re-verification.

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

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-Q06 |
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

Implement hallucination tracking and alerting for AI-generated medical justifications per AIR-Q06. This includes: (1) database entities to store hallucination verification records and daily hallucination rate metrics; (2) `IHallucinationTrackingService` that records staff verification outcomes against source documents, classifying each AI justification as "supported", "unsupported" (hallucination), or "partially-supported"; (3) a daily aggregation job that calculates the hallucination rate (unsupported / total verified) and generates a critical alert when the rate exceeds 5%; (4) retroactive hallucination detection that handles cases where staff discover unsupported data after initial approval, creating alerts and marking affected entries for re-verification per edge case.

## Dependent Tasks

- US_008 task_001_be_domain_entity_models — Requires `MedicalCode` entity with `Justification`, `SuggestedByAi`, `ApprovedByUserId` properties.
- US_067 — Requires AI Gateway for AI response logging with source attribution.

## Impacted Components

- **NEW** `src/UPACIP.DataAccess/Entities/HallucinationRecord.cs` — Per-justification verification record with source support classification
- **NEW** `src/UPACIP.DataAccess/Entities/HallucinationMetric.cs` — Daily aggregated hallucination rate metrics
- **NEW** `src/UPACIP.DataAccess/Entities/HallucinationAlert.cs` — Critical alert records when rate exceeds 5%
- **NEW** `src/UPACIP.DataAccess/Enums/SourceSupportStatus.cs` — Enum: Supported, Unsupported, PartiallSupported, Pending
- **NEW** `src/UPACIP.Service/AiSafety/IHallucinationTrackingService.cs` — Service interface for verification recording and rate calculation
- **NEW** `src/UPACIP.Service/AiSafety/HallucinationTrackingService.cs` — Implementation: verification logic, rate calculation, retroactive detection
- **NEW** `src/UPACIP.Service/AiSafety/HallucinationAggregationJob.cs` — Daily IHostedService for rate aggregation and alert generation
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Register new DbSets and entity configurations
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register services and background job in DI container

## Implementation Plan

1. **Create `SourceSupportStatus` enum**: Define in `src/UPACIP.DataAccess/Enums/SourceSupportStatus.cs` with values: `Supported` (justification fully backed by source documents), `Unsupported` (hallucination — data not in source), `PartiallySupportted` (partially backed), `Pending` (not yet verified). This enum classifies each AI justification against its source.

2. **Define `HallucinationRecord` entity**: Extends `BaseEntity`. Properties: `Guid MedicalCodeId` (FK → MedicalCode), `Guid VerifiedByUserId` (FK → ApplicationUser), `SourceSupportStatus SourceSupportStatus`, `string? VerificationNotes` (staff explanation for classification), `DateTime VerifiedAt`, `bool IsRetroactive` (true when hallucination found after initial approval per edge case). Navigation: `MedicalCode MedicalCode`. One record per verified AI justification.

3. **Define `HallucinationMetric` entity**: Extends `BaseEntity`. Properties: `DateTime MetricDate` (date of aggregation), `int TotalVerified` (count of verified justifications in period), `int HallucinationCount` (count classified as Unsupported), `int PartiallySupportedCount`, `double HallucinationRate` (HallucinationCount / TotalVerified as 0-1), `double TargetRate` (0.05 — 5% target). Add unique index on `MetricDate` to prevent duplicate daily entries.

4. **Define `HallucinationAlert` entity**: Properties: `Guid AlertId` (PK), `DateTime GeneratedAt`, `double CurrentRate` (actual hallucination rate), `double TargetRate` (0.05), `string Recommendation` ("Model review recommended — hallucination rate exceeds 5% target"), `bool IsRetroactive` (true if triggered by retroactive detection), `bool IsAcknowledged`, `Guid? AcknowledgedByUserId` (FK → ApplicationUser), `DateTime? AcknowledgedAt`. Maps to AC-2.

5. **Implement `IHallucinationTrackingService` interface**: Methods: `Task RecordVerificationAsync(Guid medicalCodeId, Guid verifierUserId, SourceSupportStatus status, string? notes)` — records staff verification of an AI justification; `Task RecordRetroactiveHallucinationAsync(Guid medicalCodeId, Guid reporterUserId, string reason)` — handles post-approval hallucination discovery per edge case; `Task<double> CalculateDailyRateAsync(DateTime date)` — computes hallucination rate for a given day; `Task RunDailyAggregationAsync()` — executes full aggregation and alert check.

6. **Implement verification recording (AC-1)**: In `RecordVerificationAsync`, create a `HallucinationRecord` for the given `MedicalCode`. Staff classify the AI justification by comparing it against the source clinical document linked via `ExtractedData.SourceAttribution`. If `SourceSupportStatus == Unsupported`, the justification is counted as a hallucination. Log verification event with `MedicalCodeId` and classification via Serilog (no patient PII).

7. **Implement retroactive hallucination detection (edge case)**: In `RecordRetroactiveHallucinationAsync`: (a) load the `MedicalCode` and its existing `HallucinationRecord` if any; (b) update or create `HallucinationRecord` with `SourceSupportStatus = Unsupported` and `IsRetroactive = true`; (c) mark the `MedicalCode` entry with a re-verification flag by setting `ApprovedByUserId = null` (requiring re-approval); (d) create a `HallucinationAlert` with `IsRetroactive = true` and recommendation text including the affected `MedicalCodeId`; (e) log at Warning level.

8. **Implement daily aggregation job (AC-2)**: Create `HallucinationAggregationJob` as `IHostedService` with `PeriodicTimer` (24-hour interval). On each tick: (a) query `HallucinationRecord` entries from the past 24 hours; (b) count `Unsupported` vs total verified; (c) calculate `HallucinationRate = unsupported / total`; (d) persist `HallucinationMetric` record; (e) if rate > 0.05, create `HallucinationAlert` with `CurrentRate`, `TargetRate = 0.05`, `Recommendation = "Model review recommended — hallucination rate {rate:P1} exceeds 5% target. Review recent model changes and prompt templates."`, and `IsAcknowledged = false`. Register in DI via `services.AddHostedService<HallucinationAggregationJob>()`.

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
│   │   ├── UPACIP.Service.csproj
│   │   └── AiSafety/
│   │       ├── IPiiRedactionService.cs     ← from task_001
│   │       ├── PiiRedactionService.cs      ← from task_001
│   │       ├── PiiRedactionContext.cs      ← from task_001
│   │       ├── MedicalTermAllowlist.cs     ← from task_001
│   │       └── PiiRedactionMiddleware.cs   ← from task_001
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── BaseEntity.cs
│       │   ├── MedicalCode.cs
│       │   └── ExtractedData.cs
│       └── Enums/
│           └── DataType.cs
├── app/
├── config/
└── scripts/
```

> Assumes task_001 (PII redaction), US_067 (AI Gateway), and US_008 (domain entities) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.DataAccess/Enums/SourceSupportStatus.cs | Enum: Supported, Unsupported, PartiallySupportted, Pending |
| CREATE | src/UPACIP.DataAccess/Entities/HallucinationRecord.cs | Per-justification verification with source support classification and retroactive flag |
| CREATE | src/UPACIP.DataAccess/Entities/HallucinationMetric.cs | Daily hallucination rate metrics with target indicator |
| CREATE | src/UPACIP.DataAccess/Entities/HallucinationAlert.cs | Critical alert records with recommendation text and acknowledgment |
| CREATE | src/UPACIP.Service/AiSafety/IHallucinationTrackingService.cs | Interface: RecordVerification, RecordRetroactiveHallucination, CalculateDailyRate, RunDailyAggregation |
| CREATE | src/UPACIP.Service/AiSafety/HallucinationTrackingService.cs | Implementation: verification recording, retroactive detection, rate calculation |
| CREATE | src/UPACIP.Service/AiSafety/HallucinationAggregationJob.cs | Daily IHostedService for rate aggregation and >5% threshold alert generation |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSets for HallucinationRecord, HallucinationMetric, HallucinationAlert; configure indexes and enum mappings |
| MODIFY | src/UPACIP.Api/Program.cs | Register IHallucinationTrackingService and HallucinationAggregationJob in DI container |

## External References

- [Hallucination Detection in LLMs](https://arxiv.org/abs/2311.05232)
- [ASP.NET Core Background Services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0)
- [EF Core Entity Types](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types)
- [EF Core Value Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [Serilog Structured Logging](https://serilog.net/)

## Build Commands

```powershell
# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln

# Generate migration
dotnet ef migrations add AddHallucinationTracking --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for UPACIP.DataAccess, UPACIP.Service, and UPACIP.Api projects
- [ ] `HallucinationRecord` entity stores staff classification (Supported/Unsupported/PartiallySupportted) per AI justification
- [ ] `HallucinationMetric` has unique index on `MetricDate` preventing duplicate daily entries
- [ ] `HallucinationAggregationJob` creates `HallucinationAlert` when rate exceeds 5% with model review recommendation
- [ ] Retroactive hallucination detection resets `MedicalCode.ApprovedByUserId` to null and creates retroactive alert
- [ ] All logging uses `MedicalCodeId` references only — no patient PII in log output
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)

## Implementation Checklist

- [x] Create `SourceSupportStatus` enum with `Supported`, `Unsupported`, `PartiallySupported`, `Pending` values
- [x] Define `HallucinationRecord` entity extending `BaseEntity` with `MedicalCodeId` FK, `VerifiedByUserId`, `SourceSupportStatus`, `VerificationNotes`, `VerifiedAt`, `IsRetroactive`
- [x] Define `HallucinationMetric` entity extending `BaseEntity` with `MetricDate` (unique index), `TotalVerified`, `HallucinationCount`, `HallucinationRate`, `TargetRate`
- [x] Define `HallucinationAlert` entity with `AlertId`, `GeneratedAt`, `CurrentRate`, `TargetRate`, `Recommendation`, `IsRetroactive`, `IsAcknowledged`, `AcknowledgedByUserId`
- [x] Implement `IHallucinationTrackingService` with `RecordVerificationAsync`, `RecordRetroactiveHallucinationAsync`, `CalculateDailyRateAsync`, `RunDailyAggregationAsync`
- [x] Implement retroactive hallucination handling: reset `ApprovedByUserId`, create retroactive alert, mark entries for re-verification
- [x] Implement `HallucinationAggregationJob` as daily `IHostedService`: aggregate rate, generate critical alert with recommendation when > 5%
- [x] Register DbSets, services, and background job in `ApplicationDbContext` and `Program.cs`
- **[AI Tasks - MANDATORY]** Verify AIR-Q06 (hallucination rate < 5% tracking) requirement is met ✅
