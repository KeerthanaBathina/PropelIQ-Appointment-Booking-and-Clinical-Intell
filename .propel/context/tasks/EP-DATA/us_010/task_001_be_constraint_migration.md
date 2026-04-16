# Task - task_001_be_constraint_migration

## Requirement Reference

- User Story: us_010
- Story Location: .propel/context/tasks/EP-DATA/us_010/us_010.md
- Acceptance Criteria:
  - AC-1: Given foreign keys are configured, When an appointment is created with a non-existent patient_id, Then the database rejects the insert with a foreign key violation error.
  - AC-2: Given email uniqueness is enforced, When a duplicate email is inserted for Patient or User, Then the database rejects with a unique constraint violation.
  - AC-3: Given (patient_id, appointment_time) unique constraint exists, When a duplicate booking is attempted, Then the database prevents the insert and the API returns 409 Conflict.
  - AC-4: Given cascading deletes are configured, When a patient record is deleted, Then dependent Appointment, IntakeData, and ClinicalDocument records are cascaded appropriately per entity relationship rules.
- Edge Case:
  - What happens when a foreign key referenced record is soft-deleted? Application-level check prevents references to soft-deleted records.
  - How does the system handle orphaned ExtractedData when a ClinicalDocument is deleted? Cascading delete removes ExtractedData records.

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

Add a composite unique constraint on `(patient_id, appointment_time)` in the Appointment table to prevent duplicate bookings (DR-014). Formalize and document the cascade delete behavior for every entity relationship: Patient → Appointment (cascade), Patient → IntakeData (cascade), Patient → ClinicalDocument (cascade), Patient → MedicalCode (cascade), ClinicalDocument → ExtractedData (cascade), Appointment → QueueEntry (cascade), Appointment → NotificationLog (cascade). Configure restrict/set-null behavior for audit-related FKs (User → AuditLog as restrict, User → ExtractedData.VerifiedByUserId as set-null, User → MedicalCode.ApprovedByUserId as set-null). Verify the Patient email unique index and ApplicationUser email unique index both exist (AC-2). Generate a migration `AddReferentialIntegrityConstraints` capturing all changes.

## Dependent Tasks

- US_008 task_001_be_domain_entity_models — All 10 entity classes must be defined.
- US_008 task_002_be_efcore_configuration_migrations — Initial EF Core configurations and `CreateDomainEntities` migration must be applied.

## Impacted Components

- **MODIFY** `src/UPACIP.DataAccess/Configurations/AppointmentConfiguration.cs` — Add composite unique index on `(PatientId, AppointmentTime)`, explicitly set cascade delete on Patient FK
- **MODIFY** `src/UPACIP.DataAccess/Configurations/PatientConfiguration.cs` — Verify unique email index exists, add comment documenting soft-delete cascade implications
- **MODIFY** `src/UPACIP.DataAccess/Configurations/IntakeDataConfiguration.cs` — Explicitly set `.OnDelete(DeleteBehavior.Cascade)` on Patient FK
- **MODIFY** `src/UPACIP.DataAccess/Configurations/ClinicalDocumentConfiguration.cs` — Explicitly set cascade on Patient FK, restrict on UploaderUserId FK
- **MODIFY** `src/UPACIP.DataAccess/Configurations/ExtractedDataConfiguration.cs` — Explicitly set cascade on DocumentId FK, set-null on VerifiedByUserId FK
- **MODIFY** `src/UPACIP.DataAccess/Configurations/MedicalCodeConfiguration.cs` — Explicitly set cascade on PatientId FK, set-null on ApprovedByUserId FK
- **MODIFY** `src/UPACIP.DataAccess/Configurations/AuditLogConfiguration.cs` — Explicitly set restrict on UserId FK (prevent user deletion while audit records exist)
- **MODIFY** `src/UPACIP.DataAccess/Configurations/QueueEntryConfiguration.cs` — Explicitly set cascade on AppointmentId FK
- **MODIFY** `src/UPACIP.DataAccess/Configurations/NotificationLogConfiguration.cs` — Explicitly set cascade on AppointmentId FK
- **NEW** `src/UPACIP.DataAccess/Migrations/*_AddReferentialIntegrityConstraints.cs` — Auto-generated migration

## Implementation Plan

1. **Add composite unique index on Appointment**: In `AppointmentConfiguration.cs`, add `builder.HasIndex(a => new { a.PatientId, a.AppointmentTime }).IsUnique().HasDatabaseName("ix_appointments_patient_id_appointment_time")`. This enforces DR-014 at the database level — PostgreSQL will reject any INSERT that duplicates the (patient_id, appointment_time) combination. EF Core will throw `DbUpdateException` containing a `PostgresException` with `SqlState = "23505"` (unique_violation).

