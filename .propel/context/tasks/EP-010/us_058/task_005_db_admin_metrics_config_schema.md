# Task - TASK_005

## Requirement Reference

- User Story: US_058
- Story Location: .propel/context/tasks/EP-010/us_058/us_058.md
- Acceptance Criteria:
    - AC-1: **Given** the admin logs in, **When** the admin dashboard loads, **Then** it displays system metrics (active users, daily appointments, no-show rate, AI agreement rate, uptime percentage).
    - AC-2: **Given** the metrics section is displayed, **When** the admin views trends, **Then** the system shows rolling 7-day and 30-day trend charts for key metrics.
    - AC-3: **Given** the admin dashboard is loaded, **When** the admin navigates to user management, **Then** they see a list of all staff and admin accounts with status, last login, and role.
    - AC-4: **Given** the admin clicks configuration, **When** the configuration panel opens, **Then** it provides tabs for appointment templates, business hours, notification templates, and risk thresholds.
- Edge Case:
    - What happens when system metrics data is temporarily unavailable? Dashboard shows cached values with a "Data as of [timestamp]" indicator.

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

Create the database schema and EF Core migrations supporting the admin dashboard system metrics and configuration management features. This task adds tables for daily metrics snapshots (enabling trend chart queries), appointment slot templates (per provider/day-of-week), notification templates, and a key-value system configuration store for business hours, holidays, and risk thresholds. Includes proper indexing for date-range metrics queries, foreign key constraints to existing User entity (DR-009), and migration scripts with rollback support (DR-028, DR-029).

## Dependent Tasks

- US_001 — Foundational — Requires backend API scaffold (.NET 8 project, EF Core setup)
- US_008 — Foundational — Requires existing domain entities (User, Appointment) and initial migration baseline

## Impacted Components

- **NEW** `Server/Models/Entities/SystemMetricsSnapshot.cs` — Entity for daily aggregated metrics
- **NEW** `Server/Models/Entities/SlotTemplate.cs` — Entity for appointment slot template definitions
- **NEW** `Server/Models/Entities/NotificationTemplate.cs` — Entity for email/SMS notification templates
- **NEW** `Server/Models/Entities/SystemConfiguration.cs` — Entity for key-value configuration storage
- **NEW** `Server/Data/Migrations/YYYYMMDD_AddAdminDashboardSchema.cs` — EF Core migration
- **MODIFY** `Server/Data/ApplicationDbContext.cs` — Add DbSet properties for new entities and configure relationships

## Implementation Plan

1. **Create SystemMetricsSnapshot entity** — Define entity with columns: `Id` (UUID, PK), `MetricDate` (DateOnly, indexed), `ActiveUsers` (int), `DailyAppointments` (int), `NoShowRate` (decimal(5,2)), `AiAgreementRate` (decimal(5,2)), `UptimePercentage` (decimal(5,2)), `CreatedAt` (timestamp). Add unique constraint on `MetricDate` to ensure one snapshot per day. Add index on `MetricDate` for efficient range queries (AC-2 trend charts: 7-day and 30-day lookups). This table is populated by a scheduled background job (out of scope for this task) or on-demand by the metrics service.

2. **Create SlotTemplate entity** — Define entity with columns: `Id` (UUID, PK), `ProviderId` (UUID, FK to User), `DayOfWeek` (int 0-6), `StartTime` (TimeOnly), `EndTime` (TimeOnly), `SlotDurationMinutes` (int), `BufferTimeMinutes` (int), `IsAvailable` (bool, default true), `CreatedAt` (timestamp), `UpdatedAt` (timestamp). Add composite unique constraint on (`ProviderId`, `DayOfWeek`, `StartTime`) to prevent duplicate slot definitions. Add FK constraint to `Users` table on `ProviderId` (DR-009). Add index on `ProviderId` for provider-specific queries.

3. **Create NotificationTemplate entity** — Define entity with columns: `Id` (UUID, PK), `TemplateName` (string, max 200), `Channel` (enum: Email, SMS, InApp), `TriggerEvent` (string, max 100), `MessageBody` (text, stores template with {{variable}} placeholders), `IsActive` (bool, default true), `CreatedAt` (timestamp), `UpdatedAt` (timestamp). Add unique constraint on `TemplateName` to prevent duplicate names.

