# Task - task_001_db_agreement_rate_schema

## Requirement Reference

- User Story: US_050
- Story Location: .propel/context/tasks/EP-008/us_050/us_050.md
- Acceptance Criteria:
  - AC-1: System computes the percentage of AI codes approved without override (target >98%).
  - AC-2: Daily and rolling 30-day agreement rate is displayed (requires persisted daily metrics).
- Edge Case:
  - Insufficient data for statistical significance: System displays "Not enough data" with minimum threshold indicator (requires 50+ verified codes per period).
  - Partial overrides count as disagreements in the agreement rate (schema must support tracking override type).

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
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |

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

Add database schema and EF Core migration to persist daily AI-human agreement rate metrics and coding discrepancy records. This provides the storage layer for FR-067 (agreement rate calculation) and FR-068 (coding discrepancy flagging). The schema captures daily computed rates, rolling 30-day aggregates, verified code counts, and individual discrepancy records linking AI-suggested codes to staff-selected codes.

## Dependent Tasks

- US_008 (Foundational) — MedicalCode entity must exist in the database with `suggested_by_ai`, `approved_by_user_id`, and `ai_confidence_score` columns.
- US_049 (Same Epic) — Verified and overridden coding data must be available in the MedicalCode table.

## Impacted Components

- **NEW** — `AgreementRateMetric` entity (Server/Models/)
- **NEW** — `CodingDiscrepancy` entity (Server/Models/)
- **NEW** — EF Core migration for both tables (Server/Migrations/)
- **MODIFY** — `ApplicationDbContext` — Register new DbSets

## Implementation Plan

1. Define `AgreementRateMetric` entity with columns for daily and rolling metrics, verified code counts, and computation metadata.
2. Define `CodingDiscrepancy` entity to track individual discrepancies between AI-suggested and staff-selected codes.
3. Register both entities as DbSets in `ApplicationDbContext`.
4. Add EF Core migration with rollback support.
5. Create indexes for efficient querying by date range and patient context.

## Current Project State

- Placeholder — to be updated based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/AgreementRateMetric.cs | Entity for daily agreement rate metrics |
| CREATE | Server/Models/CodingDiscrepancy.cs | Entity for coding discrepancy records |
| MODIFY | Server/Data/ApplicationDbContext.cs | Add DbSet<AgreementRateMetric> and DbSet<CodingDiscrepancy> |
| CREATE | Server/Migrations/{timestamp}_AddAgreementRateMetrics.cs | EF Core migration |

## External References

- [PostgreSQL 16 Documentation — CREATE TABLE](https://www.postgresql.org/docs/16/sql-createtable.html)
- [EF Core 8 Migrations — Microsoft Docs](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [ICD-10-CM Code Structure](https://www.cms.gov/Medicare/Coding/ICD10)

## Build Commands

- `dotnet ef migrations add AddAgreementRateMetrics --project Server`
- `dotnet ef database update --project Server`
- `dotnet build Server`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies forward successfully
- [ ] Migration rolls back successfully
- [ ] Foreign key constraints validated against MedicalCode and Patient tables
- [ ] Indexes created and queryable

## Implementation Checklist

- [ ] Create `AgreementRateMetric` entity with columns: `metric_id` (UUID PK), `calculation_date` (date, unique), `daily_agreement_rate` (decimal), `rolling_30day_rate` (decimal nullable), `total_codes_verified` (int), `codes_approved_without_override` (int), `codes_overridden` (int), `codes_partially_overridden` (int), `meets_minimum_threshold` (bool — true when total_codes_verified >= 50), `created_at` (timestamp), `updated_at` (timestamp)
- [ ] Create `CodingDiscrepancy` entity with columns: `discrepancy_id` (UUID PK), `medical_code_id` (FK to MedicalCode), `patient_id` (FK to Patient), `ai_suggested_code` (string), `staff_selected_code` (string), `code_type` (enum: icd10, cpt), `discrepancy_type` (enum: full_override, partial_override, multiple_codes), `override_justification` (text), `detected_at` (timestamp), `created_at` (timestamp)
- [ ] Add unique index on `AgreementRateMetric.calculation_date` for efficient daily lookup
- [ ] Add composite index on `CodingDiscrepancy(patient_id, detected_at)` for patient-level queries
- [ ] Add foreign key constraints: `CodingDiscrepancy.medical_code_id` → `MedicalCode.code_id`, `CodingDiscrepancy.patient_id` → `Patient.patient_id`
- [ ] Register DbSets in `ApplicationDbContext` and generate EF Core migration with rollback support
