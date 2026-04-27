# Implementation Analysis -- task_002_be_knowledge_base_refresh_pipeline

## Verdict

**Status:** Fail

**Summary:** Zero of the eight required deliverables have been created.
The task specifies a new RAG-layer knowledge base refresh pipeline—comprising five new
service/model files, one new Admin controller, one SQL migration, and one Program.cs
registration—but none of these exist in the codebase as of the analysis date (2026-04-27).
A related but separate ICD-10/CPT library refresh pipeline (`Icd10LibraryService`,
`CptCodeLibraryService`, `CodingController`) operates at the EF Core / relational layer and
does not fulfil this task's vector-embedding requirements (chunk-embed-stage-swap pipeline
for pgvector tables). The task is therefore fully unimplemented and requires complete
greenfield development before it can be considered delivered.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : fn / line) | Result |
|---|---|---|
| AC-3: quarterly refresh triggers chunk → embed → index + deprecated code marking | No `IKnowledgeBaseRefreshService` exists | **Fail** |
| Edge case: mid-refresh queries served from live table (atomic swap) | No staging table schema or swap logic found | **Fail** |
| AIR-R05: quarterly refresh of medical terminology embeddings | No RAG-layer refresh service found | **Fail** |
| Diff engine: classify new / updated / deprecated / unchanged codes by `CodeValue` | Not implemented in any Rag/ component | **Fail** |
| Staging table writes (chunk + embed to `*_embeddings_staging`) | No staging tables in `provision-pgvector.sql` | **Fail** |
| Atomic swap: `ALTER TABLE RENAME` live→old, staging→live within single transaction | No SQL or C# swap logic exists | **Fail** |
| `deprecated_at TIMESTAMPTZ` column added to live embedding tables | Missing from `scripts/provision-pgvector.sql` | **Fail** |
| Admin-only `POST /api/admin/knowledge-base/refresh` + `GET status` endpoints | No `KnowledgeBaseRefreshController` or `Admin/` folder | **Fail** |
| `IKnowledgeBaseRefreshService` registered in `Program.cs` | No registration found | **Fail** |
| Audit logging per AIR-S04 (user ID, timestamp, version; no PII in logs) | No refresh audit log calls found | **Fail** |
| Input sanitization / guardrails (AI Impact = Yes per task spec) | Not implemented | **Fail** |
| Fallback logic for OpenAI embedding failures during refresh | Not implemented | **Fail** |
| Token budget enforcement (AIR-O01) during batch embedding | Not implemented | **Fail** |

---

## Logical & Design Findings

### Business Logic

- **No diff engine**: the task requires classifying each `CodeLibraryEntry` as new, updated,
  deprecated, or unchanged by comparing against the live embedding table. No such logic exists.
- **Staging pattern absent**: the atomic-swap approach (write to `*_embeddings_staging`, verify
  row counts, then `ALTER TABLE RENAME` inside a transaction) is the sole mechanism for the
  mid-refresh query edge case. Without it, any future partial implementation would serve
  corrupt or incomplete results to in-flight queries.
- **`deprecated_at` soft-delete column missing from all three pgvector tables**: the SQL script
  (`scripts/provision-pgvector.sql`) has no `deprecated_at TIMESTAMPTZ NULL` column and no
  `ALTER TABLE … ADD COLUMN IF NOT EXISTS` migration block. Without this column,
  soft-deprecation cannot be persisted.
- **Overlap with existing `Icd10LibraryService`**: the existing service refreshes relational
  rows in `icd10_code_library`; this task refreshes the *vector index* with new embeddings.
  Both must eventually run together for AC-3 to be fully satisfied. The two pipelines must
  be documented as complementary, not alternatives.

### Security

- **OWASP A01 – Broken Access Control**: the Admin controller does not exist yet; when
  created it must carry `[Authorize(Policy = RbacPolicies.AdminOnly)]` and return 403 for
  non-admin callers—consistent with `CodingController.cs` L299.
