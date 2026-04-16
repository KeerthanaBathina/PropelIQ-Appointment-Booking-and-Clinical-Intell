# Task - task_002_be_efcore_configuration_migrations

## Requirement Reference

- User Story: us_008
- Story Location: .propel/context/tasks/EP-DATA/us_008/us_008.md
- Acceptance Criteria:
  - AC-1: Given entity models are defined, When a developer runs `dotnet ef migrations add CreateDomainEntities`, Then the migration generates DDL for Patient, Appointment, IntakeData, ClinicalDocument, ExtractedData, MedicalCode, User, AuditLog, QueueEntry, and NotificationLog.
  - AC-2: Given the Appointment entity has a version field, When two concurrent updates target the same appointment, Then EF Core throws DbUpdateConcurrencyException for the second update (optimistic locking).
  - AC-3: Given the Patient entity has a deleted_at column, When a patient is soft-deleted, Then global query filters exclude the record from standard queries while preserving it in the database.
  - AC-5: Given enum mappings are configured, When an appointment status is set to "scheduled", Then the database stores the enum as a string value in the status column.
- Edge Case:
  - What happens when a migration conflicts with existing data? Migrations include data migration scripts with rollback capability.
  - How does the system handle JSONB column with invalid JSON? EF Core validation rejects the entity before persistence; error is logged.

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
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Backend | Entity Framework Core | 8.x |
| Backend | Npgsql.EntityFrameworkCore.PostgreSQL | 8.x |
| Database | PostgreSQL | 16.x |
| CLI | dotnet-ef (EF Core Tools) | 8.x |

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

Create EF Core Fluent API configurations (`IEntityTypeConfiguration<T>`) for all 10 domain entities. Configure the `Appointment.Version` property as an `IsConcurrencyToken()` for optimistic locking (AC-2). Apply a `HasQueryFilter(p => p.DeletedAt == null)` global query filter on `Patient` for soft delete (AC-3). Map all JSONB properties using `OwnsOne(...).ToJson()` for PostgreSQL JSONB column storage (AC-4). Configure all enum properties with `HasConversion<string>()` to store enums as string values in PostgreSQL (AC-5). Define unique indexes, foreign key constraints, and table naming conventions. Register all entity `DbSet<T>` properties on `ApplicationDbContext` and generate the initial code-first migration (AC-1).

## Dependent Tasks

- task_001_be_domain_entity_models — All entity classes, enum types, and owned types must be defined before configurations can be created.

## Impacted Components

- **NEW** `src/UPACIP.DataAccess/Configurations/PatientConfiguration.cs` — IEntityTypeConfiguration for Patient with unique email index, soft delete query filter
- **NEW** `src/UPACIP.DataAccess/Configurations/AppointmentConfiguration.cs` — IEntityTypeConfiguration for Appointment with concurrency token, JSONB, enum-to-string
- **NEW** `src/UPACIP.DataAccess/Configurations/IntakeDataConfiguration.cs` — IEntityTypeConfiguration for IntakeData with three JSONB owned types, enum-to-string
- **NEW** `src/UPACIP.DataAccess/Configurations/ClinicalDocumentConfiguration.cs` — IEntityTypeConfiguration for ClinicalDocument with FK constraints, enum-to-string
- **NEW** `src/UPACIP.DataAccess/Configurations/ExtractedDataConfiguration.cs` — IEntityTypeConfiguration for ExtractedData with JSONB, FK constraints
- **NEW** `src/UPACIP.DataAccess/Configurations/MedicalCodeConfiguration.cs` — IEntityTypeConfiguration for MedicalCode with enum-to-string, composite index
- **NEW** `src/UPACIP.DataAccess/Configurations/AuditLogConfiguration.cs` — IEntityTypeConfiguration for AuditLog with indexes on UserId and Timestamp
- **NEW** `src/UPACIP.DataAccess/Configurations/QueueEntryConfiguration.cs` — IEntityTypeConfiguration for QueueEntry with unique AppointmentId, enum-to-string
- **NEW** `src/UPACIP.DataAccess/Configurations/NotificationLogConfiguration.cs` — IEntityTypeConfiguration for NotificationLog with enum-to-string, FK constraints
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<T> for all 10 entities, apply configurations in OnModelCreating
- **NEW** `src/UPACIP.DataAccess/Migrations/XXXXXXXX_CreateDomainEntities.cs` — Auto-generated EF Core migration

## Implementation Plan

1. **Create `PatientConfiguration`**: Implement `IEntityTypeConfiguration<Patient>`. Map to table `patients`. Configure `Email` as unique index (`HasIndex(p => p.Email).IsUnique()`). Apply global query filter for soft delete: `builder.HasQueryFilter(p => p.DeletedAt == null)`. Configure `Id` as `ValueGeneratedOnAdd()`. Set `DeletedAt` as optional column.