2. **Formalize Patient cascade delete chain**: In each configuration that references Patient, explicitly set the delete behavior:
   - `AppointmentConfiguration`: `.HasOne(a => a.Patient).WithMany(p => p.Appointments).HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.Cascade)` — When a patient is deleted (hard delete), all appointments are removed.
   - `IntakeDataConfiguration`: `.OnDelete(DeleteBehavior.Cascade)` — Intake records follow patient.
   - `ClinicalDocumentConfiguration`: `.OnDelete(DeleteBehavior.Cascade)` on PatientId FK — Clinical documents follow patient.
   - `MedicalCodeConfiguration`: `.OnDelete(DeleteBehavior.Cascade)` on PatientId FK — Medical codes follow patient.

3. **Configure cascading from Appointment to dependents**: 
   - `QueueEntryConfiguration`: `.HasOne(q => q.Appointment).WithOne(a => a.QueueEntry).HasForeignKey<QueueEntry>(q => q.AppointmentId).OnDelete(DeleteBehavior.Cascade)` — Queue entry is removed when appointment is deleted.
   - `NotificationLogConfiguration`: `.HasOne(n => n.Appointment).WithMany(a => a.Notifications).HasForeignKey(n => n.AppointmentId).OnDelete(DeleteBehavior.Cascade)` — Notification logs follow appointment.

4. **Configure ClinicalDocument → ExtractedData cascade**: `ExtractedDataConfiguration`: `.HasOne<ClinicalDocument>().WithMany(cd => cd.ExtractedData).HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade)`. This addresses the edge case: when a ClinicalDocument is deleted, all ExtractedData records are removed — no orphans per DR-013.

5. **Configure restrict/set-null for User FKs**: Audit and verification FKs must NOT cascade (deleting a user should not delete audit trails or invalidate verified data):
   - `AuditLogConfiguration`: `.OnDelete(DeleteBehavior.Restrict)` — Cannot delete a user while audit records reference them. Application must handle this (e.g., soft-delete users instead).
   - `ClinicalDocumentConfiguration` (UploaderUserId): `.OnDelete(DeleteBehavior.Restrict)` — Cannot delete uploader while documents exist.
   - `ExtractedDataConfiguration` (VerifiedByUserId): `.OnDelete(DeleteBehavior.SetNull)` — If verifier user is removed, VerifiedByUserId becomes NULL (data remains, attribution cleared).
   - `MedicalCodeConfiguration` (ApprovedByUserId): `.OnDelete(DeleteBehavior.SetNull)` — If approver user is removed, ApprovedByUserId becomes NULL.

6. **Verify email uniqueness indexes**: Confirm `PatientConfiguration` has `builder.HasIndex(p => p.Email).IsUnique()` (from US_008). Confirm `ApplicationUser` has email uniqueness enforced via ASP.NET Core Identity (Identity automatically creates unique index on `NormalizedEmail`). If Identity uniqueness is not explicit, add `builder.HasIndex(u => u.Email).IsUnique()` in the Identity configuration.