- **OWASP A03 – Injection**: `CodeValue` and `Description` fields from the incoming payload
  are passed to the chunking and embedding pipeline. Both fields must be sanitised (null-byte
  removal, length caps) before use.
- **Secret handling**: `AiGatewaySettings.OpenAiApiKey` is already injected via options
  pattern; the refresh pipeline must reuse this and never log it.
- **AIR-S04 – Audit log**: must record `InitiatedByUserId`, timestamp, `SourceVersion`, and
  operation counts—no PII (no patient data touches this pipeline, so risk is low but still
  requires the log entry).

### Error Handling

- **Chunking failure**: if `IDocumentChunkingService.ChunkDocumentAsync` throws, the pipeline
  must catch, log the failing `CodeValue`, and set `RefreshResult.Status = Failed` while
  leaving the live table untouched.
- **Embedding API failure**: transient OpenAI errors must use the existing Polly retry
  pipeline (`EmbeddingGenerationService` already has caching; the refresh service should call
  `IngestDocumentAsync` or `GenerateEmbeddingsAsync` and handle `HttpRequestException` /
  `TaskCanceledException`).
- **Staging verify gate**: after all embeddings are written to staging, the implementation
  plan calls for a row-count check (`staging >= expected`). If this check fails the swap must
  not execute and `RefreshResult.Status = Failed`.
- **Transaction rollback**: the `ALTER TABLE RENAME` swap runs inside a raw SQL transaction
  via `ExecuteSqlRawAsync`; any exception must trigger an explicit `ROLLBACK` and the service
  must propagate a `Failed` result rather than re-throw silently.
- **Idempotency on re-run**: the staging table must be truncated at the start of each refresh
  so a re-run after partial failure does not leave stale data.

### Data Access

- **Raw SQL usage**: the atomic swap requires DDL statements (`ALTER TABLE RENAME`) that
  EF Core migrations cannot express as regular migration steps. The implementation must
  use `_db.Database.ExecuteSqlRawAsync` wrapped in `BeginTransactionAsync`. SQL strings
  must be constructed from a safe whitelist of table-name suffixes derived from the
  `EmbeddingCategory` enum—never from user input—to prevent SQL injection (OWASP A03).
- **Index rebuild after swap**: after the swap, IVFFlat and GIN indexes on the new live table
  need to be rebuilt. The task plan calls for `REINDEX INDEX CONCURRENTLY`; this must run
  outside the swap transaction (concurrent reindex cannot run inside a transaction block in
  PostgreSQL 16).
- **`deprecated_at` filtering**: once the column is added, `IVectorSearchService` queries
  should filter `WHERE deprecated_at IS NULL` to exclude stale embeddings from search
  results. This cross-cutting change is not part of this task but must be tracked.

### Performance

- **Batch embedding**: `IEmbeddingGenerationService.GenerateEmbeddingsAsync` already supports
  sub-batches of 100 texts per OpenAI call. The refresh pipeline must use this method rather
  than calling `GenerateEmbeddingAsync` per chunk in a loop (N+1 API calls).
- **Memory**: large code libraries (ICD-10-CM 2026 has ~70 000 codes) will produce many
  chunks. The implementation should stream/page the diff results rather than loading all
  existing rows into memory at once.

### Patterns & Standards

- **DI scope**: `IVectorSearchService` is scoped; `IKnowledgeBaseRefreshService` must
  therefore also be scoped (not singleton) to avoid captive-dependency issues.
- **`RefreshRequest` naming collision**: `AuthController.cs` L606 already defines
  `public sealed record RefreshRequest(string AccessToken)` in the `UPACIP.Api` namespace.
  The new `RefreshRequest` must be placed in the
  `UPACIP.Service.Rag.Refresh.Models` namespace and imported explicitly in the controller
  to avoid ambiguity.
