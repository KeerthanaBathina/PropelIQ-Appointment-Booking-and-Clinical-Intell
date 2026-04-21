# Task - task_004_db_ai_intake_session_persistence

## Requirement Reference

- User Story: US_027
- Story Location: .propel/context/tasks/EP-004/us_027/us_027.md
- Acceptance Criteria:
    - AC-3: Given mandatory fields are required, When the AI collects my information, Then it asks for full name, DOB, contact info, and emergency contact before marking mandatory collection complete.
    - AC-4: Given the intake session is active, When the AI displays a summary, Then I can review all collected information and correct any errors before submission.
    - AC-5: Given the conversation progresses, When I look at the progress bar, Then it accurately reflects the number of fields collected out of total required (e.g., "4/8 fields").
- Edge Case:
    - EC-1: Ambiguous-term clarification progress must survive autosave and resume without losing collected fields.
    - EC-2: Session timeout recovery must restore the last auto-saved state and last completed question from persisted data.

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

Add persistence support for AI conversational intake session state and autosave recovery. This database task extends the `IntakeData` model for EP-004 so the application can persist in-progress conversational collection state, required-field progress, last completed question, autosave timestamp, and summary-ready structured data without losing information when the patient times out or switches intake modes. The persistence model must support autosave every 30 seconds, seamless handoff to manual intake, and safe restoration of in-progress sessions.

## Dependent Tasks

- US_008 - Foundational - `IntakeData` entity must exist as the base intake persistence model
- US_028 - Same-Epic - Manual intake flow will consume carried-over AI-collected data from the same persistence record

## Impacted Components

- **MODIFY** `IntakeData` entity - Add in-progress conversational session fields and structured progress metadata (Server/Models/Entities/)
- **MODIFY** `AppDbContext` - Configure new columns, JSONB mappings, and indexes for active-session lookup (Server/Data/)
- **CREATE** EF Core migration - Add AI intake session metadata columns and indexes (Server/Data/Migrations/)

## Implementation Plan

1. **Extend `IntakeData`** with fields needed for conversational autosave and resume, such as session status, last completed question key, progress counters, autosaved conversation state, and last auto-saved timestamp.
2. **Store collected field data in structured JSONB columns** so both AI and manual intake flows can reuse the same partially completed patient information.
3. **Add active-session lookup support** with indexes for patient ID, status, and recent autosave timestamp so resume queries remain efficient.
4. **Support summary review state** by persisting a normalized snapshot of collected mandatory and optional fields ready for review and correction.
5. **Preserve switch-to-manual compatibility** so AI-collected data can populate the manual form without separate duplication tables.
6. **Create a migration path for existing intake rows** that safely defaults new session-tracking fields for legacy records.
7. **Validate autosave integrity** so partial updates do not corrupt required-field progress or completed-at state.

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
| MODIFY | Server/Models/Entities/IntakeData.cs | Add conversational session state, progress, and autosave metadata |
| MODIFY | Server/Data/AppDbContext.cs | Configure AI intake session columns, JSONB mappings, and resume indexes |
| CREATE | Server/Data/Migrations/<timestamp>_AddAIIntakeSessionState.cs | Migration adding AI conversational intake session metadata |

## External References

- EF Core JSON columns: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-8.0/whatsnew#json
- EF Core indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Add conversational session-state fields to `IntakeData` for autosave, resume, and summary readiness
- [x] Persist collected mandatory and optional field data in a structure reusable by both AI and manual intake flows
- [x] Configure indexes for fast active-session lookup by patient and recent autosave timestamp
- [x] Support switch-to-manual data carryover without creating duplicate intake records
- [x] Create a migration that safely introduces AI intake state fields for existing records
- [x] Validate autosave and resume integrity for partially completed sessions