# Task - TASK_001

## Requirement Reference

- User Story: US_047
- Story Location: .propel/context/tasks/EP-008/us_047/us_047.md
- Acceptance Criteria:
  - AC-3: **Given** the ICD-10 code library is maintained, **When** a quarterly update is applied, **Then** the system refreshes the code library and revalidates any pending codes against the updated library.
  - AC-4: **Given** multiple ICD-10 codes apply to a single diagnosis, **When** the AI identifies this, **Then** the system presents all applicable codes ranked by relevance.
- Edge Case:
  - How does the system handle deprecated ICD-10 codes after a library update? System flags existing records using deprecated codes and suggests replacement codes for staff review.

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

Create the ICD-10 code reference library database schema, versioning infrastructure, and migration scripts to support AI-driven diagnosis-to-code mapping. This task establishes the `icd10_code_library` reference table for validating AI-generated codes (DR-015, FR-063), adds library versioning columns for quarterly refresh cycles, and extends the existing `MedicalCode` entity with ranking and revalidation support for multiple applicable codes per diagnosis (AC-4). It also provides the deprecation tracking mechanism needed when library updates retire codes (edge case).

## Dependent Tasks

- US_008 - Foundational - Requires MedicalCode entity to exist before extending it.

## Impacted Components

- **NEW** `Icd10CodeLibrary` entity — Reference table for ICD-10 code validation
- **MODIFY** `MedicalCode` entity — Add `relevance_rank`, `revalidation_status`, `library_version` columns
- **NEW** EF Core migration — Schema creation and seed structure
- **NEW** Database indexes — Performance indexes on code lookup columns

## Implementation Plan

1. **Define `Icd10CodeLibrary` entity** with fields: `library_entry_id` (UUID PK), `code_value` (string, indexed), `description` (text), `category` (string), `effective_date` (date), `deprecated_date` (date, nullable), `replacement_code` (string, nullable), `library_version` (string), `is_current` (boolean, default true), `created_at` (timestamp), `updated_at` (timestamp).
2. **Add composite index** on (`code_value`, `is_current`) for fast active-code lookups used by the coding validation service (DR-015).
3. **Add index** on `category` for category-filtered queries during RAG retrieval context building.
4. **Extend `MedicalCode` entity** with: `relevance_rank` (int, nullable) to support ranked multi-code results per AC-4, `revalidation_status` (enum: valid, pending_review, deprecated_replaced, nullable) for quarterly revalidation per AC-3, `library_version` (string, nullable) to track which library version the code was validated against.
5. **Create EF Core migration** with transactional rollback support per DR-029. Include `Up()` and `Down()` methods for full reversibility.
6. **Add unique constraint** on (`code_value`, `library_version`) in `icd10_code_library` to prevent duplicate entries per version.
7. **Add foreign key index** on `MedicalCode.patient_id` if not already present (performance for patient-scoped queries).

## Current Project State

```text
[Placeholder — to be updated based on dependent task US_008 completion]
Server/
├── Models/
│   └── MedicalCode.cs          # Existing entity to extend
├── Data/
│   └── AppDbContext.cs          # EF Core context to update
└── Migrations/                  # New migration to add
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/Icd10CodeLibrary.cs | New entity for ICD-10 code reference library |
| MODIFY | Server/Models/MedicalCode.cs | Add `relevance_rank`, `revalidation_status`, `library_version` columns |
| MODIFY | Server/Data/AppDbContext.cs | Register `Icd10CodeLibrary` DbSet, add Fluent API configuration for indexes and constraints |
| CREATE | Server/Migrations/{timestamp}_AddIcd10CodeLibraryAndExtendMedicalCode.cs | EF Core migration with Up/Down methods |

## External References

- [PostgreSQL 16 CREATE INDEX documentation](https://www.postgresql.org/docs/16/sql-createindex.html)
- [EF Core 8 Migrations documentation](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/)
- [EF Core 8 Indexes documentation](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)

## Build Commands

- `dotnet ef migrations add AddIcd10CodeLibraryAndExtendMedicalCode --project Server`
- `dotnet ef database update --project Server`
- `dotnet build Server`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies cleanly with `dotnet ef database update`
- [ ] Migration rollback succeeds with `dotnet ef database update {previous_migration}`
- [ ] Composite index on (`code_value`, `is_current`) verified via `\d+ icd10_code_library` in psql
- [ ] Unique constraint on (`code_value`, `library_version`) verified
- [ ] `MedicalCode` extended columns accept null for backward compatibility
- [ ] Foreign key relationships enforce referential integrity (DR-009)

## Implementation Checklist

- [ ] Create `Icd10CodeLibrary` entity class with all fields, data annotations, and XML doc comments
- [ ] Add composite index on (`code_value`, `is_current`) via Fluent API in `AppDbContext`
- [ ] Add unique constraint on (`code_value`, `library_version`) via Fluent API
- [ ] Extend `MedicalCode` entity with `relevance_rank`, `revalidation_status`, `library_version` nullable columns
- [ ] Register `DbSet<Icd10CodeLibrary>` in `AppDbContext` and configure relationships
- [ ] Generate EF Core migration with transactional `Up()` and `Down()` methods (DR-029)
- [ ] Verify migration applies and rolls back cleanly against PostgreSQL 16.x
