# Task - task_001_be_hybrid_search_orchestration

## Requirement Reference

- User Story: us_078
- Story Location: .propel/context/tasks/EP-014/us_078/us_078.md
- Acceptance Criteria:
  - AC-1: Given a search query is issued, When hybrid search runs, Then both pgvector cosine similarity and PostgreSQL full-text search are executed and results are merged with configurable weighting.
  - AC-2: Given hybrid results are returned, When they are merged, Then duplicates are deduplicated and the combined relevance score determines final ranking.
  - AC-4: Given separate vector indexes exist (medical terminology, intake templates, coding guidelines), When a search is scoped to a category, Then only the relevant index is queried.
- Edge Case:
  - What happens when keyword search returns results that semantic search misses (exact code match)? Keyword results with exact matches are boosted to the top regardless of semantic score.

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
| Caching | Upstash Redis | 7.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements deterministic hybrid search orchestration. It delegates vector and FTS queries to `IVectorSearchService` (US_009) and adds configurable weighting, deduplication, exact match boosting, and category-scoped routing.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement a hybrid search orchestration service (`IHybridSearchOrchestrator` / `HybridSearchOrchestrator`) in the UPACIP.Service project that extends the RAG retrieval pipeline with configurable hybrid search combining pgvector cosine similarity and PostgreSQL full-text search per AIR-R06. The orchestrator calls `IVectorSearchService.HybridSearchAsync` (from US_009) for merged results with Reciprocal Rank Fusion scoring, then applies an additional layer of deduplication (same chunk returned by both vector and FTS paths), configurable weighting between semantic and keyword scores, and exact-match boosting for keyword results that precisely match ICD-10/CPT codes. The service supports category-scoped queries — when a caller specifies a single `EmbeddingCategory`, only that index is queried; when no category is specified, all three indexes (medical terminology, intake templates, coding guidelines) are searched and results merged. It also extends `IRagRetrievalService.RetrieveContextAsync` to honor the `UseHybridSearch` flag on `RetrievalRequest`.

## Dependent Tasks

- US_009 task_002_be_vector_search_service — Requires `IVectorSearchService` with `SearchSimilarAsync` and `HybridSearchAsync` methods and `EmbeddingCategory` enum.
- US_077 task_001_be_vector_retrieval_service — Requires `IRagRetrievalService` with `RetrieveContextAsync` and `RetrievalRequest` (contains `UseHybridSearch` flag).

## Impacted Components

- **NEW** `src/UPACIP.Service/Rag/IHybridSearchOrchestrator.cs` — Interface defining HybridSearchAsync with configurable weighting
- **NEW** `src/UPACIP.Service/Rag/HybridSearchOrchestrator.cs` — Implementation with deduplication, exact match boosting, category-scoped routing, configurable weights
- **NEW** `src/UPACIP.Service/Rag/Models/HybridSearchOptions.cs` — Options DTO with semantic weight, keyword weight, exact match boost factor
- **MODIFY** `src/UPACIP.Service/Rag/RagRetrievalService.cs` — Integrate hybrid search path when `RetrievalRequest.UseHybridSearch == true`
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IHybridSearchOrchestrator in DI container
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add `HybridSearch` configuration section with default weights

## Implementation Plan

1. **Define hybrid search options model**: Create `HybridSearchOptions` with configurable properties: `float SemanticWeight` (default 0.7 — 70% emphasis on cosine similarity), `float KeywordWeight` (default 0.3 — 30% emphasis on FTS relevance), `float ExactMatchBoostFactor` (default 2.0 — multiplier applied to keyword-exact matches), `bool EnableExactMatchBoosting` (default true). Weights must sum to 1.0. Bind from `appsettings.json` section `HybridSearch` using the options pattern (`IOptions<HybridSearchOptions>`). Validate at startup: if `SemanticWeight + KeywordWeight != 1.0`, throw `InvalidOperationException`.

