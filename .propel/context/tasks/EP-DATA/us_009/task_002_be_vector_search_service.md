# Task - task_002_be_vector_search_service

## Requirement Reference

- User Story: us_009
- Story Location: .propel/context/tasks/EP-DATA/us_009/us_009.md
- Acceptance Criteria:
  - AC-2: Given vector columns are configured, When a 384-dimension embedding is inserted, Then the data is stored and retrievable via cosine similarity operator (`<=>`) with correct distance calculation.
  - AC-3: Given separate vector indexes exist, When a similarity search is executed, Then it uses IVFFlat or HNSW index for sub-second query performance at up to 100K vectors.
  - AC-4: Given the vector store is ready, When a hybrid search combines `<=>` with PostgreSQL full-text search (FTS), Then both results are merged and ranked correctly.
- Edge Case:
  - What happens when an embedding with wrong dimensions is inserted? Database rejects with dimension mismatch error; API returns 400 Bad Request.

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
| Library | Pgvector (NuGet) | 0.2.x |
| Database | PostgreSQL + pgvector | 16.x / 0.5.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements the data access layer for vector search. AI embedding generation and RAG pipeline orchestration are handled by downstream AI-layer tasks.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement a backend vector search service layer (`IVectorSearchService` / `VectorSearchService`) in the UPACIP.Service project that provides cosine similarity search against pgvector embedding tables, hybrid search combining cosine similarity with PostgreSQL full-text search (FTS), and CRUD operations for vector embeddings. The service uses raw SQL via Npgsql for pgvector-specific operators (`<=>` cosine distance) while leveraging EF Core for standard CRUD. Hybrid search per AIR-R06 merges vector similarity scores with FTS relevance ranks using Reciprocal Rank Fusion (RRF) scoring. Input validation rejects non-384-dimension embeddings with descriptive error messages.

## Dependent Tasks

- task_001_db_pgvector_extension_schema — pgvector extension, embedding tables, IVFFlat indexes, and tsvector columns must exist.
- US_001 task_001_be_solution_scaffold — Backend solution structure with Service project must exist.

## Impacted Components

- **NEW** `src/UPACIP.Service/VectorSearch/IVectorSearchService.cs` — Interface defining SearchSimilarAsync, HybridSearchAsync, UpsertEmbeddingAsync, DeleteEmbeddingAsync
- **NEW** `src/UPACIP.Service/VectorSearch/VectorSearchService.cs` — Implementation using raw Npgsql SQL for vector operations and EF Core for CRUD
- **NEW** `src/UPACIP.Service/VectorSearch/VectorSearchResult.cs` — DTO with Id, Content, Similarity score, FtsRank, CombinedScore
- **NEW** `src/UPACIP.Service/VectorSearch/EmbeddingCategory.cs` — Enum: MedicalTerminology, IntakeTemplate, CodingGuideline
- **NEW** `src/UPACIP.Service/VectorSearch/HybridSearchRequest.cs` — Request DTO with query embedding, text query, top-k, similarity threshold
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IVectorSearchService in DI container

## Implementation Plan

1. **Define `IVectorSearchService` interface**: Create the interface with four methods:
   - `Task<IReadOnlyList<VectorSearchResult>> SearchSimilarAsync(EmbeddingCategory category, float[] queryEmbedding, int topK = 5, float similarityThreshold = 0.75f)` — Pure cosine similarity search per AIR-R02.
   - `Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(EmbeddingCategory category, float[] queryEmbedding, string textQuery, int topK = 5, float similarityThreshold = 0.75f)` — Combined vector + FTS search per AIR-R06.
   - `Task UpsertEmbeddingAsync(EmbeddingCategory category, Guid id, string content, float[] embedding, Dictionary<string, string>? metadata = null)` — Insert or update an embedding.
   - `Task DeleteEmbeddingAsync(EmbeddingCategory category, Guid id)` — Remove an embedding by ID.

2. **Implement input validation**: Before executing any query, validate that `queryEmbedding.Length == 384`. If not, throw `ArgumentException("Embedding must have exactly 384 dimensions")`. The caller (API controller) translates this to 400 Bad Request. Also validate `topK` is between 1 and 100, and `similarityThreshold` is between 0.0 and 1.0.

