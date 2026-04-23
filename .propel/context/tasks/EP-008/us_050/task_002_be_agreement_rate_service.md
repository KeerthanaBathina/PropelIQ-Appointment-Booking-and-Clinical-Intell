# Task - task_002_be_agreement_rate_service

## Requirement Reference

- User Story: US_050
- Story Location: .propel/context/tasks/EP-008/us_050/us_050.md
- Acceptance Criteria:
  - AC-1: Given staff verify or override AI-suggested codes, When the agreement rate is calculated, Then the system computes the percentage of AI codes approved without override (target >98%).
  - AC-2: Given the agreement rate is calculated daily, When an admin views the metrics dashboard, Then the daily and rolling 30-day agreement rate is displayed.
  - AC-3: Given a coding discrepancy exists (multiple codes for single diagnosis/procedure), When the system detects it, Then the discrepancy is flagged with a breakdown of AI suggestion vs. staff selection.
  - AC-4: Given the agreement rate drops below 98%, When the daily calculation runs, Then the system generates an alert for admin review with a summary of disagreement patterns.
- Edge Case:
  - Insufficient data for statistical significance: System displays "Not enough data" with minimum threshold indicator (requires 50+ verified codes per period). Service must return `meets_minimum_threshold: false` when count < 50.
  - Partial overrides (correct code but wrong modifier) count as disagreements in the agreement rate.

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
| ORM | Entity Framework Core | 8.x |
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

Implement the backend service layer and API endpoints for AI-human agreement rate calculation, coding discrepancy detection, and alert generation. This service queries the MedicalCode table to compute daily approval rates, detects discrepancies where AI suggestions diverge from staff selections, calculates rolling 30-day averages, and generates alerts when the rate drops below the 98% target threshold. Implements FR-067 (agreement rate calculation) and FR-068 (discrepancy flagging) with support for AIR-Q09 (daily quality monitoring).

## Dependent Tasks

- task_001_db_agreement_rate_schema — AgreementRateMetric and CodingDiscrepancy tables must exist.
- US_049 — MedicalCode records with `suggested_by_ai`, `approved_by_user_id`, and override data must be populated.

## Impacted Components

- **NEW** — `IAgreementRateService` interface (Server/Services/)
- **NEW** — `AgreementRateService` implementation (Server/Services/)
- **NEW** — `AgreementRateController` (Server/Controllers/)
- **NEW** — `AgreementRateDto`, `CodingDiscrepancyDto`, `AgreementAlertDto` DTOs (Server/DTOs/)
- **NEW** — `AgreementRateCalculationJob` background job (Server/Jobs/)
- **MODIFY** — `Program.cs` — Register service and background job in DI container

## Implementation Plan

1. Create DTOs for agreement rate metrics, discrepancies, and alerts.
2. Define `IAgreementRateService` interface with methods: `CalculateDailyRate`, `GetAgreementMetrics`, `GetDiscrepancies`, `GetAlerts`.
3. Implement `AgreementRateService`:
   - Query MedicalCode table for codes where `suggested_by_ai = true` within date range.
   - Compute daily rate: `codes_approved_without_override / total_verified_codes * 100`.
   - Partial overrides (same code_value but different modifier) count as disagreements.
   - Calculate rolling 30-day rate from stored daily metrics.
   - Check minimum threshold (50+ verified codes) and set `meets_minimum_threshold` flag.
   - Detect discrepancies: find MedicalCode records where `suggested_by_ai = true` AND `approved_by_user_id IS NOT NULL` AND the original AI suggestion differs from the final code value.
   - Generate alerts when daily rate < 98% with disagreement pattern summary.
4. Implement `AgreementRateCalculationJob` as a background hosted service running daily.
5. Create `AgreementRateController` with RESTful endpoints.
6. Register services in DI and configure background job scheduling.

## Current Project State

- Placeholder — to be updated based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/DTOs/AgreementRateDto.cs | DTO for agreement rate metrics response |
| CREATE | Server/DTOs/CodingDiscrepancyDto.cs | DTO for discrepancy records |
| CREATE | Server/DTOs/AgreementAlertDto.cs | DTO for agreement rate alerts |
| CREATE | Server/Services/IAgreementRateService.cs | Service interface |
| CREATE | Server/Services/AgreementRateService.cs | Service implementation with calculation logic |
| CREATE | Server/Controllers/AgreementRateController.cs | API endpoints for agreement rate data |
| CREATE | Server/Jobs/AgreementRateCalculationJob.cs | Daily background job for rate computation |
| MODIFY | Server/Program.cs | Register IAgreementRateService, AgreementRateCalculationJob in DI |

