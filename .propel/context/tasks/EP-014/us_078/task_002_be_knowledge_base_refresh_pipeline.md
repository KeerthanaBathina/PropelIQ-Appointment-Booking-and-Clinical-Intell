# Task - task_002_be_knowledge_base_refresh_pipeline

## Requirement Reference

- User Story: us_078
- Story Location: .propel/context/tasks/EP-014/us_078/us_078.md
- Acceptance Criteria:
  - AC-3: Given a quarterly ICD-10/CPT code update is released, When the admin triggers a knowledge base refresh, Then new codes are chunked, embedded, and indexed while deprecated codes are marked.
- Edge Case:
  - How does the system handle a mid-refresh query? System serves queries from the existing index until refresh completes; atomic swap to new index on completion.

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
| Embedding Model | OpenAI text-embedding-3-small | 2024 |
| AI Gateway | Custom .NET Service with Polly | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-R05 |
| **AI Pattern** | RAG |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | OpenAI (text-embedding-3-small) |

### **CRITICAL: AI Implementation Requirement (AI Tasks Only)**

**IF AI Impact = Yes:**

- **MUST** reference prompt templates from Prompt Template Path during implementation
- **MUST** implement guardrails for input sanitization and output validation
- **MUST** enforce token budget limits per AIR-O01 requirements
- **MUST** implement fallback logic for low-confidence responses
- **MUST** log all prompts/responses for audit (redact PII)
- **MUST** handle model failures gracefully (timeout, rate limit, 5xx errors)

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement a knowledge base refresh pipeline (`IKnowledgeBaseRefreshService` / `KnowledgeBaseRefreshService`) in the UPACIP.Service project that enables admin-triggered quarterly refresh of ICD-10/CPT code libraries per AIR-R05 and FR-063. The pipeline accepts new code library data (CSV or structured format), diffs against existing entries to identify new, updated, and deprecated codes, chunks new/updated code descriptions using `IDocumentChunkingService` (US_076 task_001), generates embeddings via `IEmbeddingGenerationService` (US_076 task_002), and stores them using `IVectorSearchService.UpsertEmbeddingAsync` (US_009). Deprecated codes are soft-marked with a `deprecated_at` timestamp rather than deleted. To handle mid-refresh queries (edge case), the pipeline uses a staging table pattern: new embeddings are written to staging tables, verified for completeness, then atomically swapped with the live tables within a database transaction. The refresh is exposed via an admin-only API endpoint and logs all operations for audit per AIR-S04.

## Dependent Tasks

- US_076 task_001_be_document_chunking_service — Requires `IDocumentChunkingService` for chunking new code descriptions.
- US_076 task_002_ai_embedding_generation_pipeline — Requires `IEmbeddingGenerationService` for embedding new/updated codes.
- US_009 task_002_be_vector_search_service — Requires `IVectorSearchService` for upserting embeddings.
- US_009 task_001_db_pgvector_extension_schema — Requires embedding table schema.

## Impacted Components

- **NEW** `src/UPACIP.Service/Rag/Refresh/IKnowledgeBaseRefreshService.cs` — Interface defining RefreshAsync, GetRefreshStatusAsync methods
- **NEW** `src/UPACIP.Service/Rag/Refresh/KnowledgeBaseRefreshService.cs` — Implementation: diff, chunk, embed, staging table swap, deprecated code marking
- **NEW** `src/UPACIP.Service/Rag/Refresh/Models/RefreshRequest.cs` — Request DTO with code library data, category, source version
- **NEW** `src/UPACIP.Service/Rag/Refresh/Models/RefreshResult.cs` — Result DTO with counts (new, updated, deprecated), duration, status
- **NEW** `src/UPACIP.Service/Rag/Refresh/Models/CodeLibraryEntry.cs` — Individual code entry DTO (code value, description, code system, status)
- **NEW** `src/UPACIP.Api/Controllers/Admin/KnowledgeBaseRefreshController.cs` — Admin-only endpoint POST /api/admin/knowledge-base/refresh
- **MODIFY** `scripts/provision-pgvector.sql` — Add staging tables and deprecated_at column to embedding tables
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IKnowledgeBaseRefreshService in DI container

## Implementation Plan

1. **Define refresh models**: Create `CodeLibraryEntry` with: `string CodeValue` (e.g., "E11.65"), `string CodeSystem` ("ICD-10" or "CPT"), `string Description` (full text description), `string Category` (maps to `EmbeddingCategory`), `bool IsDeprecated` (true if code is being retired). Create `RefreshRequest` with: `IReadOnlyList<CodeLibraryEntry> Entries`, `EmbeddingCategory TargetCategory`, `string SourceVersion` (e.g., "ICD-10-CM-2026-Q2"), `string InitiatedByUserId`. Create `RefreshResult` with: `int NewCodesAdded`, `int CodesUpdated`, `int CodesDeprecated`, `int TotalProcessed`, `TimeSpan Duration`, `RefreshStatus Status` (enum: InProgress, Completed, Failed), `string ErrorMessage` (null on success).