4. **Create SystemConfiguration entity** — Define entity as key-value store with columns: `Id` (UUID, PK), `Category` (string, max 50 — e.g., "business_hours", "holidays", "risk_thresholds"), `ConfigKey` (string, max 100), `ConfigValue` (JSONB — stores structured values), `UpdatedAt` (timestamp), `UpdatedByUserId` (UUID, FK to User). Add composite unique constraint on (`Category`, `ConfigKey`). Add FK constraint to `Users` table on `UpdatedByUserId`. This entity stores business hours per day, holiday definitions, no-show risk thresholds, and scoring weights as JSON values.

5. **Configure entity relationships in DbContext** — Add `DbSet<SystemMetricsSnapshot>`, `DbSet<SlotTemplate>`, `DbSet<NotificationTemplate>`, and `DbSet<SystemConfiguration>` to `ApplicationDbContext`. Configure Fluent API mappings: table names, index definitions, FK relationships with `OnDelete(DeleteBehavior.Restrict)` to prevent cascading deletes on provider removal.

6. **Generate EF Core migration** — Run `dotnet ef migrations add AddAdminDashboardSchema` to generate migration file. Verify the migration includes all table creations, constraints, and indexes. Add rollback support ensuring `Down()` method drops tables in correct dependency order (DR-029).

7. **Seed initial configuration data** — Add seed data in migration for default business hours (Mon-Fri 8AM-5PM, Sat 9AM-1PM, Sun Closed per wireframe) and default notification templates (Appointment Reminder 24h, Appointment Confirmation, Intake Completion, No-Show Follow-up per wireframe). Set default risk threshold to 70.

## Current Project State

- Project is in planning phase. No `Server/` folder exists yet.
- Backend scaffold and initial EF Core setup will be established by US_001 and US_008 (dependencies).
- Placeholder to be updated during task execution based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Models/Entities/SystemMetricsSnapshot.cs` | Entity for daily aggregated system metrics |
| CREATE | `Server/Models/Entities/SlotTemplate.cs` | Entity for appointment slot template definitions per provider/day |
| CREATE | `Server/Models/Entities/NotificationTemplate.cs` | Entity for email/SMS/in-app notification templates |
| CREATE | `Server/Models/Entities/SystemConfiguration.cs` | Key-value entity for business hours, holidays, risk thresholds |
| CREATE | `Server/Data/Migrations/YYYYMMDD_AddAdminDashboardSchema.cs` | EF Core migration with rollback support |
| MODIFY | `Server/Data/ApplicationDbContext.cs` | Add DbSet properties and Fluent API configurations for new entities |

## External References

- [Entity Framework Core 8 Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [EF Core Fluent API Configuration](https://learn.microsoft.com/en-us/ef/core/modeling/)
- [PostgreSQL JSONB Documentation](https://www.postgresql.org/docs/16/datatype-json.html)
- [EF Core Data Seeding](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding)
- [PostgreSQL Index Types](https://www.postgresql.org/docs/16/indexes.html)

## Build Commands

- Refer to applicable technology stack specific build commands in `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Migration applies successfully with `dotnet ef database update`
- [ ] Migration rolls back cleanly with `dotnet ef database update <previous_migration>`
- [ ] `system_metrics_snapshot` table created with unique constraint on `MetricDate`
- [ ] `slot_templates` table created with composite unique and FK to Users
- [ ] `notification_templates` table created with unique constraint on `TemplateName`
- [ ] `system_configuration` table created with composite unique and FK to Users
- [ ] Seed data populates default business hours, notification templates, and risk threshold
- [ ] All FK constraints enforced (ProviderId, UpdatedByUserId reference Users table)

## Implementation Checklist

- [ ] Create `SystemMetricsSnapshot` entity with date index and unique constraint
- [ ] Create `SlotTemplate` entity with provider FK, composite unique, and provider index
- [ ] Create `NotificationTemplate` entity with template name unique constraint
- [ ] Create `SystemConfiguration` entity with category/key composite unique and JSONB value column
- [ ] Configure DbContext with DbSet properties, Fluent API mappings, and FK relationships
- [ ] Generate EF Core migration with verified `Up()` and `Down()` methods
- [ ] Seed default business hours, notification templates, and risk threshold values
