# Task - task_003_db_appointment_slot_indexes

## Requirement Reference

- User Story: US_017
- Story Location: .propel/context/tasks/EP-002/us_017/us_017.md
- Acceptance Criteria:
    - AC-1: Given I am on the booking page, When I select a date range and optionally a provider, Then the system displays all available slots matching my criteria within 2 seconds (database query performance is foundational to this target).
    - AC-4: Given slots are cached, When I request slots for a date within the cache TTL (5 minutes), Then the response returns in sub-second time from Redis cache (initial cache population query must be fast).
- Edge Case:
    - EC-2: Booking windows beyond 90 days -> Query scope bounded to 90 days from today per FR-013.

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

Add database indexes, provider availability seed data, and query optimizations to support the appointment slot availability API (task_002). This database task creates composite indexes on the Appointment table for efficient date range and provider filtering queries, establishes a provider availability template structure for defining bookable time slots per provider and day of week, and seeds initial provider data. These optimizations ensure the slot availability query meets the 2-second response time target at P95 (NFR-001) even on cache-miss scenarios, supporting AC-1 and AC-4 performance requirements.

## Dependent Tasks

- US_008 - Foundational - Requires Appointment entity and initial table migration

## Impacted Components

- **NEW** EF Core migration for Appointment table indexes (Server/Migrations/)
- **NEW** `ProviderAvailabilityTemplate` entity for provider schedule templates (Server/Models/Entities/)
- **NEW** EF Core migration for ProviderAvailabilityTemplate table (Server/Migrations/)
- **MODIFY** `AppDbContext` - Add DbSet for ProviderAvailabilityTemplate and index configurations (Server/Data/)

## Implementation Plan

1. **Create EF Core migration for composite index on Appointment table**. Add a composite index on `(appointment_time, status)` to optimize date range queries filtered by status (e.g., excluding cancelled). Add a separate index on `(appointment_time, status, provider_id)` for provider-specific queries. Use `HasIndex` in EF Core `OnModelCreating` configuration. These indexes directly support the `WHERE appointment_time BETWEEN @from AND @to AND status != 'cancelled'` query pattern.
2. **Create `ProviderAvailabilityTemplate` entity** defining the bookable time slot structure per provider and day of week. Attributes: `TemplateId` (UUID, PK), `ProviderId` (UUID, FK to User where role = staff), `DayOfWeek` (int, 0-6), `StartTime` (TimeOnly), `EndTime` (TimeOnly), `SlotDurationMinutes` (int, default 30), `AppointmentType` (string), `IsActive` (bool), `CreatedAt`, `UpdatedAt`. This entity defines _when_ a provider is available for bookings, which the slot service uses to compute open slots.
3. **Create EF Core migration for `ProviderAvailabilityTemplate` table** with appropriate constraints: unique constraint on `(ProviderId, DayOfWeek, StartTime)` to prevent overlapping templates, foreign key to User table, check constraint on `SlotDurationMinutes > 0`, check constraint on `EndTime > StartTime`.
4. **Add seed data for provider availability templates** using EF Core `HasData` or a migration seed. Create sample provider schedules (e.g., Dr. Smith: Mon-Fri 9:00-17:00, 30-min slots, General Checkup; Dr. Jones: Mon-Wed-Fri 10:00-16:00, 30-min slots, Specialist Consultation) to enable development and testing of the slot availability query.
5. **Validate query execution plans** by documenting the expected `EXPLAIN ANALYZE` output for the primary slot query. Confirm index scans (not sequential scans) are used for date range + status + provider filtering. Document baseline query timing for future performance regression monitoring.

## Current Project State

```
[Placeholder - to be updated based on dependent task completion]
Server/
  Data/
    AppDbContext.cs (modify - add DbSet and index config)
  Models/
    Entities/
      Appointment.cs (exists from US_008)
      (new entity file to be created here)
  Migrations/
    (new migration files to be created here)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/Entities/ProviderAvailabilityTemplate.cs | Entity for provider schedule templates with day/time/slot config |
| CREATE | Server/Migrations/{timestamp}_AddAppointmentSlotIndexes.cs | EF Core migration adding composite indexes on Appointment table |
| CREATE | Server/Migrations/{timestamp}_AddProviderAvailabilityTemplate.cs | EF Core migration creating ProviderAvailabilityTemplate table with constraints and seed data |
| MODIFY | Server/Data/AppDbContext.cs | Add DbSet for ProviderAvailabilityTemplate, configure indexes on Appointment in OnModelCreating |

## External References

- PostgreSQL 16 indexes documentation: https://www.postgresql.org/docs/16/indexes.html
- EF Core 8 indexes configuration: https://learn.microsoft.com/en-us/ef/core/modeling/indexes
- EF Core 8 data seeding: https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding
- PostgreSQL EXPLAIN documentation: https://www.postgresql.org/docs/16/sql-explain.html

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Add composite index on Appointment table `(appointment_time, status)` and `(appointment_time, status, provider_id)` via EF Core OnModelCreating configuration
- [ ] Create `ProviderAvailabilityTemplate` entity with ProviderId, DayOfWeek, StartTime, EndTime, SlotDurationMinutes (default 30), AppointmentType, IsActive fields
- [ ] Create EF Core migration for `ProviderAvailabilityTemplate` table with unique constraint `(ProviderId, DayOfWeek, StartTime)`, FK to User, and check constraints
- [ ] Add seed data for sample provider availability schedules (minimum 2 providers with Mon-Fri coverage) to support development and testing
- [ ] Validate query execution plan confirms index scan usage for date range + status + provider slot filtering queries (document EXPLAIN ANALYZE output)
