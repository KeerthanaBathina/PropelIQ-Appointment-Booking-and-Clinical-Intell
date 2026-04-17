# Task - task_003_be_rpo_rto_recovery_configuration

## Requirement Reference

- User Story: us_095
- Story Location: .propel/context/tasks/EP-018/us_095/us_095.md
- Acceptance Criteria:
  - AC-3: Given recovery targets are defined, When a disaster occurs, Then the documented RPO is 1 hour and RTO is 4 hours, with recovery procedures tested quarterly.

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
| Backend | Serilog | 8.x |
| Infrastructure | PowerShell Scripts + Windows Task Scheduler | - |
| Infrastructure | Windows Server | 2022 |
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

Implement RPO/RTO recovery target configuration, monitoring, and automated quarterly test scheduling to satisfy AC-3 (NFR-024: 1-hour RPO, NFR-025: 4-hour RTO, DR-026: quarterly recovery testing). The implementation provides three components: (1) a `RecoveryTargetMonitoringService` that continuously verifies the system meets its RPO/RTO targets by checking WAL archival freshness (within 15 minutes per DR-027), backup recency (within 24 hours per DR-022), and service health check responsiveness; (2) a `QuarterlyRecoveryTestScheduler` BackgroundService that tracks quarterly test deadlines and alerts administrators when tests are due or overdue; (3) a `DisasterRecoveryRunbook` configuration entity that documents step-by-step recovery procedures with estimated time-to-completion for each phase, enabling RTO tracking during actual recovery events. An admin API exposes recovery target status, test history, and runbook access.

## Dependent Tasks

- US_088 — Requires backup orchestration service (daily backups for RPO validation).
- US_090 — Requires WAL archival service (15-minute WAL archiving for RPO validation).
- US_089 task_003 — Requires quarterly restoration testing service.

## Impacted Components

- **NEW** `src/UPACIP.Service/Recovery/RecoveryTargetMonitoringService.cs` — BackgroundService: monitors RPO/RTO target compliance
- **NEW** `src/UPACIP.Service/Recovery/QuarterlyRecoveryTestScheduler.cs` — BackgroundService: tracks and alerts on quarterly test deadlines
- **NEW** `src/UPACIP.Service/Recovery/Models/RecoveryTargetOptions.cs` — Configuration: RPO, RTO, test schedule, alert thresholds
- **NEW** `src/UPACIP.Service/Recovery/Models/RecoveryTargetStatus.cs` — DTO: current RPO/RTO compliance status
- **NEW** `src/UPACIP.Service/Recovery/Models/DisasterRecoveryRunbook.cs` — Entity: step-by-step recovery procedures with time estimates
- **NEW** `src/UPACIP.Service/Recovery/Models/RecoveryTestRecord.cs` — Entity: quarterly test execution history
- **NEW** `src/UPACIP.Api/Controllers/RecoveryController.cs` — Admin API: recovery status, test history, runbook access
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<DisasterRecoveryRunbook>, DbSet<RecoveryTestRecord>
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register recovery services, bind RecoveryTargetOptions

## Implementation Plan

1. **Create `RecoveryTargetOptions` configuration model**: Create in `src/UPACIP.Service/Recovery/Models/RecoveryTargetOptions.cs`:
   - `int RpoMinutes` (default: 60). Maximum acceptable data loss window — 1 hour per NFR-024.
   - `int RtoMinutes` (default: 240). Maximum acceptable downtime — 4 hours per NFR-025.
   - `int WalArchiveIntervalMinutes` (default: 15). Expected WAL archival frequency per DR-027.
   - `int BackupFrequencyHours` (default: 24). Expected backup frequency per DR-022.
   - `int MonitoringCheckIntervalMinutes` (default: 30). How often the monitoring service checks compliance.
   - `int QuarterlyTestAlertDaysBefore` (default: 14). Days before test deadline to start alerting.
   - `string AlertEmail` (default: configured admin email). Who receives RPO/RTO alerts.
   Register via `IOptionsMonitor<RecoveryTargetOptions>`. Add to `appsettings.json`:
   ```json
   "RecoveryTargets": {
     "RpoMinutes": 60,
     "RtoMinutes": 240,
     "WalArchiveIntervalMinutes": 15,
     "BackupFrequencyHours": 24,
     "MonitoringCheckIntervalMinutes": 30,
     "QuarterlyTestAlertDaysBefore": 14,
     "AlertEmail": "admin@clinic.com"
   }
   ```

