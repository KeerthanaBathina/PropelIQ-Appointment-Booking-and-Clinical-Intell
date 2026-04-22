# Task - task_001_db_profile_versioning_schema

## Requirement Reference

- User Story: US_043
- Story Location: .propel/context/tasks/EP-007/us_043/us_043.md
- Acceptance Criteria:
  - AC-2: Given the consolidated profile is created or updated, When the update completes, Then a new profile version is recorded with timestamp, user attribution, and list of source documents.
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

Create the database schema and EF Core migration for patient profile version tracking. This introduces a `PatientProfileVersion` table that records each consolidation event with timestamp, user attribution, source document list, and a snapshot of the consolidated data delta. The schema supports the version history required by FR-056 and provides the persistence foundation for the consolidation service.

## Dependent Tasks

- US_008 (EP-DATA) - Requires Patient, ExtractedData, and ClinicalDocument entities to exist
- US_040 (EP-006) - Requires ClinicalDocument and ExtractedData tables to be populated

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `PatientProfileVersion` entity model | Server (Data Layer) |
| NEW | EF Core migration for `patient_profile_versions` table | Server (Data Layer) |
| MODIFY | `PatientDbContext` - Add DbSet for PatientProfileVersion | Server (Data Layer) |

## Implementation Plan

1. Define the `PatientProfileVersion` EF Core entity with required columns: `version_id` (UUID PK), `patient_id` (FK to Patient), `version_number` (int, auto-increment per patient), `consolidated_by_user_id` (FK to User), `source_document_ids` (JSONB array of document UUIDs), `data_snapshot` (JSONB delta of merged data), `created_at` (timestamp), `consolidation_type` (enum: initial, incremental)
2. Add foreign key constraints: `patient_id` references `Patient(patient_id)`, `consolidated_by_user_id` references `User(user_id)`
3. Create composite index on `(patient_id, version_number)` with unique constraint for ordered version retrieval
4. Create index on `patient_id` with descending `created_at` for latest-version lookups
5. Generate EF Core code-first migration with rollback support
6. Add seed data script for development/testing with sample version entries

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
    Migrations/
    PatientDbContext.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Data/Entities/PatientProfileVersion.cs | New entity model with version tracking columns, FK relationships, JSONB properties |
| CREATE | Server/Data/Enums/ConsolidationType.cs | Enum: Initial, Incremental |
| MODIFY | Server/Data/PatientDbContext.cs | Add DbSet&lt;PatientProfileVersion&gt;, configure entity in OnModelCreating |
| CREATE | Server/Data/Migrations/{timestamp}_AddPatientProfileVersionTable.cs | EF Core migration with rollback |
| CREATE | Server/Data/Seeds/PatientProfileVersionSeedData.cs | Sample version entries for development |

## External References

- [EF Core 8 Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/) - Code-first migration workflow
- [PostgreSQL JSONB](https://www.postgresql.org/docs/16/datatype-json.html) - JSONB column for source_document_ids and data_snapshot
- [EF Core Value Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions) - JSON serialization for JSONB columns

## Build Commands

- `dotnet ef migrations add AddPatientProfileVersionTable --project Server`
- `dotnet ef database update --project Server`
- `dotnet build Server/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies successfully to clean database
- [ ] Migration rollback restores previous schema state
- [ ] Foreign key constraints enforced (invalid patient_id rejected)
- [ ] Unique constraint on (patient_id, version_number) prevents duplicate versions
- [ ] JSONB columns accept and return valid JSON arrays/objects

## Implementation Checklist

- [x] Define `PatientProfileVersion` entity class with all columns, FK navigation properties, and JSONB-typed properties
- [x] Create `ConsolidationType` enum (Initial, Incremental)
- [x] Register entity in `PatientDbContext.OnModelCreating` with FK constraints, indexes, and JSONB column configuration
- [x] Generate and verify EF Core migration script with up/down methods
- [ ] Apply migration to development database and validate schema
- [ ] Create seed data with sample version entries spanning multiple patients