## External References

- [ASP.NET Core 8 Background Tasks — Microsoft Docs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)
- [EF Core 8 Querying — Microsoft Docs](https://learn.microsoft.com/en-us/ef/core/querying/)
- [ICD-10-CM Official Coding Guidelines](https://www.cms.gov/Medicare/Coding/ICD10)
- [CPT Code Structure — AMA](https://www.ama-assn.org/practice-management/cpt)

## Build Commands

- `dotnet build Server`
- `dotnet test Server.Tests`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] API endpoints return correct status codes (200, 404, 422)
- [ ] Daily calculation job executes without errors
- [ ] Rate calculation accuracy verified with known test data
- [ ] Minimum threshold logic returns correct flag at boundary (49 vs 50 codes)

## Implementation Checklist

- [x] Create `AgreementRateDto` with fields: `calculationDate`, `dailyAgreementRate`, `rolling30DayRate`, `totalCodesVerified`, `codesApprovedWithoutOverride`, `codesOverridden`, `codesPartiallyOverridden`, `meetsMinimumThreshold`, `targetRate` (98.0). Create `CodingDiscrepancyDto` with fields: `discrepancyId`, `patientId`, `aiSuggestedCode`, `staffSelectedCode`, `codeType`, `discrepancyType`, `overrideJustification`, `detectedAt`. Create `AgreementAlertDto` with fields: `alertDate`, `currentRate`, `targetRate`, `disagreementPatterns` (list of top discrepancy categories).
- [x] Define `IAgreementRateService` interface with methods: `Task<AgreementRateDto> CalculateDailyRateAsync(DateOnly date)`, `Task<AgreementRateDto> GetLatestMetricsAsync()`, `Task<List<AgreementRateDto>> GetMetricsRangeAsync(DateOnly from, DateOnly to)`, `Task<List<CodingDiscrepancyDto>> GetDiscrepanciesAsync(DateOnly? from, DateOnly? to)`, `Task<List<AgreementAlertDto>> GetActiveAlertsAsync()`.
- [x] Implement `AgreementRateService.CalculateDailyRateAsync`: Query MedicalCode where `suggested_by_ai = true` and `updated_at` falls within the target date. Count approved-without-override (approved_by_user_id != null AND code value matches AI suggestion). Count overridden (approved_by_user_id != null AND code value differs). Partial overrides (same base code, different modifier) count as disagreements. Compute rate as `(approved_without_override / total_verified) * 100`. Set `meets_minimum_threshold = total_verified >= 50`. Persist result to AgreementRateMetric table.
- [x] Implement rolling 30-day rate calculation: Query AgreementRateMetric for the last 30 days. Compute weighted average: `SUM(codes_approved_without_override) / SUM(total_codes_verified) * 100`. Return null if fewer than 7 days of data exist.
- [x] Implement discrepancy detection: Query MedicalCode records where AI suggestion differs from final approved code. Create CodingDiscrepancy records with `ai_suggested_code`, `staff_selected_code`, `code_type`, `discrepancy_type`, and `override_justification`. Detect multi-code discrepancies where multiple codes map to a single diagnosis/procedure (FR-068).
- [x] Implement alert generation: After daily calculation, check if `daily_agreement_rate < 98.0`. If below threshold, compile disagreement pattern summary (top 5 override categories by frequency). Persist alert and make available via `GetActiveAlertsAsync`.
- [x] Implement `AgreementRateCalculationJob` as `BackgroundService` with daily execution using `PeriodicTimer`. Schedule execution at midnight (configurable via appsettings.json). Include error handling with structured logging (Serilog) and retry on transient DB failures (max 3 retries, exponential backoff per NFR-032).
- [x] Create `AgreementRateController` with endpoints: `GET /api/coding/agreement-rate` (latest metrics), `GET /api/coding/agreement-rate/history?from={date}&to={date}` (date range), `GET /api/coding/discrepancies?from={date}&to={date}` (discrepancy list), `GET /api/coding/agreement-rate/alerts` (active alerts). Authorize all endpoints for Admin role only (RBAC per NFR-011). Return 422 when date range exceeds 90 days. Include OpenAPI annotations for Swagger documentation (NFR-038).
