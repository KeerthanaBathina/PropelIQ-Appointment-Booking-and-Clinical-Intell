# Task - task_002_be_calibration_service

## Requirement Reference

- User Story: us_073
- Story Location: .propel/context/tasks/EP-013/us_073/us_073.md
- Acceptance Criteria:
  - AC-1: Given the AI generates confidence scores, When scores are assigned, Then they follow a calibrated 0-1 distribution where a score of 0.80 corresponds to approximately 80% actual accuracy.
  - AC-2: Given a confidence score is below 0.80, When the result is stored, Then it is automatically flagged for mandatory manual review with "low-confidence" status.
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
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-Q07, AIR-Q08 |
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

Implement the confidence score calibration service layer containing: (1) `ICalibrationService` with Platt scaling logic that transforms raw AI confidence scores into calibrated probabilities per data type (AC-1); (2) automatic low-confidence flagging that marks `ExtractedData` records with calibrated score < 0.80 as `FlaggedForReview = true` with `CalibrationStatus = Calibrated` (AC-2); (3) `CalibrationJob` weekly background service that queries staff-verified `ExtractedData` records, computes predicted-vs-actual accuracy per confidence bin per data type, fits Platt scaling parameters, persists `CalibrationRecord` entries, and updates active `CalibrationParameter` records (AC-3); (4) drift detection that generates `CalibrationDriftAlert` records when the gap between predicted and actual accuracy exceeds 5% for any data type (AC-4). The service handles insufficient verification data by falling back to uncalibrated scoring with `CalibrationPending` status per edge case.

## Dependent Tasks

- US_073 task_001_be_calibration_schema — Requires `CalibrationParameter`, `CalibrationRecord`, `CalibrationDriftAlert` entities and `CalibrationStatus` enum.
- US_008 task_001_be_domain_entity_models — Requires `ExtractedData` entity with `ConfidenceScore`, `FlaggedForReview`, `VerifiedByUserId`, `DataType`.
- US_041 — Requires confidence scoring infrastructure that assigns raw `ConfidenceScore` to `ExtractedData` records.

## Impacted Components

- **NEW** `src/UPACIP.Service/Calibration/ICalibrationService.cs` — Service interface for calibration operations
- **NEW** `src/UPACIP.Service/Calibration/CalibrationService.cs` — Implementation: Platt scaling, score transformation, flagging, parameter fitting
- **NEW** `src/UPACIP.Service/Calibration/CalibrationJob.cs` — Weekly IHostedService for calibration runs and drift detection
- **NEW** `src/UPACIP.Service/Calibration/PlattScaler.cs` — Static utility class implementing Platt scaling sigmoid transformation
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register `ICalibrationService` and `CalibrationJob` in DI container

## Implementation Plan

1. **Implement `PlattScaler` utility class**: Create a static class with method `double CalibrateScore(double rawScore, double slope, double intercept)` that applies the Platt scaling sigmoid: `calibrated = 1.0 / (1.0 + Math.Exp(slope * rawScore + intercept))`. Add method `(double slope, double intercept) FitParameters(IReadOnlyList<(double predicted, bool actualCorrect)> samples)` that uses gradient descent to find optimal Platt scaling parameters from verification data. Minimum sample size: 50 verified items per data type.

2. **Define `ICalibrationService` interface**: Methods: `Task<float> CalibrateScoreAsync(float rawScore, DataType dataType)` — applies active calibration parameters to raw score, returns calibrated score; `Task FlagLowConfidenceAsync(Guid extractedDataId)` — evaluates calibrated score and flags if < 0.80; `Task RunWeeklyCalibrationAsync()` — executes full calibration workflow; `Task<bool> HasSufficientDataAsync(DataType dataType)` — checks if minimum verification sample exists.

3. **Implement score calibration logic (AC-1)**: In `CalibrationService.CalibrateScoreAsync`, query active `CalibrationParameter` for the given `DataType`. If active parameters exist, apply `PlattScaler.CalibrateScore` with the stored slope/intercept. If no active parameters exist (insufficient data edge case), return the raw score unchanged and set `CalibrationStatus = CalibrationPending` on the `ExtractedData` record. Store the calibrated score in `ExtractedData.CalibratedConfidenceScore`.

4. **Implement low-confidence flagging (AC-2)**: In `CalibrationService.FlagLowConfidenceAsync`, load the `ExtractedData` record, check the calibrated score (or raw score if uncalibrated). If score < 0.80, set `FlaggedForReview = true` and update `CalibrationStatus`. This method is called after every AI extraction result is persisted, ensuring real-time flagging. Log flagging events via Serilog with `ExtractedDataId` and score value.

5. **Implement weekly calibration job (AC-3)**: Create `CalibrationJob` as `IHostedService` using `PeriodicTimer` (7-day interval). On each tick, iterate over each `DataType` value independently (per edge case). For each data type: (a) query `ExtractedData` records where `VerifiedByUserId != null` from the past 90 days; (b) if sample size < 50, skip calibration for this type and log "Insufficient data for {DataType}, using uncalibrated model"; (c) group verified records into 10 confidence bins (0.0-0.1, ..., 0.9-1.0); (d) for each bin, calculate predicted accuracy (average raw confidence) and actual accuracy (fraction where staff verification matched AI result); (e) persist `CalibrationRecord` entries per bin; (f) fit new Platt scaling parameters using `PlattScaler.FitParameters`; (g) deactivate old `CalibrationParameter` for this `DataType`, create and activate new parameter record.

