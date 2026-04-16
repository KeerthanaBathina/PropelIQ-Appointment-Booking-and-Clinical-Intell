# Task - task_001_be_domain_entity_models

## Requirement Reference

- User Story: us_008
- Story Location: .propel/context/tasks/EP-DATA/us_008/us_008.md
- Acceptance Criteria:
  - AC-1: Given entity models are defined, When a developer runs `dotnet ef migrations add CreateDomainEntities`, Then the migration generates DDL for Patient, Appointment, IntakeData, ClinicalDocument, ExtractedData, MedicalCode, User, AuditLog, QueueEntry, and NotificationLog.
  - AC-4: Given JSONB columns are configured, When data is stored in preferred_slot_criteria, mandatory_fields, optional_fields, insurance_info, or data_content columns, Then EF Core serializes and deserializes JSON correctly.
  - AC-5: Given enum mappings are configured, When an appointment status is set to "scheduled", Then the database stores the enum as a string value in the status column.
- Edge Case:
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

Define all 10 domain entity classes as C# POCOs in the `UPACIP.DataAccess` project, matching the ERD from models.md. Create a `BaseEntity` abstract class with shared audit fields (`Id`, `CreatedAt`, `UpdatedAt`). Define all domain enum types used across entities (AppointmentStatus, IntakeMethod, DocumentCategory, ProcessingStatus, DataType, CodeType, AuditAction, QueuePriority, QueueStatus, NotificationType, DeliveryChannel, NotificationStatus). Each entity includes properly typed properties, JSONB-destined properties as owned types or `JsonDocument`, nullable `DeletedAt` on Patient for soft delete, and navigation properties for all foreign key relationships per the ERD.

## Dependent Tasks

- US_003 task_002_be_efcore_integration — `ApplicationDbContext` and EF Core + Npgsql packages must be registered.
- US_005 task_001_be_identity_configuration — `ApplicationUser` (IdentityUser<Guid>) already exists; the `User` entity in the ERD maps to this existing class. New entities reference `ApplicationUser` via FK.

## Impacted Components

- **NEW** `src/UPACIP.DataAccess/Entities/BaseEntity.cs` — Abstract base class with `Id` (Guid), `CreatedAt`, `UpdatedAt`
- **NEW** `src/UPACIP.DataAccess/Entities/Patient.cs` — Patient entity with email UK, soft-delete `DeletedAt`
- **NEW** `src/UPACIP.DataAccess/Entities/Appointment.cs` — Appointment entity with `Version` (concurrency), JSONB `PreferredSlotCriteria`
- **NEW** `src/UPACIP.DataAccess/Entities/IntakeData.cs` — IntakeData entity with JSONB `MandatoryFields`, `OptionalFields`, `InsuranceInfo`
- **NEW** `src/UPACIP.DataAccess/Entities/ClinicalDocument.cs` — ClinicalDocument entity with document metadata and processing status
- **NEW** `src/UPACIP.DataAccess/Entities/ExtractedData.cs` — ExtractedData entity with JSONB `DataContent`, confidence score
- **NEW** `src/UPACIP.DataAccess/Entities/MedicalCode.cs` — MedicalCode entity with ICD-10/CPT code type, AI confidence
- **NEW** `src/UPACIP.DataAccess/Entities/AuditLog.cs` — AuditLog entity (no BaseEntity — uses own `LogId`, no `UpdatedAt`)
- **NEW** `src/UPACIP.DataAccess/Entities/QueueEntry.cs` — QueueEntry entity with priority and arrival timestamp
- **NEW** `src/UPACIP.DataAccess/Entities/NotificationLog.cs` — NotificationLog entity with delivery channel and retry count
- **NEW** `src/UPACIP.DataAccess/Enums/` — All domain enum type files

## Implementation Plan

1. **Create `BaseEntity` abstract class**: Define in `src/UPACIP.DataAccess/Entities/BaseEntity.cs` with `public Guid Id { get; set; }`, `public DateTime CreatedAt { get; set; }`, `public DateTime UpdatedAt { get; set; }`. All entities except `AuditLog` and `NotificationLog` inherit from this base class. `AuditLog` has its own `LogId` and no `UpdatedAt`. `NotificationLog` has its own `NotificationId` and only `CreatedAt`.