- **`Admin/` subfolder**: all existing Admin controllers (`AdminConfigController.cs`, etc.)
  reside in `src/UPACIP.Api/Controllers/` without a subfolder. The task specifies
  `Controllers/Admin/KnowledgeBaseRefreshController.cs`. The directory must be created and
  the route prefix set to `api/admin/knowledge-base` to match the task spec.

---

## Test Review

### Existing Tests

No unit or integration tests covering any RAG refresh component were found.

### Missing Tests (must add)

- [ ] Unit: `DiffEngine_NewCode_ClassifiedAsNew` — verify a code present in request but
  absent from existing set is added to the new-codes queue.
- [ ] Unit: `DiffEngine_DescriptionChanged_ClassifiedAsUpdated` — verify `CodeValue` match
  with differing `Description` routes to re-embed queue.
- [ ] Unit: `DiffEngine_AbsentCode_ClassifiedAsDeprecated` — verify code absent from request
  (or `IsDeprecated=true`) is stamped with `deprecated_at`.
- [ ] Unit: `DiffEngine_UnchangedCode_Skipped` — verify unchanged code produces no
  chunking/embedding calls.
- [ ] Integration: `RefreshAsync_AtomicSwap_LiveTableNeverPartial` — simulate a mid-swap
  query; verify it reads from the live (pre-swap) table, not staging.
- [ ] Integration: `RefreshAsync_EmbeddingFailure_LiveTableUntouched` — mock OpenAI to throw
  on batch 2; verify live table rows are unchanged.
- [ ] Integration: `RefreshAsync_RequiresAdminRole_Returns403ForStaff` — verify the endpoint
  rejects Staff-role JWT.
- [ ] Negative/Edge: `RefreshAsync_EmptyEntries_ReturnsFailedResult` — empty entry list
  should be rejected at validation.
- [ ] Negative/Edge: `RefreshAsync_DuplicateCodeValues_DeduplicatedBeforeDiff` — verify
  duplicate `CodeValue` entries in the request do not produce duplicate embeddings.

---

## Validation Results

- **Commands Executed:** `grep -r "IKnowledgeBaseRefreshService|KnowledgeBaseRefresh" src/`
- **Outcomes:** 0 matches in service / controller layers — task is fully unimplemented.
- **Build status:** `dotnet build UPACIP.Service` and `UPACIP.Api` both pass (0 CS errors),
  confirming the absence of these files does not break the existing build.
- **SQL check:** `provision-pgvector.sql` contains no `deprecated_at` column or staging
  tables for any of the three embedding tables.

---

## Fix Plan (Prioritized)

1. **Add `deprecated_at` + staging tables to `provision-pgvector.sql`**
   — `scripts/provision-pgvector.sql`
   — Add `ALTER TABLE … ADD COLUMN IF NOT EXISTS deprecated_at TIMESTAMPTZ NULL` for all
   three embedding tables; add three `CREATE TABLE IF NOT EXISTS *_embeddings_staging
   (LIKE *_embeddings INCLUDING ALL)` blocks.
   — ETA 1h — Risk: **L** (additive DDL only; no existing data affected; idempotent guards).

2. **Create `CodeLibraryEntry`, `RefreshRequest`, `RefreshResult` model classes**
   — `src/UPACIP.Service/Rag/Refresh/Models/`
   — Use `EmbeddingCategory` (not `string`) for category to stay type-safe.
   — Watch for naming collision: `RefreshRequest` already exists in `UPACIP.Api`; keep
   service models in `UPACIP.Service.Rag.Refresh.Models` namespace.
   — ETA 0.5h — Risk: **L**.

3. **Define `IKnowledgeBaseRefreshService` interface**
   — `src/UPACIP.Service/Rag/Refresh/IKnowledgeBaseRefreshService.cs`
   — Two methods: `RefreshAsync` + `GetRefreshStatusAsync`.
   — ETA 0.25h — Risk: **L**.

