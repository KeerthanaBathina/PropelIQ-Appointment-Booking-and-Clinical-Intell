# Task - task_001_db_resolution_schema

## Requirement Reference

- User Story: US_045
- Story Location: .propel/context/tasks/EP-007/us_045/us_045.md
- Acceptance Criteria:
  - AC-2: Given the staff member reviews a conflict, When they select the correct data value, Then the chosen value is saved to the consolidated profile and the conflict is marked "resolved" with staff attribution.
  - AC-4: Given a staff member resolves all conflicts for a patient, When the last conflict is resolved, Then the profile status updates to "verified" and an audit log entry is created.
- Edge Case:
  - Both values correct: Staff can select "Both Valid — Different Dates" which preserves both entries with distinct date attribution.

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

Extend the ClinicalConflict entity (from US_044/task_001) with resolution workflow fields to support staff data value selection and the "Both Valid — Different Dates" option. Add a verification status lifecycle to PatientProfileVersion (from US_043/task_001) so that the profile can transition to "verified" when all conflicts are resolved. These schema changes enable the full staff resolution workflow defined in FR-054.

## Dependent Tasks

- US_044/task_001_db_conflict_schema - Requires ClinicalConflict entity with status, severity, and source data fields
- US_043/task_001_db_profile_versioning_schema - Requires PatientProfileVersion entity

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| MODIFY | `ClinicalConflict` entity - Add resolution workflow fields | Server (Data Layer) |
| MODIFY | `PatientProfileVersion` entity - Add verification lifecycle fields | Server (Data Layer) |
| NEW | `ConflictResolutionType` enum | Server (Data Layer) |
| NEW | `ProfileVerificationStatus` enum | Server (Data Layer) |
| MODIFY | `PatientDbContext` - Configure new fields and indexes | Server (Data Layer) |
| NEW | EF Core migration for resolution workflow fields | Server (Data Layer) |

## Implementation Plan

1. Create `ConflictResolutionType` enum: SelectedValue, BothValid, Dismissed
   - SelectedValue: Staff picked one source value as correct (AC-2)
   - BothValid: Staff confirmed both values are valid with different date attribution (EC-2)
   - Dismissed: False-positive conflict (carried from US_044)
2. Create `ProfileVerificationStatus` enum: Unverified, PartiallyVerified, Verified
   - Unverified: Default for new profiles with unresolved conflicts
   - PartiallyVerified: Some conflicts resolved, others pending (EC-1 from US_045)
   - Verified: All conflicts resolved by staff (AC-4)
3. Add resolution workflow fields to `ClinicalConflict` entity:
   - `resolution_type` (ConflictResolutionType enum, nullable) - How the conflict was resolved
   - `selected_extracted_data_id` (FK to ExtractedData, nullable) - The specific data value staff chose as correct (AC-2)
   - `both_valid_explanation` (text, nullable) - Staff explanation when "Both Valid" is selected (EC-2)
4. Add verification lifecycle fields to `PatientProfileVersion` entity:
   - `verification_status` (ProfileVerificationStatus enum, default Unverified) - Current verification state (AC-4)
   - `verified_by_user_id` (FK to User, nullable) - Staff who completed final verification (AC-4)
   - `verified_at` (DateTimeOffset, nullable) - Timestamp of verification completion (AC-4)
5. Configure in PatientDbContext.OnModelCreating:
   - FK: ClinicalConflict.selected_extracted_data_id → ExtractedData
   - FK: PatientProfileVersion.verified_by_user_id → User
   - Index: PatientProfileVersion(patient_id, verification_status) for verification status lookups
   - Enum-to-string value conversions for both new enums
6. Generate EF Core migration with rollback support

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Data/
    Entities/
      ClinicalConflict.cs
      PatientProfileVersion.cs
      ExtractedData.cs
      User.cs
    Enums/
      ConflictType.cs
      ConflictSeverity.cs
      ConflictStatus.cs
    Migrations/
    PatientDbContext.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Data/Enums/ConflictResolutionType.cs | Enum: SelectedValue, BothValid, Dismissed |
| CREATE | Server/Data/Enums/ProfileVerificationStatus.cs | Enum: Unverified, PartiallyVerified, Verified |
| MODIFY | Server/Data/Entities/ClinicalConflict.cs | Add resolution_type, selected_extracted_data_id (FK), both_valid_explanation fields |
| MODIFY | Server/Data/Entities/PatientProfileVersion.cs | Add verification_status, verified_by_user_id (FK), verified_at fields |
| MODIFY | Server/Data/PatientDbContext.cs | Configure new FKs, indexes, and enum-to-string conversions |
| CREATE | Server/Data/Migrations/{timestamp}_AddResolutionWorkflowFields.cs | EF Core migration with up/down methods |

## External References

- [EF Core 8 Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/) - Code-first migration workflow
- [EF Core Enum Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions#built-in-converters) - Enum-to-string storage for readable DB values
- [EF Core Nullable FK Relationships](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/one-to-many#optional-relationships) - Nullable FK for selected_extracted_data_id

## Build Commands

- `dotnet ef migrations add AddResolutionWorkflowFields --project Server`
- `dotnet ef database update --project Server`
- `dotnet build Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies successfully to clean database
- [ ] Migration rollback restores previous schema state
- [ ] ClinicalConflict.selected_extracted_data_id FK constraint enforced
- [ ] PatientProfileVersion.verification_status defaults to Unverified for new records
- [ ] Enum-to-string conversions produce readable values in database

## Implementation Checklist

- [ ] Create ConflictResolutionType enum (SelectedValue, BothValid, Dismissed) and ProfileVerificationStatus enum (Unverified, PartiallyVerified, Verified)
- [ ] Add resolution workflow fields to ClinicalConflict entity (resolution_type, selected_extracted_data_id FK, both_valid_explanation)
- [ ] Add verification lifecycle fields to PatientProfileVersion entity (verification_status, verified_by_user_id FK, verified_at)
- [ ] Configure FK constraints and navigation properties for selected_extracted_data_id and verified_by_user_id in PatientDbContext
- [ ] Add index on PatientProfileVersion(patient_id, verification_status) and configure enum-to-string value conversions
- [ ] Generate and verify EF Core migration script with up/down methods and rollback support
