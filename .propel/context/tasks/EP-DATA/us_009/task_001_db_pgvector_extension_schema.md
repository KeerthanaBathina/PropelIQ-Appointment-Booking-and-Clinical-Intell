# Task - task_001_db_pgvector_extension_schema

## Requirement Reference

- User Story: us_009
- Story Location: .propel/context/tasks/EP-DATA/us_009/us_009.md
- Acceptance Criteria:
  - AC-1: Given PostgreSQL 16 is running, When the migration runs `CREATE EXTENSION vector`, Then pgvector 0.5.x is enabled without errors.
  - AC-2: Given vector columns are configured, When a 384-dimension embedding is inserted, Then the data is stored and retrievable via cosine similarity operator (`<=>`) with correct distance calculation.
  - AC-3: Given separate vector indexes exist, When a similarity search is executed, Then it uses IVFFlat or HNSW index for sub-second query performance at up to 100K vectors.
- Edge Case:
  - What happens when an embedding with wrong dimensions is inserted? Database rejects with dimension mismatch error; API returns 400 Bad Request.
  - How does the system handle index rebuild after bulk insert? Maintenance script rebuilds indexes during low-traffic window (2-4 AM).

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
| Database | pgvector extension | 0.5.x |
| Backend | EF Core (migration host) | 8.x |
| Backend | Npgsql.EntityFrameworkCore.PostgreSQL | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task creates the database schema for vector storage. AI integration (embedding generation, RAG retrieval) is handled by downstream AI-layer tasks.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Install the pgvector 0.5.x extension on PostgreSQL 16, create three vector embedding tables per AIR-R04 (medical_terminology_embeddings, intake_template_embeddings, coding_guideline_embeddings) with 384-dimension `vector(384)` columns, and create separate IVFFlat indexes for each table optimized for cosine similarity search on up to 100K vectors. Add a full-text search (`tsvector`) column on each table to support hybrid search (AC-4). Include a SQL provisioning script for the extension and schema, plus a maintenance script for index rebuilding during low-traffic windows. Define EF Core entity models for the embedding tables to enable code-first migration support.

## Dependent Tasks

- US_003 task_001_db_postgresql_provisioning — PostgreSQL 16 must be provisioned with the `upacip` database and `upacip_app` role.
- US_003 task_002_be_efcore_integration — EF Core + Npgsql must be configured with `ApplicationDbContext`.

## Impacted Components

- **NEW** `scripts/provision-pgvector.sql` — SQL script: CREATE EXTENSION vector, CREATE 3 embedding tables with vector(384) columns, tsvector columns, IVFFlat indexes, CHECK constraints
- **NEW** `scripts/rebuild-vector-indexes.ps1` — PowerShell maintenance script for index rebuild during 2-4 AM window
- **NEW** `src/UPACIP.DataAccess/Entities/MedicalTerminologyEmbedding.cs` — Entity for medical_terminology_embeddings table
- **NEW** `src/UPACIP.DataAccess/Entities/IntakeTemplateEmbedding.cs` — Entity for intake_template_embeddings table
- **NEW** `src/UPACIP.DataAccess/Entities/CodingGuidelineEmbedding.cs` — Entity for coding_guideline_embeddings table
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Register embedding DbSets, enable pgvector with `UseVector()`
- **MODIFY** `scripts/provision-database.sql` — Add pgvector extension install step

## Implementation Plan

1. **Install pgvector 0.5.x extension on PostgreSQL**: Create `scripts/provision-pgvector.sql` that runs `CREATE EXTENSION IF NOT EXISTS vector;`. Verify the extension is active by querying `SELECT extversion FROM pg_extension WHERE extname = 'vector';` — must return `0.5.x`. This script must be executed by a database superuser (the `postgres` role) since extension installation requires elevated privileges. The `upacip_app` role receives USAGE grants on the vector type.

2. **Create medical_terminology_embeddings table**: Define the table with columns: `id` (UUID PK, gen_random_uuid()), `term` (VARCHAR(512) NOT NULL), `description` (TEXT), `source` (VARCHAR(100) — e.g., "ICD-10", "CPT", "SNOMED"), `embedding` (vector(384) NOT NULL), `content_tsv` (tsvector — generated from term + description for FTS), `created_at` (TIMESTAMPTZ DEFAULT NOW()), `updated_at` (TIMESTAMPTZ). Add a CHECK constraint: `array_length(embedding::float[], 1) = 384` (pgvector enforces dimension via type, but explicit check documents intent). Add a GIN index on `content_tsv` for full-text search.