6. **Implement drift detection (AC-4)**: During the weekly calibration run, after computing per-bin predicted-vs-actual accuracy, calculate the weighted average drift across all bins. If `Math.Abs(predictedAccuracy - actualAccuracy) > 0.05` (5% gap) for any data type, create a `CalibrationDriftAlert` record with the data type, predicted accuracy, actual accuracy, drift percentage, and `IsAcknowledged = false`. Log drift alert generation at Warning level.

7. **Handle insufficient verification data (edge case)**: When `HasSufficientDataAsync` returns false for a data type, the service: (a) skips Platt parameter fitting; (b) marks all new `ExtractedData` records of that type with `CalibrationStatus = CalibrationPending`; (c) uses raw confidence scores for flagging logic; (d) logs a structured warning identifying the data type and current sample count vs. required minimum (50).

8. **Register services in DI**: Add `services.AddScoped<ICalibrationService, CalibrationService>()` and `services.AddHostedService<CalibrationJob>()` in `Program.cs`. Configure the job interval via `appsettings.json` key `Calibration:IntervalDays` (default: 7).

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
│       │   ├── ExtractedData.cs
│       │   ├── CalibrationParameter.cs   ← from task_001
│       │   ├── CalibrationRecord.cs      ← from task_001
│       │   └── CalibrationDriftAlert.cs  ← from task_001
│       └── Enums/
│           ├── DataType.cs
│           └── CalibrationStatus.cs      ← from task_001
├── app/
└── scripts/
```

> Assumes task_001 (calibration schema) and US_041 (confidence scoring infrastructure) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Calibration/ICalibrationService.cs | Interface: CalibrateScoreAsync, FlagLowConfidenceAsync, RunWeeklyCalibrationAsync, HasSufficientDataAsync |
| CREATE | src/UPACIP.Service/Calibration/CalibrationService.cs | Implementation: Platt scaling transformation, flagging, per-category calibration, drift check |
| CREATE | src/UPACIP.Service/Calibration/CalibrationJob.cs | IHostedService weekly job: calibration run, parameter fitting, drift alerts |
| CREATE | src/UPACIP.Service/Calibration/PlattScaler.cs | Static utility: sigmoid transformation, gradient descent parameter fitting |
| MODIFY | src/UPACIP.Api/Program.cs | Register ICalibrationService and CalibrationJob in DI container |

## External References

- [Platt Scaling — Probabilistic Outputs for SVMs](https://www.cs.colorado.edu/~mozer/Teaching/syllabi/6622/papers/Platt1999.pdf)
- [Probability Calibration of Classifiers](https://scikit-learn.org/stable/modules/calibration.html)
- [ASP.NET Core Background Services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0)
- [EF Core Querying](https://learn.microsoft.com/en-us/ef/core/querying/)
- [Serilog Structured Logging](https://serilog.net/)

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
- [ ] `PlattScaler.CalibrateScore(0.80, slope, intercept)` produces a value near 0.80 with correctly fitted parameters
- [ ] `CalibrationService.CalibrateScoreAsync` returns raw score with `CalibrationPending` status when no active parameters exist
- [ ] `CalibrationService.FlagLowConfidenceAsync` sets `FlaggedForReview = true` when calibrated score < 0.80
- [ ] `CalibrationJob` iterates over each `DataType` independently for per-category calibration
- [ ] `CalibrationJob` skips calibration when verified sample size < 50 per data type
- [ ] Drift detection creates `CalibrationDriftAlert` when gap > 5% between predicted and actual accuracy
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)

## Implementation Checklist

- [ ] Create `PlattScaler` static utility with `CalibrateScore` (sigmoid transformation) and `FitParameters` (gradient descent fitting) methods
- [ ] Define `ICalibrationService` interface with `CalibrateScoreAsync`, `FlagLowConfidenceAsync`, `RunWeeklyCalibrationAsync`, `HasSufficientDataAsync`
- [ ] Implement score calibration: query active `CalibrationParameter` per `DataType`, apply Platt scaling, fall back to uncalibrated with `CalibrationPending` when no parameters exist
- [ ] Implement low-confidence flagging: set `FlaggedForReview = true` on `ExtractedData` records with calibrated score < 0.80 with structured logging
- [ ] Implement weekly calibration job: query verified `ExtractedData` per `DataType`, bin by confidence ranges, compute predicted-vs-actual accuracy, fit Platt parameters, persist `CalibrationRecord` entries
- [ ] Implement drift detection: generate `CalibrationDriftAlert` when weighted average gap > 5% per data type
- [ ] Handle insufficient data edge case: skip parameter fitting, use raw scores, mark records `CalibrationPending`, log warning with sample count
- [ ] Register `ICalibrationService` and `CalibrationJob` in `Program.cs` DI container with configurable interval via `appsettings.json`
- **[AI Tasks - MANDATORY]** Verify AIR-Q07 (calibrated 0-1 distribution) and AIR-Q08 (flag <0.80) requirements are met