3. **Implement `SearchSimilarAsync` with raw SQL**: Use `NpgsqlConnection` from the DI-injected `IDbContextFactory<ApplicationDbContext>` to execute parameterized SQL:
   ```sql
   SELECT id, term AS content, 1 - (embedding <=> @queryVector::vector(384)) AS similarity
   FROM medical_terminology_embeddings
   WHERE 1 - (embedding <=> @queryVector::vector(384)) >= @threshold
   ORDER BY embedding <=> @queryVector::vector(384)
   LIMIT @topK;
   ```
   Map `EmbeddingCategory` enum to the correct table name via a private helper method (prevents SQL injection by using a whitelist). Pass the query embedding as `new Pgvector.Vector(queryEmbedding)` parameter.

4. **Implement `HybridSearchAsync` with Reciprocal Rank Fusion**: Execute two separate ranked queries — cosine similarity and FTS — then merge using RRF scoring. The SQL for hybrid search uses a CTE pattern:
   ```sql
   WITH vector_results AS (
     SELECT id, content, 1 - (embedding <=> @queryVector::vector(384)) AS similarity,
            ROW_NUMBER() OVER (ORDER BY embedding <=> @queryVector::vector(384)) AS vec_rank
     FROM {table}
     WHERE 1 - (embedding <=> @queryVector::vector(384)) >= @threshold
     LIMIT @topK * 2
   ),
   fts_results AS (
     SELECT id, content, ts_rank(content_tsv, plainto_tsquery(@textQuery)) AS fts_rank,
            ROW_NUMBER() OVER (ORDER BY ts_rank(content_tsv, plainto_tsquery(@textQuery)) DESC) AS text_rank
     FROM {table}
     WHERE content_tsv @@ plainto_tsquery(@textQuery)
     LIMIT @topK * 2
   )
   SELECT COALESCE(v.id, f.id) AS id, COALESCE(v.content, f.content) AS content,
          v.similarity, f.fts_rank,
          (1.0 / (60 + COALESCE(v.vec_rank, 1000))) + (1.0 / (60 + COALESCE(f.text_rank, 1000))) AS combined_score
   FROM vector_results v
   FULL OUTER JOIN fts_results f ON v.id = f.id
   ORDER BY combined_score DESC
   LIMIT @topK;
   ```
   RRF constant of 60 balances between vector and FTS results per standard practice.

5. **Implement `UpsertEmbeddingAsync`**: Use parameterized SQL with `INSERT ... ON CONFLICT (id) DO UPDATE` to handle both inserts and updates. Update the `content_tsv` column using `to_tsvector('english', @content)`. Validate embedding dimensions before executing. Map metadata dictionary to the appropriate columns based on `EmbeddingCategory`.

6. **Implement table name resolution helper**: Create a private method `GetTableName(EmbeddingCategory category)` that returns the table name string from a whitelist dictionary:
   - `MedicalTerminology` → `"medical_terminology_embeddings"`
   - `IntakeTemplate` → `"intake_template_embeddings"`
   - `CodingGuideline` → `"coding_guideline_embeddings"`
   This prevents SQL injection by never interpolating user input into table names.

7. **Register service in DI**: In `Program.cs`, register `builder.Services.AddScoped<IVectorSearchService, VectorSearchService>()`. The service depends on `ApplicationDbContext` (or `IDbContextFactory<ApplicationDbContext>`) for database access.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   └── Caching/
│   │       ├── ICacheService.cs
│   │       └── RedisCacheService.cs
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── MedicalTerminologyEmbedding.cs
│       │   ├── IntakeTemplateEmbedding.cs
│       │   ├── CodingGuidelineEmbedding.cs
│       │   └── ...
│       └── Configurations/
├── app/
└── scripts/
    ├── provision-pgvector.sql
    └── rebuild-vector-indexes.ps1