2. **Create all domain enum types**: Create individual files under `src/UPACIP.DataAccess/Enums/`:
   - `AppointmentStatus` — Scheduled, Completed, Cancelled, NoShow
   - `IntakeMethod` — AiConversational, ManualForm
   - `DocumentCategory` — LabResult, Prescription, ClinicalNote, ImagingReport
   - `ProcessingStatus` — Queued, Processing, Completed, Failed
   - `DataType` — Medication, Diagnosis, Procedure, Allergy
   - `CodeType` — Icd10, Cpt
   - `AuditAction` — Login, Logout, DataAccess, DataModify, DataDelete
   - `QueuePriority` — Normal, Urgent
   - `QueueStatus` — Waiting, InVisit, Completed
   - `NotificationType` — Confirmation, Reminder24h, Reminder2h, SlotSwap
   - `DeliveryChannel` — Email, Sms
   - `NotificationStatus` — Sent, Failed, Bounced

3. **Define `Patient` entity**: Extends `BaseEntity`. Properties: `string Email` (unique), `string PasswordHash`, `string FullName`, `DateOnly DateOfBirth`, `string PhoneNumber`, `string? EmergencyContact`, `DateTime? DeletedAt`. Navigation: `ICollection<Appointment> Appointments`, `ICollection<IntakeData> IntakeRecords`, `ICollection<ClinicalDocument> ClinicalDocuments`, `ICollection<MedicalCode> MedicalCodes`. Maps to DR-001.

4. **Define `Appointment` entity**: Extends `BaseEntity`. Properties: `Guid PatientId` (FK), `DateTime AppointmentTime`, `AppointmentStatus Status`, `bool IsWalkIn`, `PreferredSlotCriteria? PreferredSlotCriteria` (owned type for JSONB), `int Version` (concurrency token). Navigation: `Patient Patient`, `ICollection<NotificationLog> Notifications`, `QueueEntry? QueueEntry`. Maps to DR-002.

5. **Define `IntakeData` entity**: Extends `BaseEntity`. Properties: `Guid PatientId` (FK), `IntakeMethod IntakeMethod`, `IntakeMandatoryFields? MandatoryFields` (JSONB), `IntakeOptionalFields? OptionalFields` (JSONB), `InsuranceInfo? InsuranceInfo` (JSONB), `DateTime? CompletedAt`. Navigation: `Patient Patient`. Create owned type classes `IntakeMandatoryFields`, `IntakeOptionalFields`, `InsuranceInfo` for strongly-typed JSONB serialization.

6. **Define `ClinicalDocument` and `ExtractedData` entities**: `ClinicalDocument` extends `BaseEntity`. Properties: `Guid PatientId` (FK), `DocumentCategory DocumentCategory`, `string FilePath`, `DateTime UploadDate`, `Guid UploaderUserId` (FK → ApplicationUser), `ProcessingStatus ProcessingStatus`. `ExtractedData` extends `BaseEntity`. Properties: `Guid DocumentId` (FK), `DataType DataType`, `ExtractedDataContent? DataContent` (JSONB owned type), `float ConfidenceScore`, `string SourceAttribution`, `bool FlaggedForReview`, `Guid? VerifiedByUserId` (FK → ApplicationUser). Maps to DR-003, DR-004.

7. **Define `MedicalCode`, `AuditLog`, `QueueEntry`, `NotificationLog` entities**: `MedicalCode` extends `BaseEntity` — `Guid PatientId`, `CodeType CodeType`, `string CodeValue`, `string Description`, `string Justification`, `bool SuggestedByAi`, `Guid? ApprovedByUserId` (FK), `float? AiConfidenceScore`. `AuditLog` — own `Guid LogId`, `Guid UserId` (FK), `AuditAction Action`, `string ResourceType`, `Guid? ResourceId`, `DateTime Timestamp`, `string IpAddress`, `string UserAgent`. `QueueEntry` extends `BaseEntity` — `Guid AppointmentId` (FK), `DateTime ArrivalTimestamp`, `int WaitTimeMinutes`, `QueuePriority Priority`, `QueueStatus Status`. `NotificationLog` — own `Guid NotificationId`, `Guid AppointmentId` (FK), `NotificationType NotificationType`, `DeliveryChannel DeliveryChannel`, `NotificationStatus Status`, `int RetryCount`, `DateTime? SentAt`, `DateTime CreatedAt`. Maps to DR-005, DR-006, DR-007, DR-008.

