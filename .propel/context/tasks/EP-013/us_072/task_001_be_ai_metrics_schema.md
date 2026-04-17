# Task - task_001_be_ai_metrics_schema

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

Define the database schema and EF Core entity models required to persist AI performance metrics for the monitoring dashboard. This includes entities for daily aggregated accuracy metrics (AI-human agreement rate, precision, recall), latency percentile metrics (P50, P95 for intake, document parsing, medical coding), alert threshold configuration, and generated alert records. These entities provide the persistence layer consumed by the metrics aggregation service (task_002) and displayed by the frontend dashboard (task_003).

## Dependent Tasks

- US_008 task_001_be_domain_entity_models — Requires `BaseEntity` abstract class, `MedicalCode` and `ExtractedData` entities for FK relationships and metric source data.
- US_067 — Requires AI Gateway service foundation for metrics collection points.

## Impacted Components

- **NEW** `src/UPACIP.DataAccess/Entities/AiAccuracyMetric.cs` — Daily aggregated accuracy metrics entity (agreement rate, precision, recall, sample size)
- **NEW** `src/UPACIP.DataAccess/Entities/AiLatencyMetric.cs` — Latency percentile metrics entity (P50, P95 per operation type)
- **NEW** `src/UPACIP.DataAccess/Entities/AiMetricAlert.cs` — Generated alert records when thresholds breached
- **NEW** `src/UPACIP.DataAccess/Entities/AiMetricThreshold.cs` — Configurable threshold definitions per metric type
- **NEW** `src/UPACIP.DataAccess/Enums/AiMetricType.cs` — Enum for metric types (CodingAgreement, ExtractionPrecision, ExtractionRecall)
- **NEW** `src/UPACIP.DataAccess/Enums/AiOperationType.cs` — Enum for AI operation types (Intake, DocumentParsing, MedicalCoding)
- **NEW** `src/UPACIP.DataAccess/Enums/TrendDirection.cs` — Enum for trend direction (Up, Down, Stable)
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Register new DbSets and configure entity mappings

## Implementation Plan

1. **Create `AiMetricType` enum**: Define in `src/UPACIP.DataAccess/Enums/AiMetricType.cs` with values: `CodingAgreement`, `ExtractionPrecision`, `ExtractionRecall`. Maps to AIR-Q01 (coding accuracy) and AIR-Q02 (extraction accuracy).

2. **Create `AiOperationType` enum**: Define in `src/UPACIP.DataAccess/Enums/AiOperationType.cs` with values: `Intake`, `DocumentParsing`, `MedicalCoding`. Maps to the three latency categories from AC-3.

3. **Create `TrendDirection` enum**: Define in `src/UPACIP.DataAccess/Enums/TrendDirection.cs` with values: `Up`, `Down`, `Stable`. Used in alert records per AC-4.

4. **Define `AiAccuracyMetric` entity**: Extends `BaseEntity`. Properties: `DateTime MetricDate` (date of aggregation), `AiMetricType MetricType`, `double Value` (0-100 percentage), `int SampleSize` (number of items in calculation), `double TargetValue` (threshold target). Add composite unique index on (`MetricDate`, `MetricType`) to prevent duplicate daily entries.

5. **Define `AiLatencyMetric` entity**: Extends `BaseEntity`. Properties: `DateTime MetricDate`, `AiOperationType OperationType`, `double P50Milliseconds`, `double P95Milliseconds`, `double TargetP95Milliseconds`, `int SampleSize`. Add composite unique index on (`MetricDate`, `OperationType`).

6. **Define `AiMetricThreshold` entity**: Extends `BaseEntity`. Properties: `string MetricName` (unique), `double TargetValue`, `double WarningValue`, `bool IsEnabled`. Stores configurable alert thresholds per AIR-Q01 (>98%), AIR-Q02 (>95%), latency targets.

7. **Define `AiMetricAlert` entity**: Properties: `Guid AlertId` (PK), `DateTime GeneratedAt`, `string MetricName`, `double CurrentValue`, `double TargetValue`, `TrendDirection TrendDirection`, `bool IsAcknowledged`, `Guid? AcknowledgedByUserId` (FK → ApplicationUser). Maps to AC-4 alert generation.

