# Task - task_003_db_sms_opt_out_and_notification_status

## Requirement Reference

- User Story: US_033
- Story Location: .propel/context/tasks/EP-005/us_033/us_033.md
- Acceptance Criteria:
    - AC-2: Given the patient opts out of SMS, When an SMS reminder is scheduled, Then the system skips SMS and logs "opted-out" status.
    - AC-4: Given delivery succeeds, When Twilio confirms delivery, Then status is logged as "sent" in NotificationLog.
- Edge Case:
    - EC-1: SMS opt-out preference must persist independently of email notifications so email-only delivery can continue.
    - EC-2: NotificationLog must represent `opted-out` as a first-class status in addition to existing sent, failed, and bounced outcomes.

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

Extend persistence for SMS opt-out preferences and SMS notification statuses. This database task supports FR-039 and TR-024 by storing patient-level SMS opt-out state independently from email preferences and by expanding `NotificationLog` status support so `opted-out` can be recorded alongside `sent`, `failed`, and `bounced`. The schema must remain backward-compatible with existing notification history while enabling reusable notification orchestration across EP-005.

## Dependent Tasks

- US_001 - Foundational - Backend scaffold and EF Core migration pipeline must exist
- US_008 - Foundational - `Patient` and `NotificationLog` entities must exist

## Impacted Components

- **MODIFY** `Patient` entity - Add SMS opt-out preference metadata while preserving email notification eligibility (Server/Models/Entities/)
- **MODIFY** `NotificationLog` entity - Extend delivery status support to include `opted-out` for SMS notifications (Server/Models/Entities/)
- **MODIFY** `AppDbContext` - Configure new patient preference field and notification status mapping updates (Server/Data/)
- **CREATE** EF Core migration - Add SMS opt-out persistence and update notification status constraints or enum mappings (Server/Data/Migrations/)

## Implementation Plan

1. **Extend the `Patient` model** with a persisted SMS opt-out preference that can be consulted independently of email delivery settings.
2. **Update `NotificationLog` status support** so SMS delivery can record `opted-out` as a valid outcome without overloading existing failure states.
3. **Maintain backward compatibility** by defaulting existing patient rows to SMS-enabled and preserving historical notification records.
4. **Update ORM mappings and constraints** so EF Core and PostgreSQL both understand the expanded delivery-status set.
5. **Provide a migration path** that safely introduces the new preference field and status values without breaking existing notification processing.

## Current Project State

```text
Server/
  Models/
    Entities/
      Patient.cs
      NotificationLog.cs
  Data/
    AppDbContext.cs
    Migrations/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Models/Entities/Patient.cs | Add persisted SMS opt-out preference state |
| MODIFY | Server/Models/Entities/NotificationLog.cs | Extend delivery status support to include `opted-out` |
| MODIFY | Server/Data/AppDbContext.cs | Configure SMS preference field and updated notification status mapping |
| CREATE | Server/Data/Migrations/<timestamp>_AddSmsOptOutAndNotificationStatus.cs | Migration adding SMS opt-out persistence and notification status support |

## External References

- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/
- EF Core value conversions and enums: https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions
- EF Core seeding guidance: https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [X] Persist SMS opt-out preference independently from email notifications
- [X] Extend `NotificationLog` so `opted-out` is a valid SMS delivery outcome
- [X] Keep existing patient rows and notification history backward-compatible
- [X] Update EF Core mappings and constraints for the expanded status set
- [X] Add a migration that safely introduces SMS preference and status changes