3. **Create intake_template_embeddings table**: Define with columns: `id` (UUID PK), `template_name` (VARCHAR(256) NOT NULL), `section` (VARCHAR(256)), `content` (TEXT NOT NULL), `embedding` (vector(384) NOT NULL), `content_tsv` (tsvector), `created_at`, `updated_at`. Add GIN index on `content_tsv`. This stores chunked intake flow templates for RAG retrieval during conversational intake.

4. **Create coding_guideline_embeddings table**: Define with columns: `id` (UUID PK), `code_system` (VARCHAR(20) NOT NULL — "ICD-10" or "CPT"), `code_value` (VARCHAR(20)), `guideline_text` (TEXT NOT NULL), `embedding` (vector(384) NOT NULL), `content_tsv` (tsvector), `created_at`, `updated_at`. Add GIN index on `content_tsv`. This stores coding guidelines for AI medical coding assistance.

5. **Create IVFFlat indexes for cosine similarity**: For each of the 3 tables, create an IVFFlat index on the `embedding` column using cosine distance operator class: `CREATE INDEX idx_<table>_embedding ON <table> USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100);`. IVFFlat with `lists = 100` is appropriate for up to 100K vectors (sqrt of expected rows). The cosine operator class maps to the `<=>` operator for similarity queries.

6. **Create EF Core entity models for embedding tables**: Define `MedicalTerminologyEmbedding`, `IntakeTemplateEmbedding`, `CodingGuidelineEmbedding` entity classes in `src/UPACIP.DataAccess/Entities/`. Use `Pgvector.Vector` type for the `Embedding` property (requires `Pgvector` NuGet package). Use `NpgsqlTsVector` type for the `ContentTsv` property. Register `DbSet<T>` properties on `ApplicationDbContext` and configure `UseVector()` in the Npgsql options.

7. **Create index rebuild maintenance script**: Write `scripts/rebuild-vector-indexes.ps1` that: (a) checks current time is within 2-4 AM window, (b) drops and recreates IVFFlat indexes with `CONCURRENTLY` option for all 3 tables, (c) runs `VACUUM ANALYZE` on each table, (d) logs results. This script is intended to be scheduled via Windows Task Scheduler after bulk embedding inserts.

8. **Grant table permissions to `upacip_app` role**: In the provisioning script, grant `SELECT, INSERT, UPDATE, DELETE` on all 3 embedding tables to `upacip_app`. Grant `USAGE` on the vector type. This follows least-privilege principle from the existing database provisioning pattern (US_003).

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
│       ├── Entities/
│       │   ├── BaseEntity.cs
│       │   ├── Patient.cs
│       │   └── ... (10 domain entities)
│       ├── Configurations/
│       └── Enums/
├── app/
└── scripts/
    ├── provision-database.ps1
    ├── provision-database.sql
    └── ...
```

> Assumes US_003 (PostgreSQL provisioning + EF Core) and US_008 (domain entities) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | scripts/provision-pgvector.sql | CREATE EXTENSION vector; CREATE 3 embedding tables with vector(384), tsvector, IVFFlat indexes, GIN FTS indexes, CHECK constraints, GRANT permissions |
| CREATE | scripts/rebuild-vector-indexes.ps1 | PowerShell script for scheduled IVFFlat index rebuild (REINDEX CONCURRENTLY) during 2-4 AM low-traffic window |
| CREATE | src/UPACIP.DataAccess/Entities/MedicalTerminologyEmbedding.cs | Entity with Id, Term, Description, Source, Embedding (Vector), ContentTsv (NpgsqlTsVector) |
| CREATE | src/UPACIP.DataAccess/Entities/IntakeTemplateEmbedding.cs | Entity with Id, TemplateName, Section, Content, Embedding (Vector), ContentTsv |
| CREATE | src/UPACIP.DataAccess/Entities/CodingGuidelineEmbedding.cs | Entity with Id, CodeSystem, CodeValue, GuidelineText, Embedding (Vector), ContentTsv |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add 3 embedding DbSet<T> properties, configure UseVector() in Npgsql options |
| MODIFY | scripts/provision-database.sql | Add prerequisite step to install pgvector extension before schema creation |

## External References

- [pgvector GitHub — Installation & Usage](https://github.com/pgvector/pgvector)
- [pgvector IVFFlat Indexing](https://github.com/pgvector/pgvector#ivfflat)
- [pgvector HNSW Indexing](https://github.com/pgvector/pgvector#hnsw)
- [pgvector Cosine Distance Operator](https://github.com/pgvector/pgvector#distances)
- [Npgsql pgvector Support](https://www.npgsql.org/doc/types/pgvector.html)
- [PostgreSQL Full-Text Search](https://www.postgresql.org/docs/16/textsearch.html)
- [Pgvector NuGet Package](https://www.nuget.org/packages/Pgvector)

## Build Commands

```powershell
# Install pgvector extension (run as postgres superuser)
psql -U postgres -d upacip -f scripts/provision-pgvector.sql