8. **Add navigation properties for all FK relationships**: Ensure bidirectional navigation where needed. `ApplicationUser` → `ICollection<ClinicalDocument>`, `ICollection<AuditLog>`. `ClinicalDocument` → `ICollection<ExtractedData>`. `Appointment` → `QueueEntry`, `ICollection<NotificationLog>`. All FK properties use `Guid` type consistent with PostgreSQL UUID.

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
│       └── Identity/
│           ├── ApplicationUser.cs
│           └── ApplicationRole.cs
├── app/
└── scripts/
```

> Assumes US_003 (EF Core integration) and US_005 (Identity configuration) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.DataAccess/Entities/BaseEntity.cs | Abstract base class with Id (Guid), CreatedAt, UpdatedAt |
| CREATE | src/UPACIP.DataAccess/Enums/AppointmentStatus.cs | Enum: Scheduled, Completed, Cancelled, NoShow |
| CREATE | src/UPACIP.DataAccess/Enums/IntakeMethod.cs | Enum: AiConversational, ManualForm |
| CREATE | src/UPACIP.DataAccess/Enums/DocumentCategory.cs | Enum: LabResult, Prescription, ClinicalNote, ImagingReport |
| CREATE | src/UPACIP.DataAccess/Enums/ProcessingStatus.cs | Enum: Queued, Processing, Completed, Failed |
| CREATE | src/UPACIP.DataAccess/Enums/DataType.cs | Enum: Medication, Diagnosis, Procedure, Allergy |
| CREATE | src/UPACIP.DataAccess/Enums/CodeType.cs | Enum: Icd10, Cpt |
| CREATE | src/UPACIP.DataAccess/Enums/AuditAction.cs | Enum: Login, Logout, DataAccess, DataModify, DataDelete |
| CREATE | src/UPACIP.DataAccess/Enums/QueuePriority.cs | Enum: Normal, Urgent |
| CREATE | src/UPACIP.DataAccess/Enums/QueueStatus.cs | Enum: Waiting, InVisit, Completed |
| CREATE | src/UPACIP.DataAccess/Enums/NotificationType.cs | Enum: Confirmation, Reminder24h, Reminder2h, SlotSwap |
| CREATE | src/UPACIP.DataAccess/Enums/DeliveryChannel.cs | Enum: Email, Sms |
| CREATE | src/UPACIP.DataAccess/Enums/NotificationStatus.cs | Enum: Sent, Failed, Bounced |
| CREATE | src/UPACIP.DataAccess/Entities/Patient.cs | Patient entity with email UK, soft-delete DeletedAt, navigation properties |
| CREATE | src/UPACIP.DataAccess/Entities/Appointment.cs | Appointment entity with Version concurrency token, JSONB PreferredSlotCriteria |
| CREATE | src/UPACIP.DataAccess/Entities/IntakeData.cs | IntakeData entity with JSONB MandatoryFields, OptionalFields, InsuranceInfo |
| CREATE | src/UPACIP.DataAccess/Entities/ClinicalDocument.cs | ClinicalDocument entity with document metadata and processing status |
| CREATE | src/UPACIP.DataAccess/Entities/ExtractedData.cs | ExtractedData entity with JSONB DataContent, confidence score |
| CREATE | src/UPACIP.DataAccess/Entities/MedicalCode.cs | MedicalCode entity with ICD-10/CPT code type, AI confidence |
| CREATE | src/UPACIP.DataAccess/Entities/AuditLog.cs | AuditLog entity with action enum, resource tracking |
| CREATE | src/UPACIP.DataAccess/Entities/QueueEntry.cs | QueueEntry entity with priority and arrival timestamp |
| CREATE | src/UPACIP.DataAccess/Entities/NotificationLog.cs | NotificationLog entity with delivery channel and retry count |
| CREATE | src/UPACIP.DataAccess/Entities/OwnedTypes/PreferredSlotCriteria.cs | Owned type for Appointment JSONB column |
| CREATE | src/UPACIP.DataAccess/Entities/OwnedTypes/IntakeMandatoryFields.cs | Owned type for IntakeData JSONB column |
| CREATE | src/UPACIP.DataAccess/Entities/OwnedTypes/IntakeOptionalFields.cs | Owned type for IntakeData JSONB column |
| CREATE | src/UPACIP.DataAccess/Entities/OwnedTypes/InsuranceInfo.cs | Owned type for IntakeData JSONB column |
| CREATE | src/UPACIP.DataAccess/Entities/OwnedTypes/ExtractedDataContent.cs | Owned type for ExtractedData JSONB column |

## External References

- [EF Core Entity Types](https://learn.microsoft.com/en-us/ef/core/modeling/entity-types)
- [EF Core Owned Entity Types (JSON columns)](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-7.0/whatsnew#json-columns)
- [Npgsql JSON/JSONB mapping](https://www.npgsql.org/efcore/mapping/json.html)
- [Npgsql Enum mapping](https://www.npgsql.org/efcore/mapping/enum.html)
- [EF Core Value Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions)
- [ERD Reference — models.md Logical Data Model](../../../docs/models.md)

## Build Commands

```powershell
# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Build full solution (ensures entity references compile)
dotnet build UPACIP.sln

