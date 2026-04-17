# Task - task_003_db_waitlist_schema

## Requirement Reference

- User Story: US_020
- Story Location: .propel/context/tasks/EP-002/us_020/us_020.md
- Acceptance Criteria:
    - AC-1: Given all slots for my preferred time are booked, When I select "Join Waitlist", Then I am registered on the waitlist with my preferred criteria (date, time, provider).
    - AC-2: Given I am on the waitlist, When a matching slot becomes available, Then I receive a notification (email + SMS) within 5 minutes of slot availability.
    - AC-4: Given multiple patients are waitlisted for the same slot, When the slot opens, Then the system notifies all waitlisted patients and the first to confirm books the slot.
- Edge Case:
    - EC-1: Waitlist entries remain active when unrelated appointments are cancelled.
    - EC-2: Offers for slots inside 24 hours still need persisted notification timestamps for audit and troubleshooting.

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

Add the persistence model required for waitlist registration and offer tracking. This database task supports FR-017, FR-018, FR-042, DR-007, and DR-019 by introducing a `WaitlistEntry` entity/table for preferred slot criteria, active/claimed/expired lifecycle state, and offer timestamps, plus the indexes needed for fast matching when a slot opens. It also extends notification logging support so waitlist-offer deliveries can be audited and retained consistently with the rest of the notification subsystem.

## Dependent Tasks

- US_008 - Foundational - Requires Appointment and NotificationLog entities

## Impacted Components

- **NEW** `WaitlistEntry` entity for persisted patient waitlist criteria and lifecycle state (Server/Models/Entities/)
- **NEW** EF Core migration for waitlist table and indexes (Server/Migrations/)
- **MODIFY** `AppDbContext` - Add `DbSet<WaitlistEntry>` and model configuration (Server/Data/)
- **MODIFY** notification log model or enum mapping - Add waitlist-offer notification type support (Server/Models/Entities/ or Server/Data/)

## Implementation Plan

1. **Create a `WaitlistEntry` entity** with fields for `WaitlistEntryId`, `PatientId`, `PreferredDate`, `PreferredStartTime`, `PreferredEndTime`, `ProviderId`, `AppointmentType`, `Status`, `LastNotifiedAtUtc`, `ClaimedAtUtc`, `CreatedAtUtc`, and `UpdatedAtUtc`.
2. **Define lifecycle status values** that support active registration and offer processing, such as `active`, `offered`, `claimed`, `booked`, `expired`, and `removed`, while keeping unrelated appointment cancellation from changing entry status.
3. **Add EF Core configuration and migration** creating the waitlist table with foreign keys to patient and optional provider records, required constraints, and UTC timestamp columns.
4. **Create indexes for matching queries** covering active entries by preferred date, provider, appointment type, and notification recency so released-slot matching can execute quickly.
5. **Add a uniqueness constraint** preventing duplicate active waitlist entries for the same patient and criteria while still allowing historical expired or removed records.
6. **Extend notification log support** so waitlist-offer deliveries can be recorded distinctly from confirmations and reminders, preserving audit and retention behavior.

## Current Project State

```text
Server/
  Data/
    AppDbContext.cs
  Models/
    Entities/
      Appointment.cs
      NotificationLog.cs
      Patient.cs
  Migrations/
    (existing EF Core migrations)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/Entities/WaitlistEntry.cs | Entity for patient waitlist criteria, lifecycle state, and notification timestamps |
| CREATE | Server/Migrations/{timestamp}_AddWaitlistEntry.cs | Migration creating waitlist table, constraints, and indexes |
| MODIFY | Server/Data/AppDbContext.cs | Register `WaitlistEntry` and configure matching indexes and uniqueness rules |
| MODIFY | Server/Models/Entities/NotificationLog.cs | Add support for waitlist-offer notification typing if modeled in code |

## External References

- PostgreSQL 16 indexes documentation: https://www.postgresql.org/docs/16/indexes.html
- EF Core 8 indexes configuration: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- EF Core 8 relationships: https://learn.microsoft.com/en-us/ef/core/modeling/relationships
- EF Core 8 migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Add `WaitlistEntry` entity fields for patient criteria, lifecycle state, and notification timestamps
- [ ] Configure lifecycle statuses so active entries persist until explicitly removed, even if unrelated appointments are cancelled
- [ ] Create EF Core migration for the waitlist table with foreign keys and UTC timestamp columns
- [ ] Add indexes for active-entry matching by date, provider, appointment type, and notification timing
- [ ] Prevent duplicate active waitlist registrations for the same patient and criteria while preserving history
- [ ] Extend notification log typing to distinguish waitlist-offer deliveries for audit and retention