8. **Register DbSets and configure entity mappings**: Add `DbSet<AiAccuracyMetric>`, `DbSet<AiLatencyMetric>`, `DbSet<AiMetricThreshold>`, `DbSet<AiMetricAlert>` to `ApplicationDbContext`. Configure enum-to-string conversions, composite unique indexes, and FK relationships.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Identity/
│       │   ├── ApplicationUser.cs
│       │   └── ApplicationRole.cs
│       ├── Entities/
│       │   ├── BaseEntity.cs
│       │   ├── Patient.cs
│       │   ├── Appointment.cs
│       │   ├── IntakeData.cs
│       │   ├── ClinicalDocument.cs
│       │   ├── ExtractedData.cs
│       │   ├── MedicalCode.cs
│       │   ├── AuditLog.cs
│       │   ├── QueueEntry.cs
│       │   └── NotificationLog.cs
│       └── Enums/
│           ├── AppointmentStatus.cs
│           ├── IntakeMethod.cs
│           ├── DocumentCategory.cs
│           ├── ProcessingStatus.cs
│           ├── DataType.cs
│           ├── CodeType.cs
│           ├── AuditAction.cs
│           ├── QueuePriority.cs
│           ├── QueueStatus.cs
│           ├── NotificationType.cs
│           ├── DeliveryChannel.cs
│           └── NotificationStatus.cs
├── app/
└── scripts/
```

> Assumes US_008 (domain entities) and US_067 (AI Gateway) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.DataAccess/Enums/AiMetricType.cs | Enum: CodingAgreement, ExtractionPrecision, ExtractionRecall |
| CREATE | src/UPACIP.DataAccess/Enums/AiOperationType.cs | Enum: Intake, DocumentParsing, MedicalCoding |
| CREATE | src/UPACIP.DataAccess/Enums/TrendDirection.cs | Enum: Up, Down, Stable |
| CREATE | src/UPACIP.DataAccess/Entities/AiAccuracyMetric.cs | Daily accuracy metrics with MetricType, Value, SampleSize, TargetValue |
| CREATE | src/UPACIP.DataAccess/Entities/AiLatencyMetric.cs | Daily latency percentiles with OperationType, P50, P95, TargetP95 |
| CREATE | src/UPACIP.DataAccess/Entities/AiMetricThreshold.cs | Configurable alert threshold definitions per metric |
| CREATE | src/UPACIP.DataAccess/Entities/AiMetricAlert.cs | Alert records with metric name, current/target values, trend |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSets, entity configurations, composite indexes, enum mappings |

## External References

- [EF Core Entity Types](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types)
- [EF Core Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [EF Core Value Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [Npgsql Enum Mapping](https://www.npgsql.org/efcore/mapping/enum.html)
- [ERD Reference — models.md](../../../docs/models.md)

## Build Commands

```powershell
# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Build full solution
dotnet build UPACIP.sln

# Generate migration for AI metrics schema
dotnet ef migrations add AddAiMetricsSchema --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for UPACIP.DataAccess project
- [ ] All 4 entity classes compile and include required properties
- [ ] All 3 enum types compile with correct member values
- [ ] `AiAccuracyMetric` has composite unique index on (MetricDate, MetricType)
- [ ] `AiLatencyMetric` has composite unique index on (MetricDate, OperationType)
- [ ] `AiMetricThreshold` has unique index on MetricName
- [ ] `AiMetricAlert` has FK to ApplicationUser for AcknowledgedByUserId
- [ ] Enum properties use string conversion in EF Core configuration

## Implementation Checklist

- [ ] Create `AiMetricType` enum with `CodingAgreement`, `ExtractionPrecision`, `ExtractionRecall` values
- [ ] Create `AiOperationType` enum with `Intake`, `DocumentParsing`, `MedicalCoding` values
- [ ] Create `TrendDirection` enum with `Up`, `Down`, `Stable` values
- [ ] Define `AiAccuracyMetric` entity extending `BaseEntity` with `MetricDate`, `MetricType`, `Value`, `SampleSize`, `TargetValue`
- [ ] Define `AiLatencyMetric` entity extending `BaseEntity` with `MetricDate`, `OperationType`, `P50Milliseconds`, `P95Milliseconds`, `TargetP95Milliseconds`, `SampleSize`
- [ ] Define `AiMetricThreshold` entity extending `BaseEntity` with `MetricName`, `TargetValue`, `WarningValue`, `IsEnabled`
- [ ] Define `AiMetricAlert` entity with `AlertId`, `GeneratedAt`, `MetricName`, `CurrentValue`, `TargetValue`, `TrendDirection`, `IsAcknowledged`, `AcknowledgedByUserId`
- [ ] Register all new DbSets in `ApplicationDbContext` and configure composite unique indexes, enum conversions, and FK relationships
