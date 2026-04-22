# Task - task_003_db_reminder_checkpoint_and_notification_status_support

## Requirement Reference

- User Story: US_035
- Story Location: .propel/context/tasks/EP-005/us_035/us_035.md
- Acceptance Criteria:
    - AC-3: Given a patient has opted out of SMS, When the 24-hour reminder job runs, Then only the email reminder is sent and SMS is skipped with "opted-out" logged.
    - AC-5: Given an appointment is cancelled before the reminder window, When the batch job processes it, Then the reminder is skipped and status logged as "cancelled-before-send."
    - AC-4: Given the reminder batch job runs, When it processes all scheduled appointments, Then the entire batch completes within 10 minutes.
- Edge Case:
    - EC-1: Mid-batch failures must resume from the last successful record on the next run using persisted checkpoint tracking.
    - EC-2: Checkpoint storage must remain separate for 24-hour and 2-hour batches so the two windows do not overwrite each other's progress.

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

Extend persistence to support reminder batch checkpointing and new reminder-specific status outcomes. This database task adds durable checkpoint storage so 24-hour and 2-hour reminder runs can resume from the last successful appointment after failure, and expands notification status support so `cancelled-before-send` can be audited alongside existing `sent`, `failed`, `bounced`, and `opted-out` outcomes. The schema must remain backward-compatible with the existing notification history while enabling deterministic resume behavior for reminder workers.

## Dependent Tasks

- US_008 - Foundational - `Appointment` and `NotificationLog` entities must exist
- US_033 task_003_db_sms_opt_out_and_notification_status (Expanded notification status model must exist as the current baseline)

## Impacted Components

- **NEW** `ReminderBatchCheckpoint` entity - Persist per-batch cursor, last successful appointment, and run metadata for 24-hour and 2-hour workers (Server/Models/Entities/)
- **MODIFY** `NotificationLog` entity - Extend status support to include `cancelled-before-send` for reminder skips (Server/Models/Entities/)
- **MODIFY** `AppDbContext` - Register checkpoint persistence, indexes, and updated notification status mapping (Server/Data/)
- **CREATE** EF Core migration - Add reminder checkpoint table and updated notification status support (Server/Data/Migrations/)

## Implementation Plan

1. **Create a `ReminderBatchCheckpoint` entity** that stores reminder batch type, clinic-date execution window, last successful appointment identifier, last processed scheduled time, run status, and updated timestamp.
2. **Keep checkpoint rows independent per reminder window** so 24-hour and 2-hour batches track progress separately and do not overwrite each other.
3. **Extend `NotificationLog` status support** so reminder skips caused by cancelled appointments can be recorded explicitly as `cancelled-before-send`.
4. **Add indexes supporting resume queries** by batch type, execution window, and last updated timestamp so the worker can resume quickly without scanning old runs.
5. **Preserve backward compatibility** by defaulting existing notification rows and allowing current statuses to remain valid without data rewrite.
6. **Provide a safe migration path** that introduces checkpoint persistence and status expansion without breaking existing email, SMS, confirmation, or waitlist notification flows.

## Current Project State

```text
Server/
  Models/
    Entities/
      Appointment.cs
      NotificationLog.cs
      Patient.cs
  Data/
    AppDbContext.cs
    Migrations/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/Entities/ReminderBatchCheckpoint.cs | Entity for persisted 24-hour and 2-hour reminder batch checkpoints |
| MODIFY | Server/Models/Entities/NotificationLog.cs | Add `cancelled-before-send` reminder delivery outcome support |
| MODIFY | Server/Data/AppDbContext.cs | Configure reminder checkpoint table, indexes, and updated notification status mapping |
| CREATE | Server/Data/Migrations/<timestamp>_AddReminderCheckpointAndStatusSupport.cs | Migration adding reminder checkpoint persistence and notification status expansion |

## External References

- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/
- EF Core indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- EF Core value conversions and enums: https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Persist separate checkpoint rows for 24-hour and 2-hour reminder batches
- [x] Store last successful reminder cursor data needed for mid-batch resume
- [x] Extend `NotificationLog` to represent `cancelled-before-send` explicitly
- [x] Add indexes that support fast checkpoint resume and recent-run lookup
- [x] Keep existing notification history backward-compatible during migration rollout
- [x] Add a migration that introduces checkpoint and status support safely