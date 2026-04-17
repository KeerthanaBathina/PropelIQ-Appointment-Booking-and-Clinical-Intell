# Task - task_003_db_patient_search_indexes

## Requirement Reference

- User Story: US_062
- Story Location: .propel/context/tasks/EP-010/us_062/us_062.md
- Acceptance Criteria:
    - AC-1: **Given** the staff member opens the patient search, **When** they enter a name, DOB, or phone number, **Then** the system returns matching results within 1 second with relevance ranking.
- Edge Cases:
    - Partial name search: Database-level trigram indexing enables partial matching (e.g., "Joh" matches "John," "Johnston") with similarity scoring.

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
| Backend | N/A | - |
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
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

Create database indexes and enable the `pg_trgm` extension to support performant patient search by name, date of birth, and phone number. This task ensures the search endpoint (AC-1) returns results within 1 second by adding trigram GIN indexes for partial name matching, B-tree indexes on phone_number and date_of_birth for exact lookups, and a composite index for multi-criteria search optimization. All changes are delivered as EF Core migration scripts with rollback support per DR-028 and DR-029.

## Dependent Tasks

- US_008 - Foundational - Requires Patient entity and table to exist (migration must be applied)

## Impacted Components

- **MODIFY** PostgreSQL database — Enable `pg_trgm` extension, add indexes on `patients` table
- **NEW** EF Core migration — Migration script for index creation with rollback

## Implementation Plan

1. **Enable pg_trgm Extension**: Create an EF Core migration that enables the `pg_trgm` PostgreSQL extension using `CREATE EXTENSION IF NOT EXISTS pg_trgm;`. This extension provides trigram-based similarity functions (`similarity()`, `%` operator) and GIN index support for fuzzy text matching. Verify the extension is available in PostgreSQL 16.x.

2. **GIN Trigram Index on full_name**: Create a GIN index using `gin_trgm_ops` on the `patients.full_name` column. This enables efficient partial name matching with the `%` (similarity) operator and `ILIKE` patterns. Query example: `SELECT * FROM patients WHERE full_name % 'Joh' ORDER BY similarity(full_name, 'Joh') DESC`.

3. **B-tree Index on phone_number**: Create a standard B-tree index on `patients.phone_number` for exact and prefix phone number lookups. Supports queries like `WHERE phone_number = '(555) 123-4567'`.

4. **B-tree Index on date_of_birth**: Create a standard B-tree index on `patients.date_of_birth` for exact DOB match queries. Supports queries like `WHERE date_of_birth = '1985-03-22'`.

5. **Partial Index for Active Patients**: Create a partial B-tree index on `patients.full_name` filtered by `WHERE deleted_at IS NULL` to optimize searches that exclude soft-deleted records (DR-021). This ensures only active patient records are indexed, reducing index size and improving scan performance.

6. **Migration Script with Rollback**: Implement the migration using EF Core's `Up()` and `Down()` methods. The `Down()` method must drop all created indexes and the `pg_trgm` extension in reverse order. Execute migration within a transaction block per DR-029 for automatic rollback on failure.

## Current Project State

- [Placeholder — to be updated based on completion of dependent task US_008]

```text
Server/
├── Data/
│   ├── Migrations/
│   └── AppDbContext.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Data/Migrations/{timestamp}_AddPatientSearchIndexes.cs | EF Core migration: enable pg_trgm, create GIN trigram index on full_name, B-tree indexes on phone_number and date_of_birth, partial index for active patients |

## External References

- [PostgreSQL 16 pg_trgm Extension](https://www.postgresql.org/docs/16/pgtrgm.html)
- [PostgreSQL 16 GIN Indexes](https://www.postgresql.org/docs/16/gin.html)
- [PostgreSQL 16 Partial Indexes](https://www.postgresql.org/docs/16/indexes-partial.html)
- [EF Core 8 Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [Npgsql EF Core PostgreSQL Extensions](https://www.npgsql.org/efcore/mapping/full-text-search.html)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Migration applies successfully without errors
- [ ] Migration rollback (`Down()`) executes cleanly, removing all indexes and extension
- [ ] `pg_trgm` extension is active: `SELECT * FROM pg_extension WHERE extname = 'pg_trgm';`
- [ ] GIN index exists on `patients.full_name`: verify via `\di` or `pg_indexes` system view
- [ ] B-tree index exists on `patients.phone_number`
- [ ] B-tree index exists on `patients.date_of_birth`
- [ ] Partial index on `patients.full_name` with `WHERE deleted_at IS NULL` filter confirmed
- [ ] Query plan for name search uses GIN index: `EXPLAIN ANALYZE SELECT * FROM patients WHERE full_name % 'test';`

## Implementation Checklist

- [ ] Create EF Core migration enabling `pg_trgm` extension (`CREATE EXTENSION IF NOT EXISTS pg_trgm`)
- [ ] Add GIN trigram index on `patients.full_name` using `gin_trgm_ops` operator class
- [ ] Add B-tree index on `patients.phone_number` for exact phone lookups
- [ ] Add B-tree index on `patients.date_of_birth` for exact DOB lookups
- [ ] Add partial B-tree index on `patients.full_name` filtered by `WHERE deleted_at IS NULL` for active-only searches
- [ ] Implement `Down()` rollback method dropping all indexes and extension in reverse order
