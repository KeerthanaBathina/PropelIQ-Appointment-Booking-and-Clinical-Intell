# Task - task_001_db_conflict_schema

## Requirement Reference

- User Story: US_044
- Story Location: .propel/context/tasks/EP-007/us_044/us_044.md
- Acceptance Criteria:
  - AC-2: Given duplicate diagnoses with different dates are found, When the conflict is detected, Then the system flags the conflict with source citations from both documents.
  - AC-3: Given a medication contraindication is detected, When the conflict is flagged, Then the system escalates it with an "URGENT" indicator and moves it to the top of the review queue.
  - AC-5: Given clinical event dates fail chronological plausibility validation, When the inconsistency is detected, Then the system flags the date conflict with a clear explanation.
- Edge Case:
  - N/A (schema layer does not directly handle edge case logic)

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

Create the database schema and EF Core migration for persisting clinical data conflicts detected by the AI conflict detection service. The `ClinicalConflict` entity records conflict type, severity, urgency status, source data points and documents, AI explanation, resolution state, and staff attribution. This schema supports the conflict lifecycle from detection through resolution, enabling the review queue and side-by-side comparison workflows required by FR-053.

## Dependent Tasks

- US_008 (EP-DATA) - Requires Patient, ExtractedData, ClinicalDocument, and User entities
- US_043/task_001_db_profile_versioning_schema - Requires PatientProfileVersion table for version correlation

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `ClinicalConflict` entity model | Server (Data Layer) |
| NEW | `ConflictType` enum | Server (Data Layer) |
| NEW | `ConflictSeverity` enum | Server (Data Layer) |
| NEW | `ConflictStatus` enum | Server (Data Layer) |
| NEW | EF Core migration for `clinical_conflicts` table | Server (Data Layer) |
| MODIFY | `PatientDbContext` - Add DbSet for ClinicalConflict | Server (Data Layer) |

## Implementation Plan

1. Define the `ClinicalConflict` EF Core entity with columns: `conflict_id` (UUID PK), `patient_id` (FK to Patient), `conflict_type` (enum: medication_discrepancy, duplicate_diagnosis, date_inconsistency, medication_contraindication), `severity` (enum: critical, high, medium, low), `status` (enum: detected, under_review, resolved, dismissed), `is_urgent` (boolean, default false), `source_extracted_data_ids` (JSONB array of ExtractedData UUIDs), `source_document_ids` (JSONB array of ClinicalDocument UUIDs), `conflict_description` (text), `ai_explanation` (text), `ai_confidence_score` (float 0-1), `resolved_by_user_id` (FK to User, nullable), `resolution_notes` (text, nullable), `resolved_at` (timestamp, nullable), `profile_version_id` (FK to PatientProfileVersion, nullable), `created_at`, `updated_at`
2. Add foreign key constraints: `patient_id` references Patient, `resolved_by_user_id` references User, `profile_version_id` references PatientProfileVersion
3. Create index on `(patient_id, status)` for active conflict lookups
4. Create index on `(is_urgent, created_at DESC)` for urgent review queue ordering
5. Create index on `(patient_id, conflict_type)` for type-specific conflict queries
6. Generate EF Core code-first migration with rollback support

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
Server/
  Data/
    Entities/
      Patient.cs
      ClinicalDocument.cs
      ExtractedData.cs
      User.cs
      PatientProfileVersion.cs
    Enums/
      ConsolidationType.cs
    Migrations/
    PatientDbContext.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Data/Entities/ClinicalConflict.cs | Entity with conflict lifecycle columns, FK relationships, JSONB properties |
| CREATE | Server/Data/Enums/ConflictType.cs | Enum: MedicationDiscrepancy, DuplicateDiagnosis, DateInconsistency, MedicationContraindication |
| CREATE | Server/Data/Enums/ConflictSeverity.cs | Enum: Critical, High, Medium, Low |
| CREATE | Server/Data/Enums/ConflictStatus.cs | Enum: Detected, UnderReview, Resolved, Dismissed |
| MODIFY | Server/Data/PatientDbContext.cs | Add DbSet&lt;ClinicalConflict&gt;, configure entity in OnModelCreating with indexes and FK constraints |
| CREATE | Server/Data/Migrations/{timestamp}_AddClinicalConflictTable.cs | EF Core migration with up/down methods |

## External References

- [EF Core 8 Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/) - Code-first migration workflow
- [PostgreSQL JSONB](https://www.postgresql.org/docs/16/datatype-json.html) - JSONB column for source ID arrays
- [EF Core Enum Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions#built-in-converters) - Enum-to-string storage

## Build Commands

- `dotnet ef migrations add AddClinicalConflictTable --project Server`
- `dotnet ef database update --project Server`
- `dotnet build Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies successfully to clean database
- [ ] Migration rollback restores previous schema state
- [ ] Foreign key constraints enforced (invalid patient_id rejected)
- [ ] Indexes created on (patient_id, status), (is_urgent, created_at), (patient_id, conflict_type)
- [ ] JSONB columns accept and return valid UUID arrays

## Implementation Checklist

- [ ] Define `ClinicalConflict` entity class with all columns, FK navigation properties, and JSONB-typed properties
- [ ] Create `ConflictType`, `ConflictSeverity`, and `ConflictStatus` enums
- [ ] Register entity in `PatientDbContext.OnModelCreating` with FK constraints, composite indexes, and JSONB column configuration
- [ ] Configure enum-to-string value conversions for readable database storage
- [ ] Generate and verify EF Core migration script with up/down methods
- [ ] Apply migration to development database and validate schema
