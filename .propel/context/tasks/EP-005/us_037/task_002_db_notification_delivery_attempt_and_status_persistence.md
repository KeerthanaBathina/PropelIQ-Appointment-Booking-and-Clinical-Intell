# Task - task_002_db_notification_delivery_attempt_and_status_persistence

## Requirement Reference

- User Story: US_037
- Story Location: .propel/context/tasks/EP-005/us_037/us_037.md
- Acceptance Criteria:
    - AC-1: Given any notification is sent, When delivery completes, Then the system logs the attempt with status, timestamp, channel, and recipient.
    - AC-2: Given a notification delivery fails, When the failure is detected, Then the system retries up to 3 times with exponential backoff.
    - AC-3: Given all retry attempts fail, When the final retry fails, Then the system marks the notification as `permanently_failed` and flags it for staff review.
    - AC-4: Given an admin filters the notification log, When statistics are calculated, Then success rate, failure rate, and average delivery time can be derived from persisted data.
- Edge Case:
    - EC-1: Buffered in-memory log entries must flush into durable storage without losing attempt order when persistence recovers.
    - EC-2: Existing notification history must remain readable while new status values and attempt-level records are introduced.

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

Extend notification persistence so the system can store both the overall lifecycle of a notification and each concrete delivery attempt needed for audit, retry, and monitoring. This database task keeps the existing `NotificationLog` model as the top-level notification record, expands it to support recipient details, final status, staff-review and contact-validation flags, and introduces attempt-level storage so every send or retry can be queried with its own timestamp and outcome. The schema must remain backward-compatible with existing notification rows while enabling efficient retry workers and admin-level statistics.

## Dependent Tasks

- US_008 - Foundational - `Appointment` and `NotificationLog` entities must exist
- US_033 task_003_db_sms_opt_out_and_notification_status (Current expanded notification status baseline must exist)
- US_035 task_003_db_reminder_checkpoint_and_notification_status_support (Current `cancelled-before-send` status support must exist)

## Impacted Components

- **NEW** `NotificationDeliveryAttempt` entity - Stores one row per send or retry with per-attempt status, channel, recipient, and timing data (Server/Models/Entities/)
- **MODIFY** `NotificationLog` entity - Add recipient, final-attempt timestamp, `permanently_failed`, staff-review flag, and contact-validation flag support (Server/Models/Entities/)
- **MODIFY** `Patient` entity - Add or reuse contact-validation metadata so bounced-email remediation can be tracked (Server/Models/Entities/)
- **MODIFY** `AppDbContext` - Register attempt persistence, relationships, indexes, and updated status mapping (Server/Data/)
- **CREATE** EF Core migration - Add delivery-attempt table plus notification-log status and analytics support (Server/Data/Migrations/)

## Implementation Plan

1. **Add a `NotificationDeliveryAttempt` entity** keyed to `NotificationLog` so each send attempt or retry can be stored with channel, recipient, attempt number, attempt timestamp, duration, status, and provider metadata.
2. **Extend `NotificationLog` as the aggregate record** by storing the normalized recipient value, current/final status, total retry count, first-attempt timestamp, final-attempt timestamp, and flags for staff review or patient contact validation.
3. **Introduce `permanently_failed` into the status model** without breaking existing statuses such as `sent`, `failed`, `bounced`, `opted-out`, and `cancelled-before-send`.
4. **Add indexes supporting retry workers and admin queries** across status, created-at or final-attempt timestamps, channel, and staff-review flags so failed-delivery scans and filtered reporting stay efficient.
5. **Preserve backward compatibility** by defaulting new columns safely for existing rows and backfilling attempt counts or timestamps only where required for nullable compatibility.
6. **Support ordered flushing from the in-memory buffer** by allowing attempt records to be inserted idempotently and sequenced consistently when persistence returns.

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
| CREATE | Server/Models/Entities/NotificationDeliveryAttempt.cs | Per-attempt audit record for sends and retries |
| MODIFY | Server/Models/Entities/NotificationLog.cs | Add recipient, terminal status support, timing fields, and remediation flags |
| MODIFY | Server/Models/Entities/Patient.cs | Persist contact-validation follow-up state for bounced email remediation |
| MODIFY | Server/Data/AppDbContext.cs | Configure attempt table, notification-log indexes, and updated enum mappings |
| CREATE | Server/Data/Migrations/<timestamp>_AddNotificationDeliveryAttemptAndStatusSupport.cs | Migration adding attempt-level logging and permanent-failure support |

## External References

- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/
- EF Core indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- EF Core relationships: https://learn.microsoft.com/en-us/ef/core/modeling/relationships/one-to-many

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Store every notification send and retry as an ordered attempt-level audit record
- [x] Extend `NotificationLog` to include recipient, terminal timing, staff-review, and contact-validation metadata
- [x] Add `permanently_failed` without breaking existing notification status values already planned in EP-005
- [x] Support efficient filtering by status, channel, and staff-review state for admin queries
- [x] Keep legacy notification rows backward-compatible during migration rollout
- [x] Allow buffered attempt records to flush safely into durable storage after transient persistence outages