# Task - task_002_ai_semantic_reranking_pipeline

## Requirement Reference

- User Story: us_077
- Story Location: .propel/context/tasks/EP-014/us_077/us_077.md
- Acceptance Criteria:
  - AC-3: Given top-5 results pass the threshold, When re-ranking runs, Then the semantic re-ranker reorders results by relevance to the specific query context.
  - AC-4: Given re-ranked results are ready, When they are passed to the AI model, Then they are included as grounding context in the prompt with source citations.
- Edge Case:
  - How does the system handle ambiguous queries that match multiple knowledge domains? Re-ranker prioritizes results from the most relevant domain (medical terminology > intake templates > coding guidelines).

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
| AI/ML | OpenAI GPT-4o-mini (Primary) | 2024-07-18 |
| AI/ML | Anthropic Claude 3.5 Sonnet (Fallback) | claude-3-5-sonnet-20241022 |
| Embedding Model | OpenAI text-embedding-3-small | 2024 |
| Vector Store | PostgreSQL pgvector | 0.5.x |
| AI Gateway | Custom .NET Service with Polly | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-R02, AIR-R03 |
| **AI Pattern** | RAG |
| **Prompt Template Path** | prompts/rag/reranking-prompt.liquid |
| **Guardrails Config** | N/A |
| **Model Provider** | OpenAI GPT-4o-mini (Primary), Anthropic Claude 3.5 Sonnet (Fallback) |

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

Implement the semantic re-ranking and prompt context builder components for the RAG pipeline. The semantic re-ranker (`ISemanticReranker` / `SemanticReranker`) takes retrieved chunks from `IRagRetrievalService` (task_001) and reorders them by relevance to the specific query context using a lightweight LLM-based scoring approach via the AI Gateway. The re-ranker assigns relevance scores by prompting GPT-4o-mini with the query and candidate chunks, then applies domain priority weighting (medical terminology > intake templates > coding guidelines) as a tiebreaker for ambiguous queries. The prompt context builder (`IRagContextBuilder` / `RagContextBuilder`) formats re-ranked chunks into structured grounding context with source citations for inclusion in downstream AI prompts. When no grounding is available (`IsGrounded = false` from retrieval), the context builder produces an empty context block with the "no-grounding-available" marker. All re-ranking requests are routed through the AI Gateway for resilience (circuit breaker, retry) and audit logging per AIR-S04.

## Dependent Tasks

- US_077 task_001_be_vector_retrieval_service — Requires `IRagRetrievalService` returning `RetrievalResult` with scored chunks.
- US_067 — Requires AI Gateway service for LLM inference routing (OpenAI/Claude fallback).

## Impacted Components

- **NEW** `src/UPACIP.Service/Rag/ISemanticReranker.cs` — Interface defining RerankAsync method
- **NEW** `src/UPACIP.Service/Rag/SemanticReranker.cs` — LLM-based re-ranking implementation using AI Gateway
- **NEW** `src/UPACIP.Service/Rag/IRagContextBuilder.cs` — Interface defining BuildContextAsync method
- **NEW** `src/UPACIP.Service/Rag/RagContextBuilder.cs` — Formats re-ranked chunks into prompt-ready grounding context with citations
- **NEW** `src/UPACIP.Service/Rag/Models/RerankResult.cs` — Result DTO with reordered chunks and relevance scores
- **NEW** `src/UPACIP.Service/Rag/Models/GroundingContext.cs` — Prompt-ready context block with citations and grounding metadata
- **NEW** `prompts/rag/reranking-prompt.liquid` — Versioned Liquid template for re-ranking scoring prompt
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register ISemanticReranker and IRagContextBuilder in DI container

## Implementation Plan

1. **Define re-ranking models**: Create `RerankResult` with properties: `IReadOnlyList<RankedChunk> Chunks` (reordered), `TimeSpan RerankLatency`, `bool UsedLlmReranking` (true if LLM scoring succeeded, false if fallback to similarity-only ordering). Create `RankedChunk` extending `RetrievedChunk` with additional: `float RelevanceScore` (LLM-assigned 0-1), `int FinalRank` (1-based position), `float DomainWeight` (category-based priority weight). Create `GroundingContext` with: `string FormattedContext` (prompt-ready text block), `IReadOnlyList<SourceCitation> Citations`, `bool IsGrounded`, `string GroundingStatus`. Create `SourceCitation` with: `int Index` (1-based citation number), `string SourceDocument`, `string ChunkPreview` (truncated first 100 chars), `float RelevanceScore`.