# Verify all entity types are recognized by EF Core
dotnet ef dbcontext info --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for UPACIP.DataAccess project
- [ ] All 10 entity classes compile and match the ERD column definitions
- [ ] All 12 enum types compile with correct member values
- [ ] `BaseEntity` provides `Id`, `CreatedAt`, `UpdatedAt` inherited by applicable entities
- [ ] `Patient.DeletedAt` is nullable `DateTime?` for soft delete support
- [ ] `Appointment.Version` property exists as `int` for concurrency token
- [ ] JSONB-destined properties use owned types (PreferredSlotCriteria, IntakeMandatoryFields, etc.)
- [ ] Navigation properties establish correct FK relationships per ERD

## Implementation Checklist

- [ ] Create `BaseEntity` abstract class in `src/UPACIP.DataAccess/Entities/BaseEntity.cs` with `Id` (Guid), `CreatedAt` (DateTime), `UpdatedAt` (DateTime)
- [ ] Create all 12 domain enum types under `src/UPACIP.DataAccess/Enums/` matching ERD values (AppointmentStatus, IntakeMethod, DocumentCategory, ProcessingStatus, DataType, CodeType, AuditAction, QueuePriority, QueueStatus, NotificationType, DeliveryChannel, NotificationStatus)
- [ ] Define `Patient` entity extending `BaseEntity` with `Email` (unique), `PasswordHash`, `FullName`, `DateOfBirth`, `PhoneNumber`, `EmergencyContact`, nullable `DeletedAt`, and navigation properties to Appointments, IntakeRecords, ClinicalDocuments, MedicalCodes
- [ ] Define `Appointment` entity extending `BaseEntity` with `PatientId` FK, `AppointmentTime`, `Status` enum, `IsWalkIn`, `PreferredSlotCriteria` owned type (JSONB), `Version` int, and navigation properties
- [ ] Define `IntakeData` entity extending `BaseEntity` with `PatientId` FK, `IntakeMethod` enum, three JSONB owned types (`MandatoryFields`, `OptionalFields`, `InsuranceInfo`), `CompletedAt`, and Patient navigation
- [ ] Define `ClinicalDocument` and `ExtractedData` entities with FK relationships, enum properties, `ExtractedDataContent` JSONB owned type, `ConfidenceScore`, and `VerifiedByUserId` nullable FK
- [ ] Define `MedicalCode`, `AuditLog`, `QueueEntry`, `NotificationLog` entities with their respective properties, enum types, FK relationships, and navigation properties per ERD
- [ ] Create owned type classes under `Entities/OwnedTypes/` for all JSONB columns: `PreferredSlotCriteria`, `IntakeMandatoryFields`, `IntakeOptionalFields`, `InsuranceInfo`, `ExtractedDataContent`