7. **Generate and validate migration**: Run `dotnet ef migrations add AddReferentialIntegrityConstraints`. Inspect the generated migration to verify it captures the composite unique constraint and any delete behavior changes. Verify the `Down()` method correctly reverses all changes. Test FK violation by attempting to insert an Appointment with a random non-existent PatientId — expect `DbUpdateException`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Middleware/
│   │   │   └── GlobalExceptionHandlerMiddleware.cs
│   │   └── Controllers/
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/ (10 domain entities + BaseEntity)
│       ├── Enums/ (12 enum types)
│       ├── Configurations/ (9 IEntityTypeConfiguration files)
│       ├── Identity/
│       │   ├── ApplicationUser.cs
│       │   └── ApplicationRole.cs
│       └── Migrations/
│           └── *_CreateDomainEntities.cs
├── app/
└── scripts/
```

> Assumes US_008 (entity models + initial configurations + migration) is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.DataAccess/Configurations/AppointmentConfiguration.cs | Add composite unique index `(PatientId, AppointmentTime)`, explicitly set `OnDelete(Cascade)` on Patient FK |
| MODIFY | src/UPACIP.DataAccess/Configurations/IntakeDataConfiguration.cs | Explicitly set `OnDelete(Cascade)` on Patient FK |
| MODIFY | src/UPACIP.DataAccess/Configurations/ClinicalDocumentConfiguration.cs | Explicitly set `OnDelete(Cascade)` on Patient FK, `OnDelete(Restrict)` on UploaderUserId FK |
| MODIFY | src/UPACIP.DataAccess/Configurations/ExtractedDataConfiguration.cs | Explicitly set `OnDelete(Cascade)` on DocumentId FK, `OnDelete(SetNull)` on VerifiedByUserId FK |
| MODIFY | src/UPACIP.DataAccess/Configurations/MedicalCodeConfiguration.cs | Explicitly set `OnDelete(Cascade)` on PatientId FK, `OnDelete(SetNull)` on ApprovedByUserId FK |
| MODIFY | src/UPACIP.DataAccess/Configurations/AuditLogConfiguration.cs | Explicitly set `OnDelete(Restrict)` on UserId FK |
| MODIFY | src/UPACIP.DataAccess/Configurations/QueueEntryConfiguration.cs | Explicitly set `OnDelete(Cascade)` on AppointmentId FK |
| MODIFY | src/UPACIP.DataAccess/Configurations/NotificationLogConfiguration.cs | Explicitly set `OnDelete(Cascade)` on AppointmentId FK |
| CREATE | src/UPACIP.DataAccess/Migrations/*_AddReferentialIntegrityConstraints.cs | Migration: composite unique index, explicit cascade/restrict/set-null FK behaviors |

## External References

- [EF Core Relationships — Cascade Delete](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete)
- [EF Core DeleteBehavior Enum](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.deletebehavior)
- [EF Core Indexes — Composite Unique](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [PostgreSQL Unique Constraint Violations (SqlState 23505)](https://www.postgresql.org/docs/16/errcodes-appendix.html)
- [ASP.NET Core Identity — Unique Email Enforcement](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-8.0)

## Build Commands

```powershell
# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Generate migration
dotnet ef migrations add AddReferentialIntegrityConstraints --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Generate SQL script to inspect constraint changes
dotnet ef migrations script --idempotent --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api --output constraints.sql

# Apply migration
dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Test FK violation
psql -U upacip_app -d upacip -c "INSERT INTO appointments (appointment_id, patient_id, appointment_time, status, is_walk_in, version) VALUES (gen_random_uuid(), gen_random_uuid(), NOW(), 'Scheduled', false, 1);"
# Expected: ERROR 23503 (foreign_key_violation)

# Test duplicate booking
psql -U upacip_app -d upacip -c "INSERT INTO appointments (appointment_id, patient_id, appointment_time, status, is_walk_in, version) VALUES (gen_random_uuid(), '<existing_patient_id>', '2026-04-20 10:00:00', 'Scheduled', false, 1); INSERT INTO appointments (appointment_id, patient_id, appointment_time, status, is_walk_in, version) VALUES (gen_random_uuid(), '<existing_patient_id>', '2026-04-20 10:00:00', 'Scheduled', false, 1);"
# Expected: ERROR 23505 (unique_violation) on second insert
```

## Implementation Validation Strategy

- [ ] `dotnet ef migrations add` generates migration without errors
- [ ] Inserting an Appointment with a non-existent `patient_id` fails with FK violation (PostgreSQL error 23503)
- [ ] Inserting a duplicate (patient_id, appointment_time) pair fails with unique violation (PostgreSQL error 23505)
- [ ] Inserting a duplicate email for Patient fails with unique constraint violation
- [ ] Deleting a Patient cascades deletion to Appointments, IntakeData, ClinicalDocuments, MedicalCodes
- [ ] Deleting a ClinicalDocument cascades to ExtractedData (no orphans)
- [ ] Deleting a User with existing AuditLog records fails (Restrict behavior)
- [ ] Deleting a User sets `VerifiedByUserId` to NULL on ExtractedData and `ApprovedByUserId` to NULL on MedicalCode

## Implementation Checklist

- [ ] Add composite unique index on `(PatientId, AppointmentTime)` in `AppointmentConfiguration` with explicit database name `ix_appointments_patient_id_appointment_time`
- [ ] Explicitly configure `OnDelete(DeleteBehavior.Cascade)` on Patient FK in Appointment, IntakeData, ClinicalDocument, and MedicalCode configurations
- [ ] Explicitly configure `OnDelete(DeleteBehavior.Cascade)` on ClinicalDocument→ExtractedData FK and Appointment→QueueEntry/NotificationLog FKs
- [ ] Configure `OnDelete(DeleteBehavior.Restrict)` on User FK in AuditLog and ClinicalDocument (UploaderUserId) configurations
- [ ] Configure `OnDelete(DeleteBehavior.SetNull)` on nullable User FKs: ExtractedData.VerifiedByUserId and MedicalCode.ApprovedByUserId
- [ ] Verify Patient email unique index and ApplicationUser email unique index both exist
- [ ] Generate `AddReferentialIntegrityConstraints` migration and verify DDL includes composite unique constraint and FK behavior changes with `Down()` rollback