2. **Create `AppointmentConfiguration`**: Implement `IEntityTypeConfiguration<Appointment>`. Map to table `appointments`. Configure `Version` as concurrency token: `builder.Property(a => a.Version).IsConcurrencyToken()`. Map `PreferredSlotCriteria` as JSONB: `builder.OwnsOne(a => a.PreferredSlotCriteria, b => b.ToJson())`. Configure `Status` enum as string: `builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20)`. Add FK to `Patient` with cascade delete. Add index on `PatientId` and `AppointmentTime`.

3. **Create `IntakeDataConfiguration`**: Implement `IEntityTypeConfiguration<IntakeData>`. Map to table `intake_data`. Configure three JSONB owned types: `builder.OwnsOne(i => i.MandatoryFields, b => b.ToJson())`, `builder.OwnsOne(i => i.OptionalFields, b => b.ToJson())`, `builder.OwnsOne(i => i.InsuranceInfo, b => b.ToJson())`. Configure `IntakeMethod` enum as string. Add FK to `Patient`. Add index on `PatientId`.

4. **Create `ClinicalDocumentConfiguration` and `ExtractedDataConfiguration`**: `ClinicalDocument` → table `clinical_documents`. Configure `DocumentCategory` and `ProcessingStatus` enums as string. Add FKs to `Patient` and `ApplicationUser` (uploader). Index on `PatientId` and `ProcessingStatus`. `ExtractedData` → table `extracted_data`. Configure `DataType` enum as string. Map `DataContent` as JSONB via `OwnsOne(...).ToJson()`. Add FK to `ClinicalDocument` (cascade delete) and nullable FK to `ApplicationUser` (verified_by). Index on `DocumentId`.

5. **Create `MedicalCodeConfiguration`, `AuditLogConfiguration`, `QueueEntryConfiguration`, `NotificationLogConfiguration`**: `MedicalCode` → table `medical_codes`. Configure `CodeType` enum as string. Composite index on `PatientId` + `CodeType` + `CodeValue`. FK to `Patient` and nullable FK to `ApplicationUser`. `AuditLog` → table `audit_logs`. Key on `LogId`. Configure `Action` enum as string. Indexes on `UserId` and `Timestamp` for query performance. FK to `ApplicationUser`. `QueueEntry` → table `queue_entries`. Configure `Priority` and `Status` enums as string. Unique index on `AppointmentId`. FK to `Appointment`. `NotificationLog` → table `notification_logs`. Key on `NotificationId`. Configure `NotificationType`, `DeliveryChannel`, `Status` enums as string. FK to `Appointment`. Index on `AppointmentId`.

6. **Register DbSets and apply configurations in `ApplicationDbContext`**: Add `DbSet<Patient> Patients`, `DbSet<Appointment> Appointments`, `DbSet<IntakeData> IntakeRecords`, `DbSet<ClinicalDocument> ClinicalDocuments`, `DbSet<ExtractedData> ExtractedData`, `DbSet<MedicalCode> MedicalCodes`, `DbSet<AuditLog> AuditLogs`, `DbSet<QueueEntry> QueueEntries`, `DbSet<NotificationLog> NotificationLogs`. In `OnModelCreating`, call `modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())` to auto-discover all `IEntityTypeConfiguration<T>` implementations.

7. **Configure snake_case table/column naming**: Use `modelBuilder.UseSnakeCaseNamingConvention()` (from `EFCore.NamingConventions` package) or explicitly set `.ToTable("table_name")` and `.HasColumnName("column_name")` in each configuration. This ensures PostgreSQL-idiomatic `snake_case` column names matching the ERD.