2. **Define `IKnowledgeBaseRefreshService` interface**: Two methods:
   - `Task<RefreshResult> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)` — Executes full refresh pipeline.
   - `Task<RefreshResult> GetRefreshStatusAsync(CancellationToken cancellationToken = default)` — Returns current/last refresh status for admin monitoring.

3. **Implement code library diff**: In `RefreshAsync`, first load existing code entries from the target embedding table. Compare incoming `RefreshRequest.Entries` against existing entries by `CodeValue`:
   - **New codes**: Present in request but not in existing table → queue for chunking + embedding.
   - **Updated codes**: Present in both but `Description` differs → queue for re-chunking + re-embedding.
   - **Deprecated codes**: Present in existing table but absent from request (or marked `IsDeprecated = true`) → mark with `deprecated_at = CURRENT_TIMESTAMP`.
   - **Unchanged codes**: Present in both with identical description → skip (no processing needed).
   Log summary: "Refresh diff: {NewCount} new, {UpdatedCount} updated, {DeprecatedCount} deprecated, {UnchangedCount} unchanged."

4. **Process new and updated codes**: For each new/updated code, call `IDocumentChunkingService.ChunkDocumentAsync` to split the code description into 512-token chunks. Then call `IEmbeddingGenerationService.GenerateEmbeddingsAsync` with all chunk texts in batch. Write embeddings to a staging table (`{category}_embeddings_staging`) rather than the live table. This ensures mid-refresh queries continue to hit the live table without seeing partially-updated data.

5. **Add staging table schema**: Modify `scripts/provision-pgvector.sql` to create three staging tables mirroring the live embedding tables: `medical_terminology_embeddings_staging`, `intake_template_embeddings_staging`, `coding_guideline_embeddings_staging`. These have identical schemas to the live tables. Also add `deprecated_at TIMESTAMPTZ NULL` column to each live embedding table to support soft-deprecation.

6. **Implement atomic swap**: After all new/updated embeddings are written to staging and verified (count check: staging row count >= expected), execute an atomic swap within a single PostgreSQL transaction:
   ```sql
   BEGIN;
   -- Rename live to old
   ALTER TABLE {category}_embeddings RENAME TO {category}_embeddings_old;
   -- Rename staging to live
   ALTER TABLE {category}_embeddings_staging RENAME TO {category}_embeddings;
   -- Drop old
   DROP TABLE {category}_embeddings_old;
   -- Recreate empty staging for next refresh
   CREATE TABLE {category}_embeddings_staging (LIKE {category}_embeddings INCLUDING ALL);
   COMMIT;
   ```
   This ensures mid-refresh queries always hit a complete, consistent table. If the transaction fails, the live table remains untouched. Rebuild IVFFlat indexes after swap using `REINDEX INDEX CONCURRENTLY`.

7. **Implement admin API endpoint**: Create `KnowledgeBaseRefreshController` with `[Authorize(Roles = "Admin")]` attribute at `POST /api/admin/knowledge-base/refresh`. The endpoint accepts `RefreshRequest` body, validates the request (non-empty entries, valid category, valid code system), and calls `IKnowledgeBaseRefreshService.RefreshAsync`. Return `202 Accepted` with a job ID since refresh is a long-running operation. Add `GET /api/admin/knowledge-base/refresh/status` to poll refresh status. Log the refresh trigger in `AuditLog` with user ID, timestamp, and code library version per AIR-S04.

8. **Handle refresh failures and rollback**: If any step fails (chunking error, embedding API failure, staging write failure), catch the exception, set `RefreshResult.Status = Failed` with error message, and leave the live table untouched (staging table contains partial data which is discarded). Log error: "Knowledge base refresh failed at step {Step}: {ErrorMessage}. Live table unchanged." On next successful refresh, the staging table is truncated before new data is written.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── Admin/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Caching/
│   │   ├── VectorSearch/
│   │   │   ├── IVectorSearchService.cs
│   │   │   ├── VectorSearchService.cs
│   │   │   └── EmbeddingCategory.cs
│   │   └── Rag/
│   │       ├── IRagRetrievalService.cs
│   │       ├── RagRetrievalService.cs
│   │       ├── IHybridSearchOrchestrator.cs       ← from task_001
│   │       ├── HybridSearchOrchestrator.cs        ← from task_001
│   │       ├── Chunking/
│   │       │   ├── IDocumentChunkingService.cs    ← from US_076
│   │       │   └── DocumentChunkingService.cs
│   │       ├── Embedding/
│   │       │   ├── IEmbeddingGenerationService.cs ← from US_076
│   │       │   └── EmbeddingGenerationService.cs
│   │       └── Models/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       └── Entities/
├── Server/
│   └── AI/
│       └── AiGatewayService.cs                    ← from US_067
├── app/
└── scripts/
    ├── provision-pgvector.sql
    └── rebuild-vector-indexes.ps1