4. **Implement `KnowledgeBaseRefreshService`**
   — `src/UPACIP.Service/Rag/Refresh/KnowledgeBaseRefreshService.cs`
   — Scoped. Depends on `IDocumentChunkingService`, `IEmbeddingGenerationService`,
   `IVectorSearchService`, `ApplicationDbContext`, `ILogger`.
   — Pipeline: truncate staging → diff → batch chunk+embed → staging upsert → count verify
   → atomic swap → `REINDEX INDEX CONCURRENTLY` (outside txn) → mark deprecated.
   — Guard SQL table-name construction against injection: derive from `EmbeddingCategory`
   enum switch, never from user input.
   — ETA 4h — Risk: **H** (DDL operations, concurrency, partial-failure semantics).

5. **Create `KnowledgeBaseRefreshController`**
   — `src/UPACIP.Api/Controllers/Admin/KnowledgeBaseRefreshController.cs`
   — `[Authorize(Policy = RbacPolicies.AdminOnly)]`.
   — `POST /api/admin/knowledge-base/refresh` → 202 Accepted + job status.
   — `GET /api/admin/knowledge-base/refresh/status` → current result.
   — Audit log call on trigger (AIR-S04).
   — ETA 1.5h — Risk: **M**.

6. **Register service in `Program.cs`**
   — `src/UPACIP.Api/Program.cs`
   — `builder.Services.AddScoped<IKnowledgeBaseRefreshService, KnowledgeBaseRefreshService>()`
   — ETA 0.25h — Risk: **L**.

7. **Add guardrails and input sanitization (AIR-O01, OWASP A03)**
   — Inside `KnowledgeBaseRefreshService.RefreshAsync`: validate
   `request.Entries` is non-null/non-empty; cap `CodeValue` to 20 chars and `Description`
   to 4 000 chars; remove null bytes before passing to chunker (consistent with
   `EmbeddingGenerationService.SanitizeText`).
   — ETA 0.5h — Risk: **M**.

8. **Write unit + integration tests**
   — See Test Review section for full list.
   — ETA 4h — Risk: **L** (tests do not touch production paths).

---

## Appendix

### Rules Applied

- `rules/ai-assistant-usage-policy.md` — explicit commands, minimal output
- `rules/code-anti-patterns.md` — no god objects, no magic constants
- `rules/dry-principle-guidelines.md` — reuse `EmbeddingCategory`, `SanitizeText` from
  existing services; do not duplicate diff logic
- `rules/security-standards-owasp.md` — OWASP A01 (AdminOnly policy), A03 (no raw user
  input in SQL), A09 (audit logging)
- `rules/backend-development-standards.md` — scoped service, constructor injection
- `rules/dotnet-architecture-standards.md` — layered architecture (Service / Controller
  separation); no direct DB access from controller
- `rules/database-standards.md` — idempotent DDL, soft-delete with timestamp
- `rules/performance-best-practices.md` — batch embedding, paged diff, no N+1 API calls

### Search Evidence

| Pattern | File | Outcome |
|---|---|---|
| `IKnowledgeBaseRefreshService` | `src/**/*.cs` | 0 matches — not implemented |
| `KnowledgeBaseRefresh` | `src/**/*.cs` | 0 matches — not implemented |
| `deprecated_at` | `scripts/provision-pgvector.sql` | 0 matches — column missing |
| `staging` | `scripts/provision-pgvector.sql` | 0 matches — tables missing |
| `UpsertEmbeddingAsync` | `IVectorSearchService.cs` L53 | Present — dependency satisfied |
| `IDocumentChunkingService` | `Rag/Chunking/IDocumentChunkingService.cs` | Present |
| `IEmbeddingGenerationService` | `Rag/Embedding/IEmbeddingGenerationService.cs` | Present |
| `RefreshRequest` (collision risk) | `AuthController.cs` L606 | Name conflict — namespace caution |
| `[Authorize(Policy = RbacPolicies.AdminOnly)]` | `CodingController.cs` L299 | Pattern to follow |
