# Task - task_003_db_minor_guardian_and_insurance_persistence

## Requirement Reference

- User Story: US_031
- Story Location: .propel/context/tasks/EP-004/us_031/us_031.md
- Acceptance Criteria:
    - AC-1: Given a patient's DOB indicates they are under 18, When they attempt to book, Then the system requires guardian consent acknowledgment before proceeding.
    - AC-2: Given insurance details are provided during intake, When the soft pre-check runs, Then the system validates against dummy records and displays "Valid" or "Needs Review" status.
    - AC-3: Given insurance validation fails, When the failure is detected, Then the system flags the record for staff review and sends a notification to the staff dashboard.
- Edge Case:
    - EC-1: Guardian consent persistence must capture enough detail to verify guardian age >= 18.
    - EC-2: Missing insurance information must still be represented in a way that staff follow-up can be derived from stored intake state.

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
| Language | C# | 12 / .NET 8 |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

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

Extend persistence to store guardian consent and insurance validation outcomes, and introduce the dummy insurance reference records used by the soft pre-check. This database task supports FR-032, FR-033, and FR-034 by enabling the application to persist guardian identity and acknowledgment metadata for minors, store insurance validation result and staff-review flags on intake records, and seed the dummy insurance dataset used during validation without depending on external payer systems.

## Dependent Tasks

- US_008 - Foundational - `Patient` and `IntakeData` entities must exist
- US_027 task_004_db_ai_intake_session_persistence (Shared intake JSONB persistence must exist)

## Impacted Components

- **MODIFY** `IntakeData` entity - Add guardian consent metadata, insurance validation status, and staff-review flags (Server/Models/Entities/)
- **CREATE** `InsuranceValidationRecord` entity - Dummy-record reference source for soft insurance pre-check results (Server/Models/Entities/)
- **MODIFY** `AppDbContext` - Configure new guardian and insurance fields plus dummy-record table mappings (Server/Data/)
- **CREATE** EF Core migration - Add guardian-consent and insurance-validation persistence, plus seed dummy insurance records (Server/Data/Migrations/)

## Implementation Plan

1. **Extend `IntakeData`** with fields or JSON metadata for guardian name, guardian DOB, guardian consent acknowledgment, insurance validation status, review reason, and staff-follow-up requirement.
2. **Create a dummy insurance validation entity or table** keyed by insurer and policy identifiers so the backend can perform deterministic soft checks without external integrations.
3. **Seed representative dummy insurance records** through the migration so development and QA environments have a stable validation source.
4. **Persist skipped-insurance states** distinctly from failed validation so staff can tell whether information was missing versus invalid.
5. **Support efficient review lookup** by indexing intake rows that require staff follow-up after failed or skipped insurance validation.
6. **Keep backward compatibility** by defaulting new fields safely for existing intake rows and patient records.

## Current Project State

```text
Server/
  Models/
    Entities/
      Patient.cs
      IntakeData.cs
  Data/
    AppDbContext.cs
    Migrations/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Models/Entities/IntakeData.cs | Add guardian consent, insurance validation status, and staff-review metadata |
| CREATE | Server/Models/Entities/InsuranceValidationRecord.cs | Dummy insurance reference entity for soft validation |
| MODIFY | Server/Data/AppDbContext.cs | Configure guardian and insurance validation persistence plus dummy-record mappings |
| CREATE | Server/Data/Migrations/<timestamp>_AddMinorGuardianAndInsuranceValidation.cs | Migration adding new fields and seeding dummy insurance records |

## External References

- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/
- EF Core indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- EF Core seeding guidance: https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Persist guardian consent details needed to verify minor booking eligibility
- [ ] Store insurance validation outcome, review reason, and staff-follow-up requirement on intake records
- [ ] Create and seed dummy insurance validation records for deterministic pre-checks
- [ ] Distinguish skipped insurance collection from failed validation in persisted status data
- [ ] Support efficient lookup of intake records that require staff review
- [ ] Add a migration that introduces the new schema without breaking existing intake rows