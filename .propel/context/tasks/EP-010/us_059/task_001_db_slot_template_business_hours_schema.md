# Task - TASK_001

## Requirement Reference

- User Story: US_059
- Story Location: .propel/context/tasks/EP-010/us_059/us_059.md
- Acceptance Criteria:
  - AC-1: Given the admin opens slot template configuration, When they select a provider and day, Then the system displays the current slot template with configurable time blocks and appointment types.
  - AC-2: Given the admin modifies a slot template, When they save changes, Then the template applies to all future appointments for that provider/day combination without affecting existing bookings.
  - AC-3: Given the admin opens business hours configuration, When they set hours for each day of the week, Then the system enforces these hours in the appointment booking interface.
  - AC-4: Given the admin adds a holiday, When the holiday date is saved, Then no appointment slots are available for that date and existing bookings for the date are flagged for staff review.
- Edge Case:
  - What happens when a template change conflicts with existing appointments? System shows a list of affected appointments and requires admin confirmation before applying.
  - How does the system handle recurring holidays (e.g., Christmas)? Admin can set annual recurring holidays that automatically block slots each year.

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

Create the database schema and EF Core entity models for appointment slot templates, business hours, and holiday schedules. This task establishes the persistence layer required for admin configuration of provider-specific weekly slot templates, per-day business hours, and holiday management with support for recurring annual holidays. The schema supports optimistic locking on templates and soft-delete patterns for holiday deactivation.

## Dependent Tasks

- US_058 — Requires admin dashboard framework (EP-010 prerequisite)
- US_008 — Requires Appointment entity and Provider/User entity definitions (EP-DATA foundational)

## Impacted Components

- **NEW** — `SlotTemplate` entity model (EF Core)
- **NEW** — `SlotTemplateBlock` entity model (EF Core)
- **NEW** — `BusinessHours` entity model (EF Core)
- **NEW** — `Holiday` entity model (EF Core)
- **MODIFY** — `ApplicationDbContext` — Register new DbSets and configure relationships
- **MODIFY** — `Appointment` entity — Add nullable FK to `SlotTemplate` for traceability

## Implementation Plan

1. Define `SlotTemplate` entity with composite uniqueness on (provider_id, day_of_week) and version field for optimistic locking
2. Define `SlotTemplateBlock` entity as child of `SlotTemplate` with time range, appointment type, and availability flag
3. Define `BusinessHours` entity with per-day-of-week open/close times and is_closed flag
4. Define `Holiday` entity with date, name, is_recurring flag, half_day flag, and soft-delete (deleted_at)
5. Configure EF Core relationships: SlotTemplate → SlotTemplateBlock (one-to-many cascade), SlotTemplate → User (many-to-one), Holiday → User (created_by audit)
6. Add unique indexes on (provider_id, day_of_week) for SlotTemplate and (date) for Holiday
7. Add index on Holiday.date for fast lookup during booking validation
8. Generate EF Core migration with rollback support

## Current Project State

- Placeholder — to be updated based on completion of dependent tasks (US_058, US_008).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/SlotTemplate.cs | SlotTemplate entity with provider_id, day_of_week, version, audit fields |
| CREATE | Server/Models/SlotTemplateBlock.cs | SlotTemplateBlock entity with slot_template_id FK, start_time, end_time, appointment_type, is_available |
| CREATE | Server/Models/BusinessHours.cs | BusinessHours entity with day_of_week, open_time, close_time, is_closed |
| CREATE | Server/Models/Holiday.cs | Holiday entity with date, name, is_recurring, is_half_day, deleted_at |
| MODIFY | Server/Data/ApplicationDbContext.cs | Add DbSets for SlotTemplate, SlotTemplateBlock, BusinessHours, Holiday; configure Fluent API relationships, indexes, and constraints |
| CREATE | Server/Migrations/YYYYMMDD_AddSlotTemplateBusinessHoursSchema.cs | EF Core migration with Up/Down methods |

## External References

- [EF Core 8 — Creating and Configuring a Model](https://learn.microsoft.com/en-us/ef/core/modeling/)
- [EF Core 8 — Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [EF Core 8 — Concurrency Tokens (Optimistic Locking)](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [PostgreSQL 16 — TimeOnly and DateOnly mapping](https://www.npgsql.org/efcore/mapping/nodatime.html)

## Build Commands

- `dotnet ef migrations add AddSlotTemplateBusinessHoursSchema --project Server`
- `dotnet ef database update --project Server`
- `dotnet build Server`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [x] Migration applies cleanly with `dotnet ef database update`
- [x] Migration rolls back cleanly with `dotnet ef migrations remove`
- [x] Unique constraint on (provider_id, day_of_week) verified
- [x] Optimistic locking version field on SlotTemplate confirmed
- [x] Foreign key cascades validated (SlotTemplate → SlotTemplateBlock)

## Implementation Checklist

- [x] Create `SlotTemplate` entity with fields: slot_template_id (UUID PK), provider_id (FK to User), day_of_week (enum: Monday–Sunday), version (int, concurrency token), created_at, updated_at
- [x] Create `SlotTemplateBlock` entity with fields: block_id (UUID PK), slot_template_id (FK), start_time (TimeOnly), end_time (TimeOnly), appointment_type (string), is_available (bool), created_at
- [x] Create `BusinessHours` entity with fields: business_hours_id (UUID PK), day_of_week (enum), open_time (TimeOnly), close_time (TimeOnly), is_closed (bool), updated_at, updated_by (FK to User)
- [x] Create `Holiday` entity with fields: holiday_id (UUID PK), date (DateOnly), name (string), is_recurring (bool), is_half_day (bool), created_by (FK to User), created_at, deleted_at (nullable, soft delete)
- [x] Configure ApplicationDbContext with Fluent API: unique index on SlotTemplate(provider_id, day_of_week), unique index on Holiday(date) with filter on deleted_at IS NULL, cascade delete SlotTemplate → SlotTemplateBlock
- [x] Add check constraint ensuring SlotTemplateBlock.start_time < end_time and BusinessHours.open_time < close_time
- [x] Generate and verify EF Core migration (`20260426000002_AddSlotTemplateBusinessHoursSchema.cs`) with rollback support (Up and Down methods)
- [x] Seed default business hours (Mon–Fri 08:00–17:00, Sat 09:00–13:00, Sun closed) via InsertData
- [x] Add nullable SlotTemplateId FK to Appointment entity (ON DELETE SET NULL) for traceability
- [x] Build validated: 0 errors, 0 warnings
