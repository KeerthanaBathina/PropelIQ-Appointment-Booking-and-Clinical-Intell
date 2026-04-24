# Task - task_003_db_cpt_code_library

## Requirement Reference

- User Story: US_048
- Story Location: .propel/context/tasks/EP-008/us_048/us_048.md
- Acceptance Criteria:
  - AC-4: Given the CPT code library is maintained, When a quarterly update is applied, Then the system refreshes the code library and revalidates pending codes.
- Edge Case:
  - Bundled procedures: System identifies bundling opportunities and presents the bundled code option alongside individual codes.

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

Create the CPT code library database schema and seed data required for AI-generated CPT code validation (AIR-S02) and quarterly library maintenance (AC-4). This includes the `cpt_code_library` table storing the authoritative set of valid CPT codes, a `cpt_bundle_rules` table capturing bundled procedure relationships, and the EF Core migration scripts with rollback support. Seed data covers the most common procedure codes to enable development and testing.

## Dependent Tasks

- US_008 tasks (EP-DATA) — MedicalCode entity migration must be applied first (foreign key dependencies)

## Impacted Components

- **NEW** `CptCodeLibrary` entity — EF Core entity for CPT code reference data
- **NEW** `CptBundleRule` entity — EF Core entity for bundled procedure code relationships
- **NEW** `CptCodeLibraryConfiguration` — EF Core entity type configuration (fluent API)
- **NEW** `CptBundleRuleConfiguration` — EF Core entity type configuration (fluent API)
- **NEW** Migration: `AddCptCodeLibraryAndBundleRules` — EF Core migration script
- **MODIFY** `AppDbContext` — Register CptCodeLibrary and CptBundleRule DbSets

## Implementation Plan

1. **Create `CptCodeLibrary` entity** with columns: `code_id` (UUID, PK), `cpt_code` (varchar(10), unique, not null), `description` (text, not null), `category` (varchar(50) — e.g., "Evaluation & Management", "Surgery", "Radiology", "Pathology", "Medicine"), `effective_date` (date, not null), `expiration_date` (date, nullable — null means currently active), `is_active` (boolean, default true), `created_at` (timestamp), `updated_at` (timestamp). Add unique index on `cpt_code` for fast lookup. Add index on `(category, is_active)` for filtered queries. Add index on `is_active` for active code filtering.
2. **Create `CptBundleRule` entity** for bundled procedure relationships with columns: `bundle_id` (UUID, PK), `bundle_cpt_code` (varchar(10), FK to cpt_code_library.cpt_code), `component_cpt_code` (varchar(10), FK to cpt_code_library.cpt_code), `bundle_description` (text), `is_active` (boolean, default true), `created_at` (timestamp). Add unique constraint on `(bundle_cpt_code, component_cpt_code)` to prevent duplicate rules.
3. **Create EF Core migration** `AddCptCodeLibraryAndBundleRules` encapsulated in a transaction block with automatic rollback on failure per DR-029. Migration creates both tables, indexes, and constraints. Include `Down()` method for clean rollback.
4. **Add seed data** for common CPT procedure codes covering key categories: Evaluation & Management (99201-99215), Laboratory/Pathology (80047-80076, 83036), Radiology (70553, 71046), Surgery (10060-10180), and Medicine (90471-90474). Include 50-75 commonly used codes to enable development. Seed 5-10 bundle rules for common bundled procedures (e.g., surgical packages).

## Current Project State

- No database schema exists yet (green-field). PostgreSQL 16.x instance to be provisioned by foundational tasks (EP-TECH).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Data/Entities/CptCodeLibrary.cs | CptCodeLibrary entity class with all columns |
| CREATE | Server/Data/Entities/CptBundleRule.cs | CptBundleRule entity for bundled procedure relationships |
| CREATE | Server/Data/Configurations/CptCodeLibraryConfiguration.cs | EF Core fluent API configuration (indexes, constraints) |
| CREATE | Server/Data/Configurations/CptBundleRuleConfiguration.cs | EF Core fluent API configuration (unique constraints, FKs) |
| CREATE | Server/Data/Migrations/YYYYMMDD_AddCptCodeLibraryAndBundleRules.cs | EF Core migration with Up/Down methods |
| CREATE | Server/Data/Seeds/CptCodeLibrarySeed.cs | Seed data for 50-75 common CPT codes and 5-10 bundle rules |
| MODIFY | Server/Data/AppDbContext.cs | Add DbSet<CptCodeLibrary> and DbSet<CptBundleRule> properties |

## External References

- [EF Core Migrations (v8)](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [EF Core Data Seeding (v8)](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding)
- [CPT Code Categories — AMA](https://www.ama-assn.org/practice-management/cpt/cpt-overview-and-code-approval)
- [PostgreSQL Index Types](https://www.postgresql.org/docs/16/indexes.html)

## Build Commands

- Refer to applicable technology stack specific build commands in `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies cleanly on empty database
- [ ] Migration rollback (`Down()`) removes tables without errors
- [ ] Unique constraint on `cpt_code` prevents duplicate code insertion
- [ ] Unique constraint on `(bundle_cpt_code, component_cpt_code)` prevents duplicate bundle rules
- [ ] Seed data populates 50-75 CPT codes across all major categories
- [ ] Indexes verified via `EXPLAIN ANALYZE` on typical queries (lookup by cpt_code, filter by category + is_active)

## Implementation Checklist

- [x] Create `CptCodeLibrary` entity with code_id, cpt_code, description, category, effective_date, expiration_date, is_active, timestamps; add unique index on cpt_code and composite index on (category, is_active)
- [x] Create `CptBundleRule` entity with bundle_rule_id, bundle_cpt_code, component_cpt_code, bundle_description, is_active, created_at; add unique constraint on (bundle_cpt_code, component_cpt_code)
- [x] Create EF Core migration `AddCptCodeLibraryAndBundleRules` with Up/Down methods — adds cpt_code_library table, cpt_bundle_rules table, and IsBundled/BundleGroupId columns to medical_codes; Down() performs clean rollback (DR-029)
- [x] Add seed data script with 62 common CPT codes across E&M, Preventive Medicine, Lab, Radiology, Surgery, and Medicine categories and 10 bundle rules (9 active + 1 inactive); idempotent via ON CONFLICT clauses