2. **Create `RecoveryTargetStatus` DTO**: Create in `src/UPACIP.Service/Recovery/Models/RecoveryTargetStatus.cs`:
   - `bool RpoCompliant` — whether current data protection meets 1-hour RPO.
   - `TimeSpan CurrentRpo` — actual RPO based on most recent WAL archive timestamp.
   - `TimeSpan TargetRpo` — configured RPO target (1 hour).
   - `bool RtoCompliant` — whether recovery infrastructure is ready (backups available, procedures documented).
   - `TimeSpan TargetRto` — configured RTO target (4 hours).
   - `DateTime LastBackupUtc` — timestamp of most recent successful backup.
   - `DateTime LastWalArchiveUtc` — timestamp of most recent WAL archive.
   - `DateTime? LastRecoveryTestUtc` — when the last quarterly test was executed.
   - `DateTime NextRecoveryTestDeadlineUtc` — when the next quarterly test is due.
   - `string QuarterlyTestStatus` — "Current", "DueSoon", "Overdue".
   - `List<string> Warnings` — any compliance issues detected.
   - `DateTime CheckedAtUtc`.

3. **Implement `RecoveryTargetMonitoringService` (AC-3 — RPO/RTO monitoring)**: Create in `src/UPACIP.Service/Recovery/RecoveryTargetMonitoringService.cs` as a `BackgroundService`. Constructor injection of `ApplicationDbContext`, `IOptionsMonitor<RecoveryTargetOptions>`, `ILogger<RecoveryTargetMonitoringService>`.

   **`ExecuteAsync(CancellationToken ct)`**: Loop every `MonitoringCheckIntervalMinutes`:

   - (a) **RPO check** (NFR-024):
     - Query `BackupLog` table for the most recent successful backup timestamp.
     - Query WAL archival status (from US_090's `WalArchivalStatus`) for the most recent WAL archive timestamp.
     - Compute `CurrentRpo = DateTime.UtcNow - max(lastBackup, lastWalArchive)`.
     - If `CurrentRpo > RpoMinutes`: `RpoCompliant = false`, log critical alert: `Log.Critical("RPO_VIOLATION: CurrentRPO={Minutes}min exceeds target {Target}min")`.
     - If `CurrentRpo <= RpoMinutes`: `RpoCompliant = true`, log debug: `Log.Debug("RPO_CHECK_PASSED: CurrentRPO={Minutes}min")`.

   - (b) **RTO readiness check** (NFR-025):
     - Verify backup exists and is recent (within `BackupFrequencyHours`).
     - Verify WAL archives are current (within `WalArchiveIntervalMinutes`).
     - Verify disaster recovery runbook exists and has `Status = "Active"`.
     - Verify quarterly restoration test is current (not overdue).
     - `RtoCompliant = true` only if all four conditions are met.
     - If not compliant, log warning: `Log.Warning("RTO_READINESS_GAP: {MissingComponents}")`.

   - (c) **Persist status**: Store the `RecoveryTargetStatus` snapshot for API consumption (use an in-memory cache updated on each check — no need to persist every check to the database).

4. **Create `RecoveryTestRecord` entity**: Create in `src/UPACIP.Service/Recovery/Models/RecoveryTestRecord.cs`:
   - `Guid Id` (PK).
   - `DateTime ExecutedAtUtc` — when the test was run.
   - `string ExecutedBy` — admin who triggered the test.
   - `string TestType` — "FullRestoration", "PartialRestoration", "PITRTest".
   - `bool Passed` — whether the test succeeded.
   - `TimeSpan ActualRecoveryTime` — how long the recovery took (compared against RTO).
   - `int RowsVerified` — data integrity row count.
   - `string? FailureReason` — if test failed.
   - `string Quarter` — e.g., "2026-Q2".
   - `DateTime CreatedAtUtc`.
   Add `DbSet<RecoveryTestRecord>` to `ApplicationDbContext`.

5. **Implement `QuarterlyRecoveryTestScheduler` (AC-3 — tested quarterly)**: Create in `src/UPACIP.Service/Recovery/QuarterlyRecoveryTestScheduler.cs` as a `BackgroundService`. Constructor injection of `ApplicationDbContext`, `IOptionsMonitor<RecoveryTargetOptions>`, `ILogger<QuarterlyRecoveryTestScheduler>`.

   **`ExecuteAsync(CancellationToken ct)`**: Check daily at midnight:

   - (a) Compute current quarter: Q1 (Jan-Mar), Q2 (Apr-Jun), Q3 (Jul-Sep), Q4 (Oct-Dec).
   - (b) Query `RecoveryTestRecord` for the most recent test in the current quarter.
   - (c) Compute quarter end date (deadline for testing per DR-026).
   - (d) Determine status:
     - Test exists for current quarter → `QuarterlyTestStatus = "Current"`.
     - No test but deadline > `AlertDaysBefore` away → `QuarterlyTestStatus = "Current"`.
     - No test and deadline within `AlertDaysBefore` → `QuarterlyTestStatus = "DueSoon"`, log warning: `Log.Warning("RECOVERY_TEST_DUE: Quarter={Quarter}, DaysRemaining={Days}")`.
     - No test and past deadline → `QuarterlyTestStatus = "Overdue"`, log critical: `Log.Critical("RECOVERY_TEST_OVERDUE: Quarter={Quarter}, DaysOverdue={Days}")`.
   - (e) After quarterly test execution (triggered by US_089 task_003 `IBackupRestorationTestService`), record a `RecoveryTestRecord` with the actual recovery time. Compare against RTO target:
     - If `ActualRecoveryTime <= RtoMinutes`: log success: `Log.Information("RECOVERY_TEST_PASSED: RecoveryTime={Minutes}min within RTO={Target}min")`.
     - If `ActualRecoveryTime > RtoMinutes`: log warning: `Log.Warning("RECOVERY_TEST_SLOW: RecoveryTime={Minutes}min exceeds RTO={Target}min")`.

6. **Create `DisasterRecoveryRunbook` entity (AC-3 — documented procedures)**: Create in `src/UPACIP.Service/Recovery/Models/DisasterRecoveryRunbook.cs`:
   - `Guid Id` (PK).
   - `string Title` — e.g., "Full Database Recovery Procedure".
   - `string ScenarioType` — "FullDatabaseLoss", "PartialDataCorruption", "ServiceOutage", "SecurityBreach".
   - `string StepsJson` — JSON array of recovery steps, each with:
     ```json
     [
       { "stepNumber": 1, "action": "Identify failure scope", "estimatedMinutes": 15, "responsible": "DBA" },
       { "stepNumber": 2, "action": "Locate latest backup", "estimatedMinutes": 10, "responsible": "DBA" },
       { "stepNumber": 3, "action": "Restore from backup", "estimatedMinutes": 60, "responsible": "DBA" },
       { "stepNumber": 4, "action": "Replay WAL archives", "estimatedMinutes": 30, "responsible": "DBA" },
       { "stepNumber": 5, "action": "Verify data integrity", "estimatedMinutes": 30, "responsible": "Admin" },
       { "stepNumber": 6, "action": "Restore application services", "estimatedMinutes": 30, "responsible": "DevOps" },
       { "stepNumber": 7, "action": "Validate end-to-end functionality", "estimatedMinutes": 30, "responsible": "QA" },
       { "stepNumber": 8, "action": "Notify stakeholders", "estimatedMinutes": 15, "responsible": "Admin" }
     ]
     ```
   - `int TotalEstimatedMinutes` — sum of all step estimates. Must be ≤ 240 (RTO target).
   - `string Status` — "Draft", "Active", "Archived".
   - `int Version` — runbook version number.
   - `string CreatedBy`.
   - `DateTime CreatedAtUtc`.
   - `DateTime UpdatedAtUtc`.
   Add `DbSet<DisasterRecoveryRunbook>` to `ApplicationDbContext`.

7. **Implement `RecoveryController` API**: Create in `src/UPACIP.Api/Controllers/RecoveryController.cs`. All endpoints require `[Authorize(Roles = "Admin")]`.

   **GET `/api/admin/recovery/status`** — Current RPO/RTO compliance status:
   - Return `RecoveryTargetStatus` from the monitoring service's cached snapshot.

   **GET `/api/admin/recovery/tests`** — Quarterly test history:
   - Return paginated `RecoveryTestRecord` entries ordered by `ExecutedAtUtc` desc.
   - Support `quarter` filter (e.g., "2026-Q2").

   **POST `/api/admin/recovery/tests`** — Record a manual test result:
   - Accept `RecoveryTestRecord` body (for manually triggered tests outside the automated pipeline).
   - Validate `ActualRecoveryTime` is provided and `Quarter` matches current quarter.
   - Return `201 Created`.

   **GET `/api/admin/recovery/runbooks`** — List disaster recovery runbooks:
   - Return active runbooks, optionally filtered by `scenarioType`.

   **POST `/api/admin/recovery/runbooks`** — Create or update a runbook:
   - Accept runbook body. Validate `TotalEstimatedMinutes <= RtoMinutes`.
   - Auto-increment version for existing runbooks.
   - Return `201 Created`.

   **GET `/api/admin/recovery/runbooks/{runbookId}`** — Get specific runbook with steps:
   - Return full runbook with parsed steps for display.

8. **Seed default disaster recovery runbook**: Create seed data for one default runbook:
   - **Scenario: Full Database Loss** (8 steps, total 220 minutes — within 240-minute RTO):
     1. Identify failure scope (15 min)
     2. Locate latest backup from backup storage (10 min)
     3. Decrypt and restore base backup via pg_restore (60 min)
     4. Replay WAL archives to point-in-time (30 min)
     5. Verify data integrity (row counts, FK constraints, checksums) (30 min)
     6. Restart Windows Services (API, background jobs) (30 min)
     7. Validate end-to-end functionality (health checks, sample queries) (30 min)
     8. Notify stakeholders and document incident (15 min)

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   │   ├── CorrelationIdMiddleware.cs            ← from task_001
│   │   │   └── OperationLoggingMiddleware.cs         ← from task_001
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   │   └── [BackupService, BackupLog]            ← from US_088
│   │   ├── Compliance/
│   │   ├── Import/
│   │   ├── Logging/
│   │   │   ├── CorrelationIdAccessor.cs              ← from task_001
│   │   │   └── ...
│   │   ├── Migration/
│   │   ├── Monitoring/
│   │   │   └── WalArchivalMonitoringService.cs       ← from US_090
│   │   ├── PatientRights/
│   │   └── Resilience/
│   │       ├── ResiliencePipelineConfigurator.cs      ← from task_002
│   │       ├── TransientFaultClassifier.cs            ← from task_002
│   │       └── Models/
│   │           └── ResilienceOptions.cs               ← from task_002
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       └── Configurations/
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes US_088 (backup orchestration), US_090 (WAL archival), US_089 task_003 (restoration testing), US_095 task_001/task_002 are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Recovery/RecoveryTargetMonitoringService.cs | BackgroundService: monitors RPO via WAL/backup freshness, RTO readiness |
| CREATE | src/UPACIP.Service/Recovery/QuarterlyRecoveryTestScheduler.cs | BackgroundService: tracks quarterly test deadlines and alerts |
| CREATE | src/UPACIP.Service/Recovery/Models/RecoveryTargetOptions.cs | Config: RPO, RTO, monitoring interval, alert thresholds |
| CREATE | src/UPACIP.Service/Recovery/Models/RecoveryTargetStatus.cs | DTO: RPO/RTO compliance snapshot with warnings |
| CREATE | src/UPACIP.Service/Recovery/Models/DisasterRecoveryRunbook.cs | Entity: versioned recovery procedures with step-by-step time estimates |
| CREATE | src/UPACIP.Service/Recovery/Models/RecoveryTestRecord.cs | Entity: quarterly test execution history with actual recovery time |
| CREATE | src/UPACIP.Api/Controllers/RecoveryController.cs | Admin API: recovery status, test history, runbook CRUD |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<DisasterRecoveryRunbook>, DbSet<RecoveryTestRecord> |
| MODIFY | src/UPACIP.Api/Program.cs | Register monitoring and scheduler BackgroundServices, bind RecoveryTargetOptions |

## External References

- [PostgreSQL WAL Archiving — Point-in-Time Recovery](https://www.postgresql.org/docs/16/continuous-archiving.html)
- [BackgroundService — .NET 8](https://learn.microsoft.com/en-us/dotnet/core/extensions/timer-service)
- [RPO and RTO Definitions — Microsoft Azure](https://learn.microsoft.com/en-us/azure/reliability/disaster-recovery-overview)
- [HIPAA Contingency Planning — §164.308(a)(7)](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)

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

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] RPO monitoring detects when WAL archive gap exceeds 1 hour (AC-3, NFR-024)
- [ ] RTO readiness check validates backup, WAL, runbook, and quarterly test status (AC-3, NFR-025)
- [ ] RPO_VIOLATION critical alert fires when data protection gap exceeds target
- [ ] Quarterly test scheduler detects DueSoon and Overdue states (AC-3, DR-026)
- [ ] Recovery test record captures actual recovery time and compares against RTO
- [ ] Disaster recovery runbook total estimated minutes is ≤ 240 (RTO target)
- [ ] Default runbook seed data covers full database loss scenario
- [ ] API endpoints require Admin role authorization
- [ ] Recovery status endpoint returns cached compliance snapshot

## Implementation Checklist

- [ ] Create RecoveryTargetOptions with RPO, RTO, monitoring interval, alert thresholds
- [ ] Create RecoveryTargetStatus DTO with RPO/RTO compliance flags and warnings
- [ ] Implement RecoveryTargetMonitoringService checking WAL freshness and backup recency
- [ ] Create RecoveryTestRecord entity for quarterly test history tracking
- [ ] Implement QuarterlyRecoveryTestScheduler with DueSoon/Overdue alerting
- [ ] Create DisasterRecoveryRunbook entity with step-by-step recovery procedures
- [ ] Implement RecoveryController with status, test history, and runbook endpoints
- [ ] Seed default disaster recovery runbook for full database loss scenario