# Verify extension
psql -U postgres -d upacip -c "SELECT extversion FROM pg_extension WHERE extname = 'vector';"

# Build DataAccess project
dotnet build src/UPACIP.DataAccess/UPACIP.DataAccess.csproj

# Test vector insertion (384 dimensions)
psql -U upacip_app -d upacip -c "INSERT INTO medical_terminology_embeddings (id, term, embedding) VALUES (gen_random_uuid(), 'hypertension', '[0.1,0.2,...384 values...]'::vector(384));"

# Test cosine similarity query
psql -U upacip_app -d upacip -c "SELECT term, 1 - (embedding <=> '[query_vector]'::vector(384)) AS similarity FROM medical_terminology_embeddings ORDER BY embedding <=> '[query_vector]'::vector(384) LIMIT 5;"

# Verify IVFFlat index usage
psql -U upacip_app -d upacip -c "EXPLAIN ANALYZE SELECT * FROM medical_terminology_embeddings ORDER BY embedding <=> '[query_vector]'::vector(384) LIMIT 5;"

# Rebuild indexes (run during maintenance window)
.\scripts\rebuild-vector-indexes.ps1
```

## Implementation Validation Strategy

- [ ] `SELECT extversion FROM pg_extension WHERE extname = 'vector'` returns `0.5.x` — **PENDING: pgvector OS-level library not installed; see `scripts/provision-pgvector.sql` prerequisites**
- [ ] All 3 embedding tables exist with `vector(384)` column and `tsvector` column — **PENDING: requires pgvector extension install**
- [ ] Inserting a 384-dimension vector succeeds; inserting a wrong-dimension vector fails with dimension mismatch error — **PENDING: requires pgvector extension install**
- [ ] IVFFlat indexes exist on all 3 tables using `vector_cosine_ops` operator class — **PENDING: requires pgvector extension install**
- [ ] `EXPLAIN ANALYZE` confirms index scan (not sequential scan) for cosine similarity queries — **PENDING: requires pgvector extension install**
- [ ] GIN indexes exist on `content_tsv` columns for full-text search — **PENDING: requires pgvector extension install**
- [ ] `upacip_app` role has SELECT/INSERT/UPDATE/DELETE on all 3 embedding tables — **PENDING: requires pgvector extension install**
- [x] `dotnet build` succeeds with embedding entity models and `UseVector()` configuration — **CONFIRMED: 0 errors, 0 warnings**

## Implementation Checklist

- [x] Create `scripts/provision-pgvector.sql` with `CREATE EXTENSION IF NOT EXISTS vector`, 3 embedding tables (`medical_terminology_embeddings`, `intake_template_embeddings`, `coding_guideline_embeddings`) with `vector(384)` and `tsvector` columns
- [x] Add IVFFlat indexes on each embedding table using `vector_cosine_ops` with `lists = 100` for cosine similarity search
- [x] Add GIN indexes on `content_tsv` columns for PostgreSQL full-text search support
- [x] Grant `SELECT, INSERT, UPDATE, DELETE` on all 3 embedding tables to `upacip_app` role
- [x] Create EF Core entity models (`MedicalTerminologyEmbedding`, `IntakeTemplateEmbedding`, `CodingGuidelineEmbedding`) with `Pgvector.Vector` and `NpgsqlTsVector` property types, register DbSets on `ApplicationDbContext`
- [x] Configure `UseVector()` in `ApplicationDbContext` Npgsql options to enable pgvector type mapping
- [x] Create `scripts/rebuild-vector-indexes.ps1` that drops and recreates IVFFlat indexes with `CONCURRENTLY`, runs `VACUUM ANALYZE`, and validates execution is within 2-4 AM maintenance window
