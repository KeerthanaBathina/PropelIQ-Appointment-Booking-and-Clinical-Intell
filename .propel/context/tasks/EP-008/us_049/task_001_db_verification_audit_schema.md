# Task - task_001_db_verification_audit_schema

## Requirement Reference

- User Story: US_049
- Story Location: .propel/context/tasks/EP-008/us_049/us_049.md
- Acceptance Criteria:
  - AC-2: Given a staff member approves an AI-suggested code, When they click "Approve," Then the code status changes to "verified" with staff attribution and timestamp in the audit trail.
  - AC-4: Given a code change is made (approval or override), When the change is saved, Then an immutable audit log entry records the old code, new code, justification, user, and timestamp.
- Edge Case:
  - Deprecated code approval: System blocks approval and shows the deprecated notice with suggested replacement codes.

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

Extend the MedicalCode entity with verification lifecycle fields (status, verified_by, verified_at, override justification, deprecated flag) and create a specialized CodingAuditLog entity for immutable code change tracking. The MedicalCode entity (from US_008) currently stores AI-generated codes but lacks verification workflow state. This task adds the schema needed for staff approval/override actions and the audit trail required by FR-066.

## Dependent Tasks

- US_008 (EP-DATA) - Requires MedicalCode, User, and AuditLog entities

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| MODIFY | `MedicalCode` entity - Add verification lifecycle fields | Server (Data Layer) |
| NEW | `CodingAuditLog` entity | Server (Data Layer) |
| NEW | `CodeVerificationStatus` enum | Server (Data Layer) |
| NEW | `CodingAuditAction` enum | Server (Data Layer) |
| MODIFY | `PatientDbContext` - Add DbSet for CodingAuditLog, configure new fields | Server (Data Layer) |
| NEW | EF Core migration for verification fields and CodingAuditLog table | Server (Data Layer) |

## Implementation Plan

1. Add verification lifecycle fields to `MedicalCode` entity:
   - `status` (enum: pending, verified, overridden, deprecated) with default "pending" (AC-2)
   - `verified_by_user_id` (FK to User, nullable) for staff attribution (AC-2)
   - `verified_at` (timestamp, nullable) for verification timestamp (AC-2)
   - `override_justification` (text, nullable) for override reason (AC-4)
   - `original_code_value` (string, nullable) to store pre-override code value (AC-4)
   - `is_deprecated` (boolean, default false) for library update flagging (EC-1)
2. Create `CodeVerificationStatus` enum: Pending, Verified, Overridden, Deprecated
3. Create `CodingAuditAction` enum: Approved, Overridden, DeprecatedBlocked, Revalidated
4. Create `CodingAuditLog` entity with immutable columns: log_id (UUID PK), medical_code_id (FK to MedicalCode), patient_id (FK to Patient), action (CodingAuditAction enum), old_code_value (string), new_code_value (string), justification (text, nullable), user_id (FK to User), timestamp (DateTimeOffset, immutable), created_at
5. Configure foreign keys and indexes in PatientDbContext.OnModelCreating:
   - Index on MedicalCode: (patient_id, status) for verification queue lookups
   - Index on CodingAuditLog: (medical_code_id) for per-code audit trail
   - Index on CodingAuditLog: (patient_id, timestamp DESC) for patient-level audit history
6. Generate EF Core code-first migration with rollback support

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Data/
    Entities/
      Patient.cs
      MedicalCode.cs
      User.cs
      AuditLog.cs
    Enums/
    Migrations/
    PatientDbContext.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Data/Entities/MedicalCode.cs | Add status, verified_by_user_id, verified_at, override_justification, original_code_value, is_deprecated fields with FK navigation properties |
| CREATE | Server/Data/Entities/CodingAuditLog.cs | Immutable audit entity: old code, new code, justification, user, timestamp per AC-4 |
| CREATE | Server/Data/Enums/CodeVerificationStatus.cs | Enum: Pending, Verified, Overridden, Deprecated |
| CREATE | Server/Data/Enums/CodingAuditAction.cs | Enum: Approved, Overridden, DeprecatedBlocked, Revalidated |
| MODIFY | Server/Data/PatientDbContext.cs | Add DbSet for CodingAuditLog, configure new MedicalCode fields, add indexes in OnModelCreating |
| CREATE | Server/Data/Migrations/{timestamp}_AddCodeVerificationAndAudit.cs | EF Core migration with up/down methods |

## External References

- [EF Core 8 Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/) - Code-first migration workflow
- [EF Core Enum Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions#built-in-converters) - Enum-to-string storage for readable DB values
- [PostgreSQL Indexes](https://www.postgresql.org/docs/16/indexes.html) - Composite index strategy for verification queue

## Build Commands

- `dotnet ef migrations add AddCodeVerificationAndAudit --project Server`
- `dotnet ef database update --project Server`
- `dotnet build Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies successfully to clean database
- [ ] Migration rollback restores previous schema state
- [ ] MedicalCode status defaults to "pending" for new records
- [ ] CodingAuditLog timestamp column is immutable (no UPDATE allowed)
- [ ] Indexes created on (patient_id, status) and (medical_code_id)

## Implementation Checklist

- [x] Add verification lifecycle fields to MedicalCode entity (status, verified_by_user_id, verified_at, override_justification, original_code_value, is_deprecated) with FK navigation properties
- [x] Create CodeVerificationStatus enum (Pending, Verified, Overridden, Deprecated) and CodingAuditAction enum (Approved, Overridden, DeprecatedBlocked, Revalidated)
- [x] Create CodingAuditLog entity with immutable audit columns (old_code_value, new_code_value, justification, user_id, timestamp)
- [x] Register CodingAuditLog in PatientDbContext with FK constraints and enum-to-string value conversions
- [x] Configure composite indexes: MedicalCode(patient_id, status), CodingAuditLog(medical_code_id), CodingAuditLog(patient_id, timestamp DESC)
- [x] Generate and verify EF Core migration script with up/down methods and rollback support
