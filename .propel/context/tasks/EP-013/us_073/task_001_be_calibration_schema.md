# Task - task_001_be_calibration_schema

## Requirement Reference

- User Story: us_073
- Story Location: .propel/context/tasks/EP-013/us_073/us_073.md
- Acceptance Criteria:
  - AC-1: Given the AI generates confidence scores, When scores are assigned, Then they follow a calibrated 0-1 distribution where a score of 0.80 corresponds to approximately 80% actual accuracy.
  - AC-3: Given calibration data is collected, When the weekly calibration job runs, Then the system compares predicted confidence vs. actual accuracy (based on staff verifications) and adjusts scoring parameters.
  - AC-4: Given calibration drift is detected (>5% gap between predicted and actual), When the weekly check runs, Then the system generates an alert for admin review.
- Edge Case:
  - What happens when insufficient verification data exists for calibration? System uses the default (uncalibrated) scoring model and flags all results as "calibration-pending."
  - How does the system handle calibration for different data types (medications vs. diagnoses)? Calibration is performed independently per extraction category.

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

Define the database schema and EF Core entity models required to persist confidence score calibration data. This includes: (1) a `CalibrationParameter` entity storing per-category scaling factors and offsets used to transform raw AI confidence scores into calibrated probabilities; (2) a `CalibrationRecord` entity storing weekly calibration run results (predicted vs. actual accuracy per bin and per data type); (3) a `CalibrationDriftAlert` entity for threshold breach alerts; (4) a `CalibrationStatus` enum tracking whether data points use calibrated or uncalibrated scoring. These entities support the weekly calibration job and drift detection implemented in task_002.

## Dependent Tasks

- US_008 task_001_be_domain_entity_models — Requires `BaseEntity` abstract class, `ExtractedData` entity with `ConfidenceScore` and `DataType` properties.

## Impacted Components

- **NEW** `src/UPACIP.DataAccess/Entities/CalibrationParameter.cs` — Per-category calibration scaling parameters (slope, intercept, last calibrated date)
- **NEW** `src/UPACIP.DataAccess/Entities/CalibrationRecord.cs` — Weekly calibration run results with predicted vs. actual accuracy per confidence bin
- **NEW** `src/UPACIP.DataAccess/Entities/CalibrationDriftAlert.cs` — Alert records when calibration drift exceeds 5% threshold
- **NEW** `src/UPACIP.DataAccess/Enums/CalibrationStatus.cs` — Enum: Calibrated, Uncalibrated, CalibrationPending
- **MODIFY** `src/UPACIP.DataAccess/Entities/ExtractedData.cs` — Add `CalibrationStatus` property and `CalibratedConfidenceScore` nullable property
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Register new DbSets and configure entity mappings

## Implementation Plan

1. **Create `CalibrationStatus` enum**: Define in `src/UPACIP.DataAccess/Enums/CalibrationStatus.cs` with values: `Calibrated`, `Uncalibrated`, `CalibrationPending`. When insufficient verification data exists, all results are flagged `CalibrationPending` per edge case.

2. **Define `CalibrationParameter` entity**: Extends `BaseEntity`. Properties: `DataType DataType` (FK to DataType enum — Medication, Diagnosis, Procedure, Allergy), `double Slope` (Platt scaling slope parameter), `double Intercept` (Platt scaling intercept parameter), `DateTime LastCalibratedAt`, `int VerificationSampleSize` (number of verified items used in calibration), `bool IsActive` (only one active parameter set per DataType). Add unique index on (`DataType`, `IsActive`) filtered to `IsActive = true` to enforce single active calibration per category per edge case.

3. **Define `CalibrationRecord` entity**: Extends `BaseEntity`. Properties: `DateTime CalibrationRunDate`, `DataType DataType`, `double PredictedAccuracy` (average predicted confidence in bin), `double ActualAccuracy` (actual correctness rate from staff verifications), `double DriftPercentage` (absolute gap between predicted and actual), `int BinStart` (confidence bin lower bound as integer 0-100), `int BinEnd` (confidence bin upper bound), `int SampleSize`, `bool DriftDetected` (true if gap > 5%). Stores per-bin comparison data for AC-3.

