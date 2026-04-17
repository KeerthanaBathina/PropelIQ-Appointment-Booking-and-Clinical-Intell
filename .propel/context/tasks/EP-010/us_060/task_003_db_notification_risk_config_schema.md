# Task - TASK_003

## Requirement Reference

- User Story: us_060
- Story Location: .propel/context/tasks/EP-010/us_060/us_060.md
- Acceptance Criteria:
    - AC-1: Given the admin opens notification template configuration, When they select a template (booking confirmation, 24h reminder, 2h reminder), Then they can edit subject line, body text, and variable placeholders (patient name, date, time, provider).
    - AC-2: Given the admin edits a notification template, When they save changes, Then all future notifications use the updated template without affecting already-sent messages.
    - AC-3: Given the admin opens risk configuration, When they adjust the no-show risk threshold, Then the system recalculates risk scoring display using the new threshold values.
    - AC-4: Given the admin modifies scoring parameters, When they save changes, Then the system logs the parameter change with admin attribution and timestamp.
- Edge Cases:
    - Invalid variable placeholders: Database stores only validated templates; constraint enforced at application layer before persistence.
    - Risk threshold changes on active appointments: `recalculation_pending` flag column tracks deferred batch recalculation state.

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
| Frontend | N/A | - |
| Backend | N/A (consumed by EF Core) | - |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| AI/ML | N/A | - |
| Vector Store | N/A | - |
| AI Gateway | N/A | - |
| Mobile | N/A | - |

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

Create the PostgreSQL database schema and EF Core migrations for the `NotificationTemplate` and `RiskConfiguration` entities. The `NotificationTemplate` table stores email/SMS notification templates with template type, channel, subject, body text, allowed variable definitions, and versioning metadata. The `RiskConfiguration` table stores the singleton no-show risk threshold and scoring parameter weights as JSONB, with a `recalculation_pending` boolean flag for deferred batch processing. Both tables include audit columns (created_by, updated_by, timestamps) and optimistic concurrency tokens. Seed data populates the 3 default notification templates (booking confirmation, 24h reminder, 2h reminder) and initial risk configuration defaults.

## Dependent Tasks

- US_008 (EP-DATA) — Core domain entity models and migrations must exist (User, AuditLog, NotificationLog entities referenced via foreign keys)
- US_003 (EP-TECH) — PostgreSQL database provisioned and accessible

## Impacted Components

- **NEW** `Server/Models/NotificationTemplate.cs` — EF Core entity for notification templates
- **NEW** `Server/Models/RiskConfiguration.cs` — EF Core entity for risk configuration (singleton)
- **NEW** `Server/Data/Configurations/NotificationTemplateConfiguration.cs` — EF Core Fluent API configuration (indexes, constraints, seed data)
- **NEW** `Server/Data/Configurations/RiskConfigurationConfiguration.cs` — EF Core Fluent API configuration (constraints, seed data)
- **NEW** `Server/Data/Migrations/XXXXXX_AddNotificationTemplateAndRiskConfig.cs` — EF Core migration
- **MODIFY** `Server/Data/AppDbContext.cs` — Add DbSet properties for NotificationTemplate and RiskConfiguration

## Implementation Plan

1. **Define NotificationTemplate entity**:
   ```
   - Id (int, PK, identity)
   - TemplateType (string, enum stored as string: "BookingConfirmation", "Reminder24h", "Reminder2h")
   - Channel (string, enum stored as string: "Email", "SMS")
   - Subject (string, max 200, required for Email; null for SMS)
   - BodyText (string, max 5000, required)
   - AllowedVariables (string[], stored as JSONB — ["patient_name", "date", "time", "provider"])
   - IsActive (bool, default true)
   - Version (int, concurrency token, auto-increment on update)
   - CreatedAt (DateTimeOffset, UTC, default now)
   - CreatedBy (string, FK to User.Id)
   - UpdatedAt (DateTimeOffset, UTC, nullable)
   - UpdatedBy (string, FK to User.Id, nullable)
   - RowVersion (byte[], concurrency token for EF Core optimistic concurrency)
   ```

2. **Define RiskConfiguration entity** (singleton pattern — single row):
   ```
   - Id (int, PK, identity — always 1)
   - NoShowThreshold (int, range 0–100, default 70)
   - ScoringParameters (JSONB — {"prior_no_shows_weight": 0.4, "cancellation_history_weight": 0.3, "appointment_lead_time_weight": 0.3})
   - RecalculationPending (bool, default false)
   - LastRecalculatedAt (DateTimeOffset, UTC, nullable)
   - UpdatedAt (DateTimeOffset, UTC, nullable)
   - UpdatedBy (string, FK to User.Id, nullable)
   - RowVersion (byte[], concurrency token)
   ```

3. **Create EF Core Fluent API configurations**:
   - `NotificationTemplateConfiguration`: Unique composite index on (TemplateType, Channel), check constraint on TemplateType values, check constraint on Channel values, JSONB column type for AllowedVariables, concurrency token on RowVersion
   - `RiskConfigurationConfiguration`: Check constraint on NoShowThreshold (0–100), JSONB column type for ScoringParameters, concurrency token on RowVersion

4. **Add seed data**:
   - 6 NotificationTemplate records (3 types × 2 channels):
     - BookingConfirmation / Email: Subject="Appointment Confirmed", Body with `{{patient_name}}`, `{{date}}`, `{{time}}`, `{{provider}}`
     - BookingConfirmation / SMS: Body with `{{patient_name}}`, `{{date}}`, `{{time}}`
     - Reminder24h / Email: Subject="Appointment Reminder — Tomorrow", Body with all 4 variables
     - Reminder24h / SMS: Body with `{{patient_name}}`, `{{date}}`, `{{time}}`
     - Reminder2h / Email: Subject="Appointment in 2 Hours", Body with all 4 variables
     - Reminder2h / SMS: Body with `{{patient_name}}`, `{{time}}`, `{{provider}}`
   - 1 RiskConfiguration record: threshold=70, default scoring weights (0.4, 0.3, 0.3), recalculation_pending=false

