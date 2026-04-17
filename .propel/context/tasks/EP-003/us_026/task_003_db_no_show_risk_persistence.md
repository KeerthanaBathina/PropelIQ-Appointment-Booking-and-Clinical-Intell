# Task - task_003_db_no_show_risk_persistence

## Requirement Reference

- User Story: US_026
- Story Location: .propel/context/tasks/EP-003/us_026/us_026.md
- Acceptance Criteria:
    - AC-1: Given an appointment exists, When the risk score is calculated, Then it produces a score from 0-100 based on patient history (past no-shows, cancellations) and appointment characteristics (time of day, day of week).
    - AC-3: Given insufficient historical data (new patient, <3 appointments), When the risk score is calculated, Then the system uses rule-based defaults and displays "Estimated" label.
    - AC-4: Given risk scores are computed, When the system evaluates slot swap priority, Then lower no-show risk patients are prioritized for preferred slots.
- Edge Case:
    - EC-1: Updated appointment history after a completed visit must be able to refresh stored score metadata on subsequent appointment creation.
    - EC-2: Maximum-risk patients must persist a capped score and outreach-needed indicator without violating data integrity rules.

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

Add persistence and query support for no-show risk metadata on appointments. This database task supports FR-014, AIR-006, and the slot-swap prioritization dependency from US_021 by storing the latest risk score, estimated-state flag, risk band, outreach-needed flag, and calculation timestamp on the appointment record. Persisting the score keeps staff dashboard and queue queries efficient, avoids recomputing scores on every list render, and gives downstream orchestration a stable field for prioritization.

## Dependent Tasks

- US_008 - Foundational - Requires `Appointment` entity and appointment history persistence
- task_002_ai_no_show_risk_scoring (Score and metadata contract must be defined before persistence fields are finalized)

## Impacted Components

- **MODIFY** `Appointment` entity - Add persisted risk score and metadata fields used by staff views and slot-swap prioritization (Server/Models/Entities/)
- **MODIFY** `AppDbContext` - Configure new columns, constraints, and indexes for risk-driven queries (Server/Data/)
- **CREATE** EF Core migration - Add appointment risk metadata columns and supporting indexes (Server/Data/Migrations/)

## Implementation Plan

1. **Add appointment-level risk metadata fields** for score, band, estimated flag, outreach-needed flag, and calculation timestamp.
2. **Constrain the score column safely** so persisted values stay within 0-100 and the band field supports only the defined low/medium/high states.
3. **Configure Fluent API mappings** in `AppDbContext` for the new columns, defaults, and nullability that support historical backfill and safe rollout.
4. **Add index support for staff queries** so schedule, queue, and slot-swap workflows can filter or sort by high-risk appointments efficiently.
5. **Create a migration path for existing appointments** that allows backfill or lazy recalculation without breaking current booking and queue data.
6. **Preserve update semantics** so recalculated scores overwrite the latest metadata while historical appointment records remain otherwise unchanged.
7. **Validate data integrity** for capped scores, estimated flags, and outreach markers during migration and runtime updates.

## Current Project State

```text
Server/
  Models/
    Entities/
      Appointment.cs
  Data/
    AppDbContext.cs
    Migrations/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Models/Entities/Appointment.cs | Add persisted no-show risk score and metadata fields |
| MODIFY | Server/Data/AppDbContext.cs | Configure risk score columns, constraints, and indexes |
| CREATE | Server/Data/Migrations/<timestamp>_AddAppointmentNoShowRiskMetadata.cs | Migration adding appointment risk metadata columns and indexes |

## External References

- EF Core entity properties: https://learn.microsoft.com/en-us/ef/core/modeling/entity-properties
- EF Core indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Add persisted appointment fields for no-show risk score, band, estimated-state, outreach-needed, and calculation timestamp
- [ ] Constrain score values to 0-100 and limit band values to the supported risk categories
- [ ] Configure EF Core mappings and indexes that support staff list and slot-swap prioritization queries
- [ ] Create a migration that rolls existing appointments forward safely without breaking current data access
- [ ] Support recalculation updates for future appointments when patient history changes
- [ ] Validate capped high-risk values and estimated-state persistence during migration and runtime writes