8. **Generate initial migration and validate DDL**: Run `dotnet ef migrations add CreateDomainEntities --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api`. Inspect the generated migration to verify it creates all 10 tables with correct columns, data types, constraints, indexes, and FK relationships. Run `dotnet ef migrations script` to generate SQL and confirm DDL correctness. Ensure migration includes `Down()` method for rollback capability.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Identity/
│       │   ├── ApplicationUser.cs
│       │   └── ApplicationRole.cs
│       ├── Entities/
│       │   ├── BaseEntity.cs
│       │   ├── Patient.cs
│       │   ├── Appointment.cs
│       │   ├── IntakeData.cs
│       │   ├── ClinicalDocument.cs
│       │   ├── ExtractedData.cs
│       │   ├── MedicalCode.cs
│       │   ├── AuditLog.cs
│       │   ├── QueueEntry.cs
│       │   ├── NotificationLog.cs
│       │   └── OwnedTypes/
│       │       ├── PreferredSlotCriteria.cs
│       │       ├── IntakeMandatoryFields.cs
│       │       ├── IntakeOptionalFields.cs
│       │       ├── InsuranceInfo.cs
│       │       └── ExtractedDataContent.cs
│       └── Enums/
│           ├── AppointmentStatus.cs
│           └── ... (12 enum files)
├── app/
└── scripts/
```

> Assumes task_001_be_domain_entity_models is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.DataAccess/Configurations/PatientConfiguration.cs | Unique email index, soft delete global query filter, FK navigation |
| CREATE | src/UPACIP.DataAccess/Configurations/AppointmentConfiguration.cs | Concurrency token on Version, JSONB PreferredSlotCriteria via ToJson(), enum-to-string Status |
| CREATE | src/UPACIP.DataAccess/Configurations/IntakeDataConfiguration.cs | Three JSONB owned types via ToJson(), enum-to-string IntakeMethod |
| CREATE | src/UPACIP.DataAccess/Configurations/ClinicalDocumentConfiguration.cs | FK constraints, enum-to-string DocumentCategory/ProcessingStatus |
| CREATE | src/UPACIP.DataAccess/Configurations/ExtractedDataConfiguration.cs | JSONB DataContent via ToJson(), FK to ClinicalDocument |
| CREATE | src/UPACIP.DataAccess/Configurations/MedicalCodeConfiguration.cs | Composite index, enum-to-string CodeType |
| CREATE | src/UPACIP.DataAccess/Configurations/AuditLogConfiguration.cs | Indexes on UserId/Timestamp, enum-to-string Action |
| CREATE | src/UPACIP.DataAccess/Configurations/QueueEntryConfiguration.cs | Unique AppointmentId, enum-to-string Priority/Status |
| CREATE | src/UPACIP.DataAccess/Configurations/NotificationLogConfiguration.cs | FK to Appointment, enum-to-string enums |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add 9 DbSet<T> properties, call ApplyConfigurationsFromAssembly in OnModelCreating |
| CREATE | src/UPACIP.DataAccess/Migrations/*_CreateDomainEntities.cs | Auto-generated EF Core migration with DDL for all 10 tables |

## External References

- [EF Core Fluent API Configuration](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types#configuring-entity-types)
- [EF Core IEntityTypeConfiguration](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types#fluent-api)
- [EF Core Concurrency Tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [EF Core Global Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [EF Core JSON Columns (ToJson)](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-7.0/whatsnew#json-columns)
- [Npgsql JSONB Support](https://www.npgsql.org/efcore/mapping/json.html)
- [EF Core Value Conversions (Enum to String)](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [EF Core Migrations Overview](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EFCore.NamingConventions (snake_case)](https://github.com/efcore/EFCore.NamingConventions)

## Build Commands

```powershell
# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Build full solution
dotnet build UPACIP.sln

# Generate migration
dotnet ef migrations add CreateDomainEntities --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Generate SQL script to inspect DDL
dotnet ef migrations script --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api --output migrations.sql

# Apply migration to database
dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Rollback migration (if needed)
dotnet ef database update 0 --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors after adding all configurations
- [ ] `dotnet ef migrations add CreateDomainEntities` generates a migration without errors
- [ ] Generated migration DDL creates all 10 tables: patients, appointments, intake_data, clinical_documents, extracted_data, medical_codes, audit_logs, queue_entries, notification_logs (+ existing identity tables for users)
- [ ] `Appointment.Version` is configured as concurrency token — concurrent updates throw `DbUpdateConcurrencyException`
- [ ] `Patient` global query filter excludes records where `DeletedAt` is not null from default queries
- [ ] `IgnoreQueryFilters()` returns soft-deleted Patient records
- [ ] JSONB columns (preferred_slot_criteria, mandatory_fields, optional_fields, insurance_info, data_content) store valid JSON in PostgreSQL
- [ ] All enum columns store string values (e.g., "Scheduled", "Completed") not integer ordinals

## Implementation Checklist

- [ ] Create `PatientConfiguration` implementing `IEntityTypeConfiguration<Patient>` with unique email index and `HasQueryFilter(p => p.DeletedAt == null)` soft delete global query filter
- [ ] Create `AppointmentConfiguration` with `IsConcurrencyToken()` on `Version`, `OwnsOne(a => a.PreferredSlotCriteria, b => b.ToJson())` for JSONB, and `HasConversion<string>()` on `Status` enum
- [ ] Create `IntakeDataConfiguration` with `OwnsOne(...).ToJson()` for `MandatoryFields`, `OptionalFields`, `InsuranceInfo` JSONB columns and `HasConversion<string>()` on `IntakeMethod`
- [ ] Create `ClinicalDocumentConfiguration` and `ExtractedDataConfiguration` with FK constraints, enum-to-string conversions, and `OwnsOne(e => e.DataContent, b => b.ToJson())` for JSONB
- [ ] Create `MedicalCodeConfiguration`, `AuditLogConfiguration`, `QueueEntryConfiguration`, `NotificationLogConfiguration` with enum-to-string conversions, indexes, and FK constraints
- [ ] Register all `DbSet<T>` properties on `ApplicationDbContext` and call `ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())` in `OnModelCreating`
- [ ] Generate migration via `dotnet ef migrations add CreateDomainEntities` and verify DDL creates all 10 tables with correct columns, constraints, and indexes
- [ ] Validate migration `Down()` method provides rollback capability and generated SQL script is reviewable via `dotnet ef migrations script`