5. **Register DbSets** in `AppDbContext.cs`:
   - `DbSet<NotificationTemplate> NotificationTemplates`
   - `DbSet<RiskConfiguration> RiskConfigurations`

6. **Generate and apply migration**:
   - `dotnet ef migrations add AddNotificationTemplateAndRiskConfig`
   - `dotnet ef database update`
   - Verify migration includes rollback support (Down method drops tables and seed data)

**Focus on how to implement:**
- Use `[Column(TypeName = "jsonb")]` for JSONB columns in PostgreSQL via Npgsql EF Core provider
- Use `HasCheckConstraint` in Fluent API for value range validation at DB level
- Use `HasAlternateKey` or `HasIndex().IsUnique()` for the composite unique constraint
- Store enums as strings using `.HasConversion<string>()` per US_008 convention
- Apply soft delete pattern with `deleted_at` column and global query filter per US_008 convention
- Use `[Timestamp]` attribute or `.IsRowVersion()` for optimistic concurrency per US_008 convention
- Seed data via `HasData()` in Fluent API configuration

## Current Project State

```
[Placeholder — to be updated based on dependent task completion]
Server/
├── Models/
│   ├── NotificationTemplate.cs       (NEW)
│   └── RiskConfiguration.cs          (NEW)
├── Data/
│   ├── AppDbContext.cs               (MODIFY)
│   ├── Configurations/
│   │   ├── NotificationTemplateConfiguration.cs  (NEW)
│   │   └── RiskConfigurationConfiguration.cs     (NEW)
│   └── Migrations/
│       └── XXXXXX_AddNotificationTemplateAndRiskConfig.cs (NEW)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/NotificationTemplate.cs | Entity with TemplateType, Channel, Subject, BodyText, AllowedVariables (JSONB), audit columns, concurrency token |
| CREATE | Server/Models/RiskConfiguration.cs | Singleton entity with NoShowThreshold, ScoringParameters (JSONB), RecalculationPending flag, audit columns |
| CREATE | Server/Data/Configurations/NotificationTemplateConfiguration.cs | Fluent API: unique index (TemplateType, Channel), check constraints, JSONB mapping, seed data for 6 templates |
| CREATE | Server/Data/Configurations/RiskConfigurationConfiguration.cs | Fluent API: check constraint (threshold 0–100), JSONB mapping, seed data for default risk config |
| CREATE | Server/Data/Migrations/XXXXXX_AddNotificationTemplateAndRiskConfig.cs | EF Core migration with Up/Down methods for schema creation and rollback |
| MODIFY | Server/Data/AppDbContext.cs | Add DbSet<NotificationTemplate> and DbSet<RiskConfiguration> properties |

## External References

- [EF Core 8 — Entity Configuration](https://learn.microsoft.com/en-us/ef/core/modeling/)
- [EF Core 8 — Data Seeding](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding)
- [EF Core 8 — Concurrency Tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency?tabs=data-annotations)
- [Npgsql EF Core — JSON Columns](https://www.npgsql.org/efcore/mapping/json.html)
- [Npgsql EF Core — PostgreSQL Extensions](https://www.npgsql.org/efcore/mapping/general.html)
- [EF Core 8 — Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)

## Build Commands

- `cd Server && dotnet restore` — Restore NuGet packages
- `cd Server && dotnet build` — Build the project
- `cd Server && dotnet ef migrations add AddNotificationTemplateAndRiskConfig` — Generate migration
- `cd Server && dotnet ef database update` — Apply migration

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies successfully (`dotnet ef database update`)
- [ ] Migration rollback succeeds (`dotnet ef database update <previous_migration>`)
- [ ] `NotificationTemplates` table created with correct columns and types
- [ ] `RiskConfigurations` table created with correct columns and types
- [ ] Unique composite index on (TemplateType, Channel) prevents duplicate templates
- [ ] Check constraint rejects NoShowThreshold values outside 0–100 range
- [ ] JSONB columns (AllowedVariables, ScoringParameters) store and retrieve correctly
- [ ] Seed data populates 6 notification templates (3 types × 2 channels)
- [ ] Seed data populates 1 risk configuration with default values (threshold=70, weights sum=1.0)
- [ ] Optimistic concurrency token (RowVersion) prevents conflicting updates
- [ ] Soft delete global query filter applied to NotificationTemplate
- [ ] Foreign keys to User table for CreatedBy/UpdatedBy columns

## Implementation Checklist

- [ ] Create `NotificationTemplate.cs` entity with all properties, JSONB AllowedVariables, audit columns, RowVersion
- [ ] Create `RiskConfiguration.cs` entity with threshold, JSONB ScoringParameters, RecalculationPending flag, RowVersion
- [ ] Create `NotificationTemplateConfiguration.cs` — Fluent API with unique index, check constraints, JSONB mapping, seed data (6 records)
- [ ] Create `RiskConfigurationConfiguration.cs` — Fluent API with check constraint (0–100), JSONB mapping, seed data (1 record)
- [ ] Add `DbSet<NotificationTemplate>` and `DbSet<RiskConfiguration>` to AppDbContext.cs
- [ ] Generate EF Core migration and verify Up/Down methods are correct
- [ ] Apply migration and validate schema in PostgreSQL