2. **Define `ISemanticReranker` interface**: Single method:
   - `Task<RerankResult> RerankAsync(IReadOnlyList<RetrievedChunk> chunks, string queryText, CancellationToken cancellationToken = default)` — Accepts retrieved chunks and original query text, returns re-ranked result.

3. **Implement LLM-based re-ranking**: In `SemanticReranker.RerankAsync`, construct a re-ranking prompt using the Liquid template (`prompts/rag/reranking-prompt.liquid`). The prompt provides the query and each candidate chunk, asking the model to score each chunk's relevance to the query on a 0-1 scale. Send the prompt through the AI Gateway service (`AiGatewayService`) which handles provider routing (GPT-4o-mini primary, Claude fallback) and resilience. Parse the JSON response to extract per-chunk relevance scores. Token budget for re-ranking: 500 input tokens + 200 output tokens (lightweight scoring, not generation).

4. **Create re-ranking prompt template**: Create `prompts/rag/reranking-prompt.liquid` with versioned content. The template receives two variables — `query` (the user's query text) and `chunks` (array of candidate text chunks). It instructs the model to score each chunk's relevance on a 0.0-1.0 scale and respond with a JSON array of `{"index": N, "relevance": X.XX}` objects. Template includes a system instruction: "You are a medical knowledge relevance scorer. Score only based on direct relevance to the query. Do not invent information." Response parsing validates JSON structure, checks score ranges (0.0-1.0), and rejects malformed responses.

5. **Apply domain priority weighting**: After LLM scoring, apply domain priority weights as tiebreakers for chunks with similar relevance scores (within 0.05 tolerance). Weight map: `MedicalTerminology = 1.0`, `IntakeTemplate = 0.9`, `CodingGuideline = 0.8`. Final score formula: `FinalScore = RelevanceScore + (DomainWeight * 0.01)`. This ensures medical terminology results are preferred over intake templates when relevance scores are nearly identical, addressing the ambiguous query edge case. Sort by `FinalScore` descending to produce final ranking.

6. **Implement LLM fallback logic**: If the AI Gateway returns an error (timeout, rate limit, both providers unavailable), fall back to similarity-score-only ordering from the original retrieval. Set `RerankResult.UsedLlmReranking = false`. Log a warning: "LLM re-ranking unavailable, falling back to cosine similarity ordering." This ensures the RAG pipeline never blocks on re-ranker failure — retrieval results are still usable without re-ranking.

7. **Define `IRagContextBuilder` interface and implement**: Single method:
   - `Task<GroundingContext> BuildContextAsync(RerankResult rerankResult, CancellationToken cancellationToken = default)` — Formats re-ranked chunks into prompt-ready grounding context.
   In `RagContextBuilder.BuildContextAsync`: if `rerankResult.Chunks` is empty, return `GroundingContext` with `IsGrounded = false`, `GroundingStatus = "no-grounding-available"`, `FormattedContext = ""`, empty citations. If chunks exist, format each chunk as a numbered citation block wrapped in `[GROUNDING CONTEXT]` delimiters: `[1] (Source: {SourceAttribution}, Relevance: {RelevanceScore:F2})\n{ChunkContent}`. Build `SourceCitation` list with index, source document, truncated content preview (first 100 chars), and relevance score for downstream audit logging per AIR-S04.

8. **Register services in DI**: In `Program.cs`, add `builder.Services.AddScoped<ISemanticReranker, SemanticReranker>()` and `builder.Services.AddScoped<IRagContextBuilder, RagContextBuilder>()`. `SemanticReranker` depends on `AiGatewayService` and `ILogger<SemanticReranker>`. `RagContextBuilder` is stateless.

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
│   │       ├── IRagRetrievalService.cs            ← from task_001
│   │       ├── RagRetrievalService.cs             ← from task_001
│   │       ├── Chunking/                          ← from US_076 task_001
│   │       ├── Embedding/                         ← from US_076 task_002
│   │       └── Models/
│   │           ├── RetrievalResult.cs             ← from task_001
│   │           ├── RetrievedChunk.cs              ← from task_001
│   │           └── RetrievalRequest.cs            ← from task_001
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       └── Entities/
├── Server/
│   └── AI/
│       └── AiGatewayService.cs                    ← from US_067
├── prompts/
│   └── rag/                                       ← NEW directory
│       └── reranking-prompt.liquid                ← NEW
├── app/
└── scripts/
```

> Assumes task_001 (retrieval service), US_067 (AI Gateway), US_076 (knowledge base), and US_009 (pgvector) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Rag/ISemanticReranker.cs | Interface with RerankAsync method |
| CREATE | src/UPACIP.Service/Rag/SemanticReranker.cs | LLM-based re-ranking via AI Gateway with domain priority weighting and fallback logic |
| CREATE | src/UPACIP.Service/Rag/IRagContextBuilder.cs | Interface with BuildContextAsync method |
| CREATE | src/UPACIP.Service/Rag/RagContextBuilder.cs | Formats re-ranked chunks into numbered citation blocks for prompt grounding |
| CREATE | src/UPACIP.Service/Rag/Models/RerankResult.cs | DTO: RankedChunks list, RerankLatency, UsedLlmReranking flag |
| CREATE | src/UPACIP.Service/Rag/Models/GroundingContext.cs | DTO: FormattedContext string, Citations list, IsGrounded flag, GroundingStatus |
| CREATE | prompts/rag/reranking-prompt.liquid | Versioned Liquid template for LLM relevance scoring prompt |
| MODIFY | src/UPACIP.Api/Program.cs | Register ISemanticReranker → SemanticReranker and IRagContextBuilder → RagContextBuilder in DI |

## External References

- [LLM-Based Passage Re-Ranking — Research Overview](https://arxiv.org/abs/2310.06839)
- [Liquid Template Language Reference](https://shopify.github.io/liquid/)
- [OpenAI GPT-4o-mini API Reference](https://platform.openai.com/docs/models/gpt-4o-mini)
- [Polly Resilience Policies (.NET)](https://github.com/App-vNext/Polly)
- [RAG Prompt Engineering Best Practices](https://docs.llamaindex.ai/en/stable/optimizing/production_rag/)
- [Serilog Structured Logging](https://serilog.net/)

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
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Re-ranking reorders chunks by LLM-assigned relevance score (not just cosine similarity)
- [ ] Domain priority weighting resolves ties: medical terminology > intake templates > coding guidelines
- [ ] LLM re-ranking fallback returns cosine-similarity-ordered chunks when AI Gateway is unavailable
- [ ] GroundingContext includes numbered source citations in `[1]`, `[2]` format
- [ ] Empty retrieval result produces `IsGrounded = false` and `GroundingStatus = "no-grounding-available"`
- [ ] Re-ranking prompt token budget stays within 500 input / 200 output tokens

## Implementation Checklist

- [ ] Create `RerankResult`, `RankedChunk`, `GroundingContext`, and `SourceCitation` model classes in `src/UPACIP.Service/Rag/Models/`
- [ ] Define `ISemanticReranker` interface with `RerankAsync(chunks, queryText)` method
- [ ] Implement `SemanticReranker` using AI Gateway for LLM-based relevance scoring with JSON response parsing and validation
- [ ] Create `prompts/rag/reranking-prompt.liquid` template with query + candidate chunks → JSON relevance scores
- [ ] Apply domain priority weighting (MedicalTerminology=1.0, IntakeTemplate=0.9, CodingGuideline=0.8) as tiebreaker for ambiguous queries
- [ ] Implement fallback to cosine-similarity ordering when LLM re-ranking fails (set `UsedLlmReranking = false`)
- [ ] Implement `RagContextBuilder.BuildContextAsync` formatting re-ranked chunks as numbered citation blocks with `[GROUNDING CONTEXT]` wrapper and no-grounding handling
- [ ] Register `ISemanticReranker` and `IRagContextBuilder` in `Program.cs` DI container
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-R02, AIR-R03 requirements are met (retrieval quality, re-ranking)