4. **Define `CalibrationDriftAlert` entity**: Properties: `Guid AlertId` (PK), `DateTime GeneratedAt`, `DataType DataType`, `double PredictedAccuracy`, `double ActualAccuracy`, `double DriftPercentage`, `bool IsAcknowledged`, `Guid? AcknowledgedByUserId` (FK → ApplicationUser), `DateTime? AcknowledgedAt`. Maps to AC-4 alert generation when drift > 5%.

5. **Modify `ExtractedData` entity**: Add `CalibrationStatus CalibrationStatus` property (default: `Uncalibrated`). Add `float? CalibratedConfidenceScore` nullable property to store the post-calibration score alongside the raw `ConfidenceScore`. The raw score persists for audit; the calibrated score is used for flagging logic.

6. **Register DbSets and configure entity mappings**: Add `DbSet<CalibrationParameter>`, `DbSet<CalibrationRecord>`, `DbSet<CalibrationDriftAlert>` to `ApplicationDbContext`. Configure enum-to-string conversions for `CalibrationStatus` and `DataType`. Add unique filtered index on `CalibrationParameter(DataType, IsActive)`. Configure FK for `CalibrationDriftAlert.AcknowledgedByUserId`.

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
│           ├── DataType.cs
│           └── ... (other enums)
├── app/
└── scripts/
```

> Assumes US_008 (domain entities) is completed. `ExtractedData` entity exists with `ConfidenceScore` (float) and `DataType` (enum) properties.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.DataAccess/Enums/CalibrationStatus.cs | Enum: Calibrated, Uncalibrated, CalibrationPending |
| CREATE | src/UPACIP.DataAccess/Entities/CalibrationParameter.cs | Per-category Platt scaling parameters with slope, intercept, sample size |
| CREATE | src/UPACIP.DataAccess/Entities/CalibrationRecord.cs | Weekly calibration run results with predicted vs. actual per confidence bin |
| CREATE | src/UPACIP.DataAccess/Entities/CalibrationDriftAlert.cs | Alert records for calibration drift > 5% with admin acknowledgment |
| MODIFY | src/UPACIP.DataAccess/Entities/ExtractedData.cs | Add CalibrationStatus and CalibratedConfidenceScore properties |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSets, entity configurations, indexes, enum mappings |

## External References

- [Platt Scaling for Probability Calibration](https://en.wikipedia.org/wiki/Platt_scaling)
- [EF Core Filtered Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=data-annotations#index-filter)
- [EF Core Value Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [EF Core Entity Types](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types)

## Build Commands

```powershell
# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Build full solution
dotnet build UPACIP.sln

# Generate migration for calibration schema
dotnet ef migrations add AddCalibrationSchema --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for UPACIP.DataAccess project
- [ ] All 3 new entity classes compile and include required properties
- [ ] `CalibrationStatus` enum compiles with 3 values
- [ ] `CalibrationParameter` has unique filtered index on (DataType, IsActive) where IsActive = true
- [ ] `CalibrationRecord` includes DriftPercentage and DriftDetected properties for >5% gap tracking
- [ ] `CalibrationDriftAlert` has FK to ApplicationUser for AcknowledgedByUserId
- [ ] `ExtractedData` now has `CalibrationStatus` and `CalibratedConfidenceScore` properties
- [ ] Enum properties use string conversion in EF Core configuration

## Implementation Checklist

- [ ] Create `CalibrationStatus` enum with `Calibrated`, `Uncalibrated`, `CalibrationPending` values
- [ ] Define `CalibrationParameter` entity extending `BaseEntity` with `DataType`, `Slope`, `Intercept`, `LastCalibratedAt`, `VerificationSampleSize`, `IsActive`, and unique filtered index
- [ ] Define `CalibrationRecord` entity extending `BaseEntity` with `CalibrationRunDate`, `DataType`, `PredictedAccuracy`, `ActualAccuracy`, `DriftPercentage`, `BinStart`, `BinEnd`, `SampleSize`, `DriftDetected`
- [ ] Define `CalibrationDriftAlert` entity with `AlertId`, `GeneratedAt`, `DataType`, `PredictedAccuracy`, `ActualAccuracy`, `DriftPercentage`, `IsAcknowledged`, `AcknowledgedByUserId`
- [ ] Modify `ExtractedData` entity to add `CalibrationStatus` (default: Uncalibrated) and `CalibratedConfidenceScore` (nullable float) properties
- [ ] Register all new DbSets in `ApplicationDbContext` and configure unique filtered index, enum-to-string conversions, and FK relationships
