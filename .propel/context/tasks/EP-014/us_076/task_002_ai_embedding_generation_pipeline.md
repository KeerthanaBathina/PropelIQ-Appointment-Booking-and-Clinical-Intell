# Task - task_002_ai_embedding_generation_pipeline

## Requirement Reference

- User Story: us_076
- Story Location: .propel/context/tasks/EP-014/us_076/us_076.md
- Acceptance Criteria:
  - AC-2: Given chunks are created, When embedding generation runs, Then each chunk is converted to a 384-dimensional vector using the configured embedding model and stored in pgvector.
  - AC-3: Given embeddings are stored, When the system queries for similar content, Then the cosine similarity search returns results within 500ms for the knowledge base.
  - AC-4: Given frequently used medical terms exist, When they are queried repeatedly, Then their embeddings are cached in Redis to reduce computation.
- Edge Case:
  - What happens when a document is too short for meaningful chunking (<100 tokens)? System stores the entire document as a single chunk without splitting.
  - How does the system handle documents with embedded tables or images? Tables are converted to structured text; images are skipped with a note "image-content-excluded."

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
| Embedding Model | OpenAI text-embedding-3-small | 2024 |
| AI Gateway | Custom .NET Service with Polly | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-R01, AIR-R04 |
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

Implement the embedding generation and storage pipeline (`IEmbeddingGenerationService` / `EmbeddingGenerationService`) in the UPACIP.Service project that converts document chunks (from task_001) into 384-dimensional vectors using OpenAI's text-embedding-3-small model and stores them in the appropriate pgvector embedding table via `IVectorSearchService.UpsertEmbeddingAsync` (from US_009). The pipeline processes chunks in batches to optimize API calls and cost ($0.02/1M tokens), implements Redis caching for frequently used medical term embeddings per AIR-O06, and runs as an async background worker triggered by a Redis queue job. It routes embedding API calls through the AI Gateway for resilience (circuit breaker, retry with exponential backoff per AIR-O04/AIR-O08) and logs all embedding operations for audit per AIR-S04. The service also orchestrates the end-to-end document ingestion flow: accept document → chunk (task_001) → embed → store → verify searchability.

## Dependent Tasks

- US_076 task_001_be_document_chunking_service — Requires `IDocumentChunkingService` for splitting documents into 512-token chunks.
- US_009 task_001_db_pgvector_extension_schema — Requires pgvector embedding tables (medical_terminology_embeddings, intake_template_embeddings, coding_guideline_embeddings).
- US_009 task_002_be_vector_search_service — Requires `IVectorSearchService.UpsertEmbeddingAsync` for storing embeddings.
- US_067 — Requires AI Gateway for resilient OpenAI API calls.

## Impacted Components

- **NEW** `src/UPACIP.Service/Rag/Embedding/IEmbeddingGenerationService.cs` — Interface defining GenerateEmbeddingAsync, GenerateEmbeddingsAsync (batch), IngestDocumentAsync
- **NEW** `src/UPACIP.Service/Rag/Embedding/EmbeddingGenerationService.cs` — Implementation using AI Gateway for OpenAI embedding calls, Redis caching, batch processing
- **NEW** `src/UPACIP.Service/Rag/Embedding/Models/EmbeddingRequest.cs` — Request DTO for single/batch embedding generation
- **NEW** `src/UPACIP.Service/Rag/Embedding/Models/EmbeddingResult.cs` — Result DTO with embedding vector, token count, cached flag
- **NEW** `src/UPACIP.Service/Rag/Embedding/DocumentIngestionWorker.cs` — IHostedService background worker processing document ingestion jobs from Redis queue
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IEmbeddingGenerationService and DocumentIngestionWorker in DI container

## Implementation Plan

1. **Define embedding models**: Create `EmbeddingRequest` with properties: `string Text` (chunk content to embed), `EmbeddingCategory Category` (determines target table), `Guid? SourceDocumentId` (for traceability). Create `EmbeddingResult` with: `float[] Embedding` (384-dim vector), `int TokensUsed` (for cost tracking), `bool WasCached` (true if retrieved from Redis). Create `IngestionRequest` with: `string DocumentText` (full document content), `Guid SourceDocumentId`, `string SourceName`, `EmbeddingCategory Category`.