2. **Define `IHybridSearchOrchestrator` interface**: Single method:
   - `Task<IReadOnlyList<RetrievedChunk>> HybridSearchAsync(float[] queryEmbedding, string textQuery, IReadOnlyList<EmbeddingCategory>? targetCategories = null, int topK = 5, float similarityThreshold = 0.75f, CancellationToken cancellationToken = default)` — Executes hybrid search across specified or all categories, returns deduplicated, weighted, boosted results.

3. **Implement category-scoped hybrid search**: In `HybridSearchOrchestrator.HybridSearchAsync`, determine the categories to query. If `targetCategories` is null or empty, search all three categories (MedicalTerminology, IntakeTemplate, CodingGuideline). If a single category is specified, query only that index. For each target category, call `IVectorSearchService.HybridSearchAsync(category, queryEmbedding, textQuery, topK, similarityThreshold)`. Use `Task.WhenAll` to parallelize multi-category queries. This satisfies AC-4 — category-scoped queries hit only the relevant index.

4. **Implement deduplication**: After collecting results from all categories (or from the single RRF query result which already handles vector/FTS dedup via `FULL OUTER JOIN`), deduplicate by chunk `Id`. When duplicates exist (same chunk appears in multiple category results), keep the entry with the higher `CombinedScore`. Track `TotalCandidatesEvaluated` as the pre-dedup count for observability.

5. **Apply configurable weighting**: After deduplication, recalculate the final score for each chunk using the configurable weights. For chunks that have both a `Similarity` score (from vector) and a `FtsRank` (from keyword), compute: `FinalScore = (Similarity * SemanticWeight) + (NormalizedFtsRank * KeywordWeight)`. Normalize `FtsRank` to 0-1 range by dividing by the maximum FTS rank in the result set. For chunks that only appeared in one search type, the missing component contributes 0.

6. **Implement exact match boosting**: After weighted scoring, detect exact keyword matches by comparing `textQuery` against the chunk's `Content` field. An exact match is when the chunk contains the query text as a complete token (case-insensitive, whole-word match). For exact matches, multiply the `FinalScore` by `ExactMatchBoostFactor` (default 2.0). This ensures that keyword results with exact ICD-10/CPT code matches (e.g., query "E11.65" exactly matching a coding guideline chunk) are boosted to the top regardless of semantic score, addressing the edge case. Log boosted items: "Exact match boost applied to chunk {ChunkId} for query '{TextQuery}'."

7. **Integrate with `RagRetrievalService`**: Modify `RagRetrievalService.RetrieveContextAsync` to check `request.UseHybridSearch`. When true, delegate to `IHybridSearchOrchestrator.HybridSearchAsync` instead of calling `IVectorSearchService.SearchSimilarAsync`. Map the orchestrator results back to `RetrievedChunk` list and wrap in `RetrievalResult` with the same `IsGrounded`/`GroundingStatus` logic. The rest of the pipeline (re-ranking, context building) operates identically on hybrid results.

