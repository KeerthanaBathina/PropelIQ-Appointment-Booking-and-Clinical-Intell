# Task - task_001_be_vector_retrieval_service

## Requirement Reference

- User Story: us_077
- Story Location: .propel/context/tasks/EP-014/us_077/us_077.md
- Acceptance Criteria:
  - AC-1: Given an AI query requires knowledge base context, When retrieval runs, Then the system returns the top-5 most similar chunks using cosine similarity search.
  - AC-2: Given similarity results are returned, When any result has a cosine similarity score below 0.75, Then it is excluded from the context provided to the AI model.
- Edge Case:
  - What happens when no results meet the 0.75 threshold? System proceeds without RAG context and adds a "no-grounding-available" flag to the AI response for staff awareness.

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

> This task implements the backend retrieval orchestration layer. It delegates vector search to `IVectorSearchService` (US_009) and returns structured results. Semantic re-ranking and prompt composition are handled by task_002.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement a backend RAG retrieval service (`IRagRetrievalService` / `RagRetrievalService`) in the UPACIP.Service project that orchestrates top-5 vector retrieval across multiple embedding categories (medical terminology, intake templates, coding guidelines) using the existing `IVectorSearchService` from US_009. The service enforces the cosine similarity threshold (≥0.75 per AIR-R02), aggregates results from one or more indexes based on the query context, and returns a structured `RetrievalResult` containing matched chunks with similarity scores and source attribution. When no results meet the threshold, the service returns a result with `IsGrounded = false` and a `no-grounding-available` flag for downstream consumers (AI Gateway, prompt builder) to propagate to staff. The service also supports optional embedding caching via Redis for frequently queried terms per AIR-O06.

## Dependent Tasks

- US_009 task_002_be_vector_search_service — Requires `IVectorSearchService` with `SearchSimilarAsync` and `HybridSearchAsync` methods.
- US_076 — Requires chunked and embedded knowledge base documents stored in pgvector.

## Impacted Components

- **NEW** `src/UPACIP.Service/Rag/IRagRetrievalService.cs` — Interface defining RetrieveContextAsync and RetrieveContextForCategoryAsync
- **NEW** `src/UPACIP.Service/Rag/RagRetrievalService.cs` — Implementation orchestrating IVectorSearchService across categories with threshold enforcement
- **NEW** `src/UPACIP.Service/Rag/Models/RetrievalResult.cs` — Result DTO containing matched chunks, IsGrounded flag, and metadata
- **NEW** `src/UPACIP.Service/Rag/Models/RetrievedChunk.cs` — Individual chunk DTO with content, similarity score, source attribution, and category
- **NEW** `src/UPACIP.Service/Rag/Models/RetrievalRequest.cs` — Request DTO with query embedding, text query, target categories, and options
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IRagRetrievalService in DI container

## Implementation Plan

1. **Define request/result models**: Create `RetrievalRequest` with properties: `float[] QueryEmbedding` (384-dim), `string TextQuery` (original text for audit), `IReadOnlyList<EmbeddingCategory> TargetCategories` (defaults to all three: MedicalTerminology, IntakeTemplate, CodingGuideline), `int TopK` (default 5), `float SimilarityThreshold` (default 0.75f), `bool UseHybridSearch` (default false). Create `RetrievedChunk` with: `Guid Id`, `string Content`, `float SimilarityScore`, `EmbeddingCategory Category`, `string SourceAttribution` (document name/section). Create `RetrievalResult` with: `IReadOnlyList<RetrievedChunk> Chunks`, `bool IsGrounded` (true when at least one chunk returned), `string GroundingStatus` ("grounded" or "no-grounding-available"), `int TotalCandidatesEvaluated`, `TimeSpan RetrievalLatency`.

2. **Define `IRagRetrievalService` interface**: Two methods:
   - `Task<RetrievalResult> RetrieveContextAsync(RetrievalRequest request, CancellationToken cancellationToken = default)` — Retrieves top-K chunks across specified categories, applies threshold filter, returns aggregated result.
   - `Task<RetrievalResult> RetrieveContextForCategoryAsync(EmbeddingCategory category, float[] queryEmbedding, int topK = 5, float similarityThreshold = 0.75f, CancellationToken cancellationToken = default)` — Single-category convenience overload.

3. **Implement multi-category retrieval**: In `RetrieveContextAsync`, iterate over each `TargetCategory` in the request and call `IVectorSearchService.SearchSimilarAsync(category, request.QueryEmbedding, topK: request.TopK, similarityThreshold: request.SimilarityThreshold)`. Collect all results across categories into a unified list. Each result is mapped to a `RetrievedChunk` with the originating `EmbeddingCategory` preserved. Use `Task.WhenAll` to parallelize category searches for latency optimization.

4. **Apply threshold filtering and top-K selection**: After aggregating results from all categories, re-filter to ensure all chunks have `SimilarityScore >= request.SimilarityThreshold` (defense-in-depth; `IVectorSearchService` also filters but re-validate here). Sort by `SimilarityScore` descending. Take the top `request.TopK` chunks from the aggregated, sorted list. This ensures the final result contains the globally top-5 most similar chunks regardless of which category they came from.

