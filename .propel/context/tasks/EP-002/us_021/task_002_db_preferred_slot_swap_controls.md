# Task - task_002_db_preferred_slot_swap_controls

## Requirement Reference

- User Story: US_021
- Story Location: .propel/context/tasks/EP-002/us_021/us_021.md
- Acceptance Criteria:
    - AC-1: Given I have an appointment and registered preferred slot criteria, When a matching preferred slot opens (cancellation or new availability), Then the system automatically swaps my appointment to the preferred slot.
    - AC-2: Given the swap occurs, When it completes, Then my original slot is released within 1 minute and I receive a notification with old and new appointment times.
    - AC-3: Given staff has disabled auto-swap for my account, When a preferred slot opens, Then the system skips the swap and logs the reason.
    - AC-4: Given multiple patients prefer the same slot, When the slot opens, Then the system prioritizes by longest wait time and lowest no-show risk score.
    - AC-5: Given a preferred slot opens less than 24 hours before the appointment, When the system evaluates the swap, Then it skips automatic swap and notifies me for manual confirmation instead.
- Edge Case:
    - EC-1: Conflict retries need indexed preferred-slot lookups so the processor can move to the next candidate quickly.
    - EC-2: Checked-in appointments need queryable arrival-state data so `arrived` and `in-visit` rows are excluded efficiently.

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

Add the persistence and query support required for dynamic preferred-slot swap control. This database task supports FR-019, FR-020, FR-043, DR-002, and NFR-035 by introducing patient-level auto-swap control fields, recording who disabled automatic swaps and why, and adding indexes that make preferred-slot matching and checked-in exclusions efficient. It also ensures the appointment model’s `preferred_slot_criteria` JSONB field can be queried efficiently during swap evaluation and that notification typing can distinguish automatic swap completion from manual-confirmation offers if needed.

## Dependent Tasks

- US_008 - Foundational - Requires Appointment, Patient, NotificationLog, and QueueEntry entities

## Impacted Components

- **MODIFY** `Patient` entity - Add auto-swap enablement and staff-override audit fields (Server/Models/Entities/)
- **MODIFY** `Appointment` entity or EF configuration - Optimize `preferred_slot_criteria` querying and swap metadata persistence if needed (Server/Models/Entities/ or Server/Data/)
- **MODIFY** `AppDbContext` - Add indexes and field mappings for swap evaluation and control lookups (Server/Data/)
- **NEW** EF Core migration for auto-swap controls and preferred-slot indexes (Server/Migrations/)
- **MODIFY** notification log model or enum mapping - Distinguish slot-swap completion vs manual-confirmation notifications when persisted in code (Server/Models/Entities/ or Server/Data/)

## Implementation Plan

1. **Extend the patient model with auto-swap controls** by adding fields such as `AutoSwapEnabled`, `AutoSwapDisabledReason`, `AutoSwapDisabledAtUtc`, and `AutoSwapDisabledByUserId` so staff overrides are queryable and auditable.
2. **Add EF Core migration support** for the new patient fields and any supporting constraints or foreign keys needed for the staff-attribution field.
3. **Add query support for `preferred_slot_criteria`** by configuring a GIN or equivalent PostgreSQL index strategy over the JSONB field so preferred-slot matching can execute efficiently.
4. **Add indexes supporting eligibility filters** that combine appointment status, appointment time, and queue/arrival state joins needed to exclude `arrived` and `in-visit` appointments.
5. **Ensure notification persistence can distinguish swap outcomes** so automatic swap notifications and manual-confirmation notifications can be audited independently if the application models notification type in code.
6. **Document or validate migration-safe defaults** so existing patients remain auto-swap enabled unless staff explicitly disables the behavior.

## Current Project State

```text
Server/
  Data/
    AppDbContext.cs
  Models/
    Entities/
      Appointment.cs
      Patient.cs
      NotificationLog.cs
      QueueEntry.cs
  Migrations/
    (existing EF Core migrations)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Models/Entities/Patient.cs | Add auto-swap enablement flag and staff-override audit fields |
| MODIFY | Server/Data/AppDbContext.cs | Configure new fields and indexes for preferred-slot and arrival-state queries |
| CREATE | Server/Migrations/{timestamp}_AddPreferredSlotSwapControls.cs | Migration adding patient auto-swap controls and preferred-slot indexes |
| MODIFY | Server/Models/Entities/NotificationLog.cs | Add support for slot-swap notification typing if modeled explicitly |

## External References

- PostgreSQL JSONB indexing: https://www.postgresql.org/docs/16/datatype-json.html#JSON-INDEXING
- PostgreSQL indexes documentation: https://www.postgresql.org/docs/16/indexes.html
- EF Core 8 indexes configuration: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- EF Core 8 relationships: https://learn.microsoft.com/en-us/ef/core/modeling/relationships
- EF Core 8 migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Add patient-level auto-swap control fields with safe defaults so existing accounts remain eligible unless staff disables the feature
- [ ] Persist staff override reason, timestamp, and actor so skipped swaps are queryable and auditable
- [ ] Add a JSONB index strategy for efficient matching on `preferred_slot_criteria`
- [ ] Add indexes or mappings that support excluding `arrived` and `in-visit` appointments during swap evaluation
- [ ] Extend notification typing support so auto-swap and manual-confirmation notifications can be differentiated if required by the code model
- [ ] Verify migration defaults and constraints keep current booking behavior backward compatible