2. **Define `IEmbeddingGenerationService` interface**: Three methods:
   - `Task<EmbeddingResult> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)` — Single text embedding via OpenAI API.
   - `Task<IReadOnlyList<EmbeddingResult>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)` — Batch embedding for multiple texts in a single API call.
   - `Task IngestDocumentAsync(IngestionRequest request, CancellationToken cancellationToken = default)` — End-to-end: chunk → embed → store.

3. **Implement single embedding generation**: In `GenerateEmbeddingAsync`, call OpenAI's `POST /v1/embeddings` endpoint through the AI Gateway service with parameters: `model = "text-embedding-3-small"`, `input = text`, `dimensions = 384`. Parse the response to extract the 384-dim float array. Validate response: if `embedding.Length != 384`, throw `InvalidOperationException("Expected 384 dimensions, got {length}")`. Track `TokensUsed` from the API response `usage.total_tokens` field for cost monitoring per AIR-O09.

4. **Implement batch embedding generation**: In `GenerateEmbeddingsAsync`, send multiple texts in a single API call using OpenAI's batch input support (array of strings in `input` field). Batch size limit: 100 texts per API call (OpenAI limit). If `texts.Count > 100`, split into sub-batches and process sequentially. This reduces API round trips and cost. Parse response to map each embedding to its input text by array index.

5. **Implement Redis embedding cache**: Before calling OpenAI API, check Redis cache for each text's embedding using `ICacheService`. Cache key: `embedding:{sha256(text)}` where SHA-256 hash prevents key collision. Cache TTL: 24 hours (embeddings are deterministic for the same model version). On cache hit, return cached embedding with `WasCached = true` without API call. On cache miss, call API, cache the result, and return with `WasCached = false`. For batch calls, filter out cached texts, call API only for uncached texts, then merge results. Log cache hit ratio for monitoring: "Embedding cache hit: {hits}/{total} ({hitRate:F1}%)".

6. **Implement end-to-end document ingestion**: In `IngestDocumentAsync`:
   - Call `IDocumentChunkingService.ChunkDocumentAsync` to split the document into 512-token chunks.
   - Call `GenerateEmbeddingsAsync` with all chunk texts in a batch.
   - For each chunk + embedding pair, call `IVectorSearchService.UpsertEmbeddingAsync(request.Category, chunkId, chunk.Content, embedding.Embedding)` to store in the appropriate pgvector table.
   - Log completion: "Document {SourceName} ingested: {chunkCount} chunks, {cachedCount} cached, {apiTokens} tokens used."
   - Handle partial failures: if any embedding call fails, log the error and continue with remaining chunks. Return partial results rather than failing entirely.

7. **Implement background worker**: Create `DocumentIngestionWorker` as an `IHostedService` (BackgroundService) that listens on a Redis queue key (`queue:document-ingestion`). When a job is dequeued, deserialize the `IngestionRequest` and call `IEmbeddingGenerationService.IngestDocumentAsync`. Implement error handling: on failure, re-queue the job with a retry count (max 3 retries per AIR-O08). On permanent failure, log error and move to dead-letter queue (`queue:document-ingestion:dead`). Process one job at a time to avoid overwhelming the OpenAI API rate limit.

8. **Handle AI provider failures**: Route all embedding API calls through the AI Gateway which provides Polly circuit breaker (AIR-O04: open after 5 consecutive failures, retry after 30s) and retry with exponential backoff (AIR-O08: max 3 retries). If the embedding API is completely unavailable (circuit breaker open), the worker pauses processing and re-queues jobs for later retry. Log: "Embedding API unavailable, circuit breaker open. Requeuing {jobCount} jobs."

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
│   │   │   └── EmbeddingCategory.cs
│   │   └── Rag/
│   │       └── Chunking/
│   │           ├── IDocumentChunkingService.cs    ← from task_001
│   │           ├── DocumentChunkingService.cs     ← from task_001
│   │           ├── TextPreprocessor.cs            ← from task_001
│   │           └── Models/
│   │               ├── DocumentChunk.cs           ← from task_001
│   │               ├── ChunkingRequest.cs         ← from task_001
│   │               └── ChunkingResult.cs          ← from task_001
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── MedicalTerminologyEmbedding.cs
│       │   ├── IntakeTemplateEmbedding.cs
│       │   └── CodingGuidelineEmbedding.cs
│       └── Configurations/
├── Server/
│   └── AI/
│       └── AiGatewayService.cs                    ← from US_067
├── app/
└── scripts/
    ├── provision-pgvector.sql
    └── rebuild-vector-indexes.ps1