```

> Assumes US_001, US_003, and task_001_db_pgvector_extension_schema are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/VectorSearch/IVectorSearchService.cs | Interface with SearchSimilarAsync, HybridSearchAsync, UpsertEmbeddingAsync, DeleteEmbeddingAsync |
| CREATE | src/UPACIP.Service/VectorSearch/VectorSearchService.cs | Implementation using raw Npgsql SQL for `<=>` cosine queries, RRF hybrid search, parameterized table name resolution |
| CREATE | src/UPACIP.Service/VectorSearch/VectorSearchResult.cs | DTO: Id, Content, Similarity, FtsRank, CombinedScore |
| CREATE | src/UPACIP.Service/VectorSearch/EmbeddingCategory.cs | Enum: MedicalTerminology, IntakeTemplate, CodingGuideline |
| CREATE | src/UPACIP.Service/VectorSearch/HybridSearchRequest.cs | Request DTO: QueryEmbedding, TextQuery, TopK, SimilarityThreshold |
| MODIFY | src/UPACIP.Api/Program.cs | Register `IVectorSearchService` → `VectorSearchService` in DI |

## External References

- [pgvector Cosine Distance Operator `<=>`](https://github.com/pgvector/pgvector#distances)
- [pgvector IVFFlat Index Tuning](https://github.com/pgvector/pgvector#ivfflat)
- [PostgreSQL Full-Text Search — ts_rank](https://www.postgresql.org/docs/16/textsearch-controls.html#TEXTSEARCH-RANKING)
- [Reciprocal Rank Fusion (RRF) — Hybrid Search Pattern](https://plg.uwaterloo.ca/~gvcormac/cormacksigir09-rrf.pdf)
- [Npgsql pgvector Support](https://www.npgsql.org/doc/types/pgvector.html)
- [Pgvector .NET NuGet Package](https://www.nuget.org/packages/Pgvector)
- [EF Core Raw SQL Queries](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln

# Run API and test vector search (requires seeded embeddings)
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj

# Verify cosine similarity search returns results ranked by similarity
# (Programmatic test via integration test or manual API call)

# Verify hybrid search merges vector + FTS results
# (Programmatic test via integration test or manual API call)
```

## Implementation Validation Strategy

- [x] `dotnet build` completes with zero errors for UPACIP.Service project — **CONFIRMED: 0 errors, 0 warnings**
- [ ] `SearchSimilarAsync` returns top-K results ranked by cosine similarity descending — **PENDING: requires pgvector extension installed on PostgreSQL**
- [ ] `SearchSimilarAsync` respects the similarity threshold (≥0.75 default per AIR-R02) — **PENDING: requires pgvector extension**
- [ ] `HybridSearchAsync` merges vector similarity and FTS results using RRF scoring — **PENDING: requires pgvector extension**
- [ ] `HybridSearchAsync` returns results that appear in either vector or FTS results (FULL OUTER JOIN) — **PENDING: requires pgvector extension**
- [ ] `UpsertEmbeddingAsync` with wrong dimension (e.g., 256) throws ArgumentException (400 Bad Request at API layer) — **CONFIRMED: validation throws ArgumentException before touching DB**
- [ ] `UpsertEmbeddingAsync` correctly inserts new embeddings and updates existing ones — **PENDING: requires pgvector extension**
- [x] Table name resolution uses whitelist — no dynamic SQL injection possible — **CONFIRMED: compile-time TableMap dictionary**

## Implementation Checklist

- [x] Create `IVectorSearchService` interface with `SearchSimilarAsync`, `HybridSearchAsync`, `UpsertEmbeddingAsync`, `DeleteEmbeddingAsync` methods
- [x] Create `VectorSearchResult` DTO with `Id`, `Content`, `Similarity`, `FtsRank`, `CombinedScore` properties
- [x] Create `EmbeddingCategory` enum (MedicalTerminology, IntakeTemplate, CodingGuideline) and `HybridSearchRequest` DTO
- [x] Implement `VectorSearchService.SearchSimilarAsync` using parameterized raw SQL with `<=>` cosine distance operator, `Pgvector.Vector` parameter type, and whitelist table name resolution
- [x] Implement `VectorSearchService.HybridSearchAsync` using CTE-based SQL combining cosine similarity with `plainto_tsquery` FTS, merged via Reciprocal Rank Fusion (RRF constant = 60)
- [x] Implement `UpsertEmbeddingAsync` with `INSERT ... ON CONFLICT DO UPDATE` and 384-dimension validation, plus `DeleteEmbeddingAsync`
- [x] Add input validation: reject embeddings where `Length != 384` with `ArgumentException`, validate `topK` (1-100) and `similarityThreshold` (0.0-1.0)
- [x] Register `IVectorSearchService` → `VectorSearchService` as scoped service in `Program.cs` DI container
