# Task - task_003_db_intake_mode_switching_attribution

## Requirement Reference

- User Story: US_029
- Story Location: .propel/context/tasks/EP-004/us_029/us_029.md
- Acceptance Criteria:
    - AC-3: Given I switch modes, When the transition completes, Then no data is lost and all previously provided answers are preserved.
    - AC-4: Given I switch modes multiple times, When I eventually submit, Then the final intake contains the merged data from all modes with correct source attribution.
- Edge Case:
    - EC-1: Conflicting AI and manual values must preserve the most recent answer while storing a conflict note in the intake record.
    - EC-2: Repeated switches must retain enough provenance metadata to reconstruct how the final merged intake was produced.

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

Extend the shared `IntakeData` persistence model so repeated mode switches can preserve per-field source attribution and conflict history without creating duplicate intake records. This database task closes the persistence gap left by US_027 and US_028 by adding structured metadata for field provenance, switch events, and conflict notes, enabling the application to reconstruct which values came from AI versus manual entry and which newer value superseded an older one at submission time.

## Dependent Tasks

- US_027 task_004_db_ai_intake_session_persistence (Shared intake JSONB persistence and autosave metadata must exist)
- US_028 task_002_be_manual_intake_api (Manual intake APIs should consume the shared attribution model once available)

## Impacted Components

- **MODIFY** `IntakeData` entity - Add field-source attribution, conflict note, and mode-switch history metadata to the shared intake record (Server/Models/Entities/)
- **MODIFY** `AppDbContext` - Configure JSONB columns or owned types for attribution and switch-history persistence (Server/Data/)
- **CREATE** EF Core migration - Add mode-switch provenance and conflict metadata support (Server/Data/Migrations/)

## Implementation Plan

1. **Extend `IntakeData`** with structured metadata describing field source, last-updated mode, and timestamp for each collected answer.
2. **Add conflict-history storage** so when a newer AI or manual value overrides an earlier answer, the replaced value and its source remain auditable inside the intake record.
3. **Persist mode-switch events** capturing source mode, target mode, switch timestamp, and session correlation data for repeated transitions.
4. **Keep the storage model JSONB-first** so provenance metadata remains flexible without fragmenting intake data across new relational tables.
5. **Preserve backward compatibility** by defaulting new provenance fields for existing in-progress and completed intake rows.
6. **Add query support for active-record recovery** so resume and final-submit flows can efficiently fetch the current merged value plus attribution metadata.

## Current Project State

```text
Server/
  Models/
    Entities/
      IntakeData.cs
  Data/
    AppDbContext.cs
    Migrations/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Models/Entities/IntakeData.cs | Add provenance, conflict-history, and switch-history metadata to intake persistence |
| MODIFY | Server/Data/AppDbContext.cs | Configure JSONB mappings and indexes for mode-switch attribution fields |
| CREATE | Server/Data/Migrations/<timestamp>_AddIntakeModeSwitchAttribution.cs | Migration adding provenance and conflict metadata support |

## External References

- EF Core JSON columns: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-8.0/whatsnew#json
- EF Core owned entities: https://learn.microsoft.com/en-us/ef/core/modeling/owned-entities
- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Persist per-field source attribution for values collected by AI and manual intake modes
- [ ] Store conflict notes when a newer value replaces an earlier answer from a different mode
- [ ] Record mode-switch history without creating duplicate intake rows
- [ ] Keep the attribution model backward-compatible for existing intake records
- [ ] Support efficient retrieval of merged values plus provenance during resume and submit flows
- [ ] Add a migration that safely introduces the new metadata to the shared intake record