```

> Assumes task_001 (chunking service), US_009 (pgvector + vector search), US_067 (AI Gateway), and US_004 (Redis) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Rag/Embedding/IEmbeddingGenerationService.cs | Interface with GenerateEmbeddingAsync, GenerateEmbeddingsAsync (batch), IngestDocumentAsync methods |
| CREATE | src/UPACIP.Service/Rag/Embedding/EmbeddingGenerationService.cs | OpenAI embedding calls via AI Gateway, Redis cache with SHA-256 keys, batch processing (max 100/call) |
| CREATE | src/UPACIP.Service/Rag/Embedding/Models/EmbeddingRequest.cs | DTO: Text, Category, SourceDocumentId |
| CREATE | src/UPACIP.Service/Rag/Embedding/Models/EmbeddingResult.cs | DTO: Embedding (float[384]), TokensUsed, WasCached |
| CREATE | src/UPACIP.Service/Rag/Embedding/DocumentIngestionWorker.cs | BackgroundService processing Redis queue jobs, retry logic (max 3), dead-letter queue |
| MODIFY | src/UPACIP.Api/Program.cs | Register IEmbeddingGenerationService (scoped) and DocumentIngestionWorker (hosted service) in DI |

## External References

- [OpenAI Embeddings API — text-embedding-3-small](https://platform.openai.com/docs/guides/embeddings)
- [OpenAI Embedding Pricing — $0.02/1M tokens](https://openai.com/api/pricing/)
- [Polly Resilience Policies (.NET)](https://github.com/App-vNext/Polly)
- [BackgroundService in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)
- [Redis Queue Pattern with StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/PipelinesMultiplexers.html)
- [SHA-256 Hashing in .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln

# Run API project (starts background worker)
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for UPACIP.Service and UPACIP.Api projects
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] `GenerateEmbeddingAsync` returns a 384-dimensional float array for valid text input
- [ ] `GenerateEmbeddingsAsync` processes 100+ texts in sub-batches of 100
- [ ] Redis cache returns cached embedding on second call for same text (WasCached = true)
- [ ] `IngestDocumentAsync` end-to-end: document → chunks → embeddings → pgvector storage
- [ ] Cosine similarity search returns ingested content within 500ms (AC-3)
- [ ] DocumentIngestionWorker processes Redis queue jobs and retries on failure (max 3)

## Implementation Checklist

- [ ] Create `EmbeddingRequest`, `EmbeddingResult`, and `IngestionRequest` model classes in `src/UPACIP.Service/Rag/Embedding/Models/`
- [ ] Define `IEmbeddingGenerationService` interface with `GenerateEmbeddingAsync`, `GenerateEmbeddingsAsync`, `IngestDocumentAsync`
- [ ] Implement single embedding generation via AI Gateway calling OpenAI `text-embedding-3-small` (384 dimensions, validate response length)
- [ ] Implement batch embedding with sub-batching at 100 texts per API call and token usage tracking
- [ ] Implement Redis embedding cache with `embedding:{sha256}` key pattern and 24-hour TTL
- [ ] Implement `IngestDocumentAsync` orchestrating chunk → embed → `UpsertEmbeddingAsync` with partial failure handling
- [ ] Implement `DocumentIngestionWorker` as BackgroundService with Redis queue, retry (max 3), and dead-letter queue
- [ ] Register `IEmbeddingGenerationService` and `DocumentIngestionWorker` in `Program.cs` DI container
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-R01, AIR-R04 requirements are met (chunking size, separate indexes)