5. **Handle no-grounding-available edge case**: After filtering, if the resulting chunk list is empty, construct `RetrievalResult` with `IsGrounded = false`, `GroundingStatus = "no-grounding-available"`, `Chunks` as empty list. Downstream consumers (AI Gateway, prompt builder) check `IsGrounded` to decide whether to include RAG context in the prompt or set the no-grounding flag on the AI response. Log a warning via `ILogger<RagRetrievalService>`: "No RAG context found above threshold {Threshold} for query. Proceeding without grounding."

6. **Implement embedding cache integration**: Before calling `IVectorSearchService`, check Redis cache for the query embedding hash using `ICacheService` (from US_004). Cache key: `rag:retrieval:{sha256(queryEmbedding)}:{categories}`. Cache TTL: 5 minutes per NFR-030. On cache hit, return cached `RetrievalResult` directly. On cache miss, execute retrieval and cache the result. Use the existing `ICacheService` interface (`RedisCacheService`) from the Caching module.

7. **Record retrieval latency**: Wrap the retrieval call with a `Stopwatch` to measure total latency (across all categories). Populate `RetrievalResult.RetrievalLatency`. Log with structured logging: "RAG retrieval completed in {LatencyMs}ms, {ChunkCount} chunks returned, IsGrounded={IsGrounded}". This supports AIR-R02 performance monitoring (top-5 retrieval <500ms target).

8. **Register service in DI**: In `Program.cs`, add `builder.Services.AddScoped<IRagRetrievalService, RagRetrievalService>()`. The service depends on `IVectorSearchService` and `ICacheService` (both already registered).

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
│   │       ├── Chunking/                          ← from US_076 task_001
│   │       │   ├── IDocumentChunkingService.cs
│   │       │   ├── DocumentChunkingService.cs
│   │       │   └── Models/
│   │       └── Embedding/                         ← from US_076 task_002
│   │           ├── IEmbeddingGenerationService.cs
│   │           ├── EmbeddingGenerationService.cs
│   │           └── Models/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── MedicalTerminologyEmbedding.cs
│       │   ├── IntakeTemplateEmbedding.cs
│       │   └── CodingGuidelineEmbedding.cs
│       └── Configurations/
├── app/
└── scripts/
```

> Assumes US_009 (pgvector + vector search service) and US_076 (document chunking and embedding) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Rag/IRagRetrievalService.cs | Interface with RetrieveContextAsync, RetrieveContextForCategoryAsync methods |
| CREATE | src/UPACIP.Service/Rag/RagRetrievalService.cs | Implementation orchestrating IVectorSearchService across categories, threshold enforcement, caching, latency tracking |
| CREATE | src/UPACIP.Service/Rag/Models/RetrievalResult.cs | DTO: Chunks list, IsGrounded flag, GroundingStatus, TotalCandidatesEvaluated, RetrievalLatency |
| CREATE | src/UPACIP.Service/Rag/Models/RetrievedChunk.cs | DTO: Id, Content, SimilarityScore, Category, SourceAttribution |
| CREATE | src/UPACIP.Service/Rag/Models/RetrievalRequest.cs | DTO: QueryEmbedding, TextQuery, TargetCategories, TopK, SimilarityThreshold, UseHybridSearch |
| MODIFY | src/UPACIP.Api/Program.cs | Register IRagRetrievalService → RagRetrievalService in DI |

## External References

- [pgvector Cosine Distance Operator `<=>`](https://github.com/pgvector/pgvector#distances)
- [Task.WhenAll — Parallel Async Operations](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.whenall)
- [Stopwatch for Performance Measurement](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch)
- [Serilog Structured Logging](https://serilog.net/)
- [Redis Caching with StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/)

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
- [ ] `RetrieveContextAsync` returns top-5 chunks sorted by cosine similarity descending
- [ ] All returned chunks have `SimilarityScore >= 0.75` (threshold enforced)
- [ ] Multi-category retrieval searches across MedicalTerminology, IntakeTemplate, CodingGuideline indexes
- [ ] When no results meet threshold, `RetrievalResult.IsGrounded == false` and `GroundingStatus == "no-grounding-available"`
- [ ] Parallel category search completes within 500ms for typical queries (per AIR-R02 <500ms target)
- [ ] Redis cache hit returns previously retrieved results without re-querying pgvector
- [ ] Structured log entries include retrieval latency, chunk count, and grounding status

## Implementation Checklist

- [ ] Create `RetrievalRequest`, `RetrievedChunk`, and `RetrievalResult` model classes in `src/UPACIP.Service/Rag/Models/`
- [ ] Define `IRagRetrievalService` interface with `RetrieveContextAsync` and `RetrieveContextForCategoryAsync` methods
- [ ] Implement multi-category parallel retrieval using `Task.WhenAll` over `IVectorSearchService.SearchSimilarAsync` per category
- [ ] Implement threshold re-validation, descending sort by similarity, and global top-K selection across aggregated results
- [ ] Implement no-grounding-available handling: return `IsGrounded = false` with `GroundingStatus = "no-grounding-available"` when chunk list is empty
- [ ] Integrate Redis caching via `ICacheService` with key `rag:retrieval:{hash}:{categories}` and 5-minute TTL
- [ ] Add retrieval latency measurement via `Stopwatch` and structured logging with Serilog
- [ ] Register `IRagRetrievalService` → `RagRetrievalService` as scoped service in `Program.cs` DI container