```

> Assumes task_001 (hybrid search), US_076 (chunking + embedding), US_077 (retrieval), and US_009 (pgvector) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Rag/Refresh/IKnowledgeBaseRefreshService.cs | Interface with RefreshAsync and GetRefreshStatusAsync methods |
| CREATE | src/UPACIP.Service/Rag/Refresh/KnowledgeBaseRefreshService.cs | Diff engine, staging writes, atomic swap, deprecated code marking, error handling |
| CREATE | src/UPACIP.Service/Rag/Refresh/Models/RefreshRequest.cs | DTO: Entries list, TargetCategory, SourceVersion, InitiatedByUserId |
| CREATE | src/UPACIP.Service/Rag/Refresh/Models/RefreshResult.cs | DTO: NewCodesAdded, CodesUpdated, CodesDeprecated, Duration, Status, ErrorMessage |
| CREATE | src/UPACIP.Service/Rag/Refresh/Models/CodeLibraryEntry.cs | DTO: CodeValue, CodeSystem, Description, Category, IsDeprecated |
| CREATE | src/UPACIP.Api/Controllers/Admin/KnowledgeBaseRefreshController.cs | Admin-only POST /api/admin/knowledge-base/refresh + GET status endpoint |
| MODIFY | scripts/provision-pgvector.sql | Add staging tables and deprecated_at column to embedding tables |
| MODIFY | src/UPACIP.Api/Program.cs | Register IKnowledgeBaseRefreshService in DI |

## External References

- [PostgreSQL ALTER TABLE RENAME](https://www.postgresql.org/docs/16/sql-altertable.html)
- [PostgreSQL REINDEX CONCURRENTLY](https://www.postgresql.org/docs/16/sql-reindex.html)
- [ASP.NET Core Authorization — Role-Based](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- [ICD-10-CM Code Update Schedule (CMS)](https://www.cms.gov/medicare/coding-billing/icd-10-codes)
- [OpenAI Embeddings API — text-embedding-3-small](https://platform.openai.com/docs/guides/embeddings)
- [EF Core Raw SQL — ExecuteSqlRawAsync](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln

# Run API project
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj

# Apply staging table migration
dotnet ef database update --project src/UPACIP.DataAccess
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for UPACIP.Service and UPACIP.Api projects
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Diff correctly identifies new, updated, deprecated, and unchanged codes from input
- [ ] New/updated codes are chunked, embedded, and written to staging table (not live)
- [ ] Atomic swap replaces live table with staging in a single transaction
- [ ] Mid-refresh queries return results from live table (not staging)
- [ ] Deprecated codes retain `deprecated_at` timestamp (soft delete, not hard delete)
- [ ] Admin endpoint requires Admin role — non-admin requests receive 403 Forbidden
- [ ] Refresh failure leaves live table untouched and sets status to Failed

## Implementation Checklist

- [ ] Create `CodeLibraryEntry`, `RefreshRequest`, and `RefreshResult` model classes in `src/UPACIP.Service/Rag/Refresh/Models/`
- [ ] Define `IKnowledgeBaseRefreshService` interface with `RefreshAsync` and `GetRefreshStatusAsync` methods
- [ ] Implement code library diff: compare incoming entries against existing by `CodeValue` to classify as new/updated/deprecated/unchanged
- [ ] Implement staging table writes: chunk + embed new/updated codes via `IDocumentChunkingService` and `IEmbeddingGenerationService`, write to staging tables
- [ ] Implement atomic swap: `ALTER TABLE RENAME` live→old, staging→live, drop old, recreate staging within single transaction
- [ ] Add staging tables and `deprecated_at` column to `scripts/provision-pgvector.sql`
- [ ] Create `KnowledgeBaseRefreshController` with admin-only `POST /api/admin/knowledge-base/refresh` and `GET status` endpoints
- [ ] Register `IKnowledgeBaseRefreshService` in `Program.cs` DI container
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-R05 requirements are met (quarterly refresh of medical terminology embeddings)