8. **Register service and configuration**: In `Program.cs`, add `builder.Services.Configure<HybridSearchOptions>(builder.Configuration.GetSection("HybridSearch"))` and `builder.Services.AddScoped<IHybridSearchOrchestrator, HybridSearchOrchestrator>()`. Add default configuration in `appsettings.json`: `"HybridSearch": { "SemanticWeight": 0.7, "KeywordWeight": 0.3, "ExactMatchBoostFactor": 2.0, "EnableExactMatchBoosting": true }`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Caching/
│   │   │   ├── ICacheService.cs
│   │   │   └── RedisCacheService.cs
│   │   ├── VectorSearch/
│   │   │   ├── IVectorSearchService.cs
│   │   │   ├── VectorSearchService.cs
│   │   │   ├── VectorSearchResult.cs
│   │   │   ├── EmbeddingCategory.cs
│   │   │   └── HybridSearchRequest.cs
│   │   └── Rag/
│   │       ├── IRagRetrievalService.cs            ← from US_077 task_001
│   │       ├── RagRetrievalService.cs             ← from US_077 task_001
│   │       ├── ISemanticReranker.cs               ← from US_077 task_002
│   │       ├── SemanticReranker.cs                ← from US_077 task_002
│   │       ├── IRagContextBuilder.cs              ← from US_077 task_002
│   │       ├── RagContextBuilder.cs               ← from US_077 task_002
│   │       ├── Chunking/                          ← from US_076 task_001
│   │       ├── Embedding/                         ← from US_076 task_002
│   │       └── Models/
│   │           ├── RetrievalResult.cs
│   │           ├── RetrievedChunk.cs
│   │           ├── RetrievalRequest.cs
│   │           ├── RerankResult.cs
│   │           └── GroundingContext.cs
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       └── Entities/
├── app/
└── scripts/
```

> Assumes US_009 (pgvector + vector search), US_076 (chunking + embedding), and US_077 (retrieval + re-ranking) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Rag/IHybridSearchOrchestrator.cs | Interface with HybridSearchAsync method supporting configurable weighting and category scoping |
| CREATE | src/UPACIP.Service/Rag/HybridSearchOrchestrator.cs | Implementation: multi-category parallel query, deduplication, weighted scoring, exact match boosting |
| CREATE | src/UPACIP.Service/Rag/Models/HybridSearchOptions.cs | Options DTO: SemanticWeight, KeywordWeight, ExactMatchBoostFactor, EnableExactMatchBoosting |
| MODIFY | src/UPACIP.Service/Rag/RagRetrievalService.cs | Add hybrid search path when RetrievalRequest.UseHybridSearch is true, delegate to IHybridSearchOrchestrator |
| MODIFY | src/UPACIP.Api/Program.cs | Register IHybridSearchOrchestrator and bind HybridSearchOptions from configuration |
| MODIFY | src/UPACIP.Api/appsettings.json | Add HybridSearch configuration section with default weights |

## External References

- [Reciprocal Rank Fusion (RRF) — Hybrid Search Pattern](https://plg.uwaterloo.ca/~gvcormac/cormacksigir09-rrf.pdf)
- [PostgreSQL Full-Text Search — ts_rank](https://www.postgresql.org/docs/16/textsearch-controls.html#TEXTSEARCH-RANKING)
- [pgvector Cosine Distance Operator `<=>`](https://github.com/pgvector/pgvector#distances)
- [ASP.NET Core Options Pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options)
- [Task.WhenAll — Parallel Async Operations](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenall)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln

# Run API project
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for UPACIP.Service and UPACIP.Api projects
- [ ] Hybrid search returns results from both cosine similarity and FTS paths merged into a single ranked list
- [ ] Duplicate chunks (same Id from vector and FTS) are deduplicated — only highest-scored entry retained
- [ ] Configurable weights applied: default 70% semantic + 30% keyword
- [ ] Exact ICD-10/CPT code query (e.g., "E11.65") boosts matching chunks to top of results
- [ ] Category-scoped query with single EmbeddingCategory queries only that index
- [ ] Category-scoped query with no category specified queries all three indexes
- [ ] RetrievalRequest.UseHybridSearch=true triggers hybrid path in RagRetrievalService

## Implementation Checklist

- [ ] Create `HybridSearchOptions` with `SemanticWeight`, `KeywordWeight`, `ExactMatchBoostFactor` bound from `appsettings.json` via options pattern
- [ ] Define `IHybridSearchOrchestrator` interface with `HybridSearchAsync` method supporting category scoping and configurable weighting
- [ ] Implement category-scoped routing: single category → query one index, null/empty → parallel `Task.WhenAll` across all three indexes
- [ ] Implement deduplication by chunk `Id` after multi-category aggregation, retaining highest `CombinedScore`
- [ ] Apply configurable weighted scoring: `FinalScore = (Similarity * SemanticWeight) + (NormalizedFtsRank * KeywordWeight)` with FTS rank normalization
- [ ] Implement exact match boosting: multiply `FinalScore` by `ExactMatchBoostFactor` for chunks containing exact query text match
- [ ] Modify `RagRetrievalService.RetrieveContextAsync` to delegate to `IHybridSearchOrchestrator` when `UseHybridSearch == true`
- [ ] Register `IHybridSearchOrchestrator` and `HybridSearchOptions` in `Program.cs`, add default config to `appsettings.json`
