# Task - task_001_be_document_chunking_service

## Requirement Reference

- User Story: us_076
- Story Location: .propel/context/tasks/EP-014/us_076/us_076.md
- Acceptance Criteria:
  - AC-1: Given a knowledge base document is ingested, When chunking runs, Then the document is split into 512-token chunks with 20% (approximately 102 tokens) overlap between consecutive chunks.
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
| Library | Microsoft.ML.Tokenizers (or tiktoken-sharp) | 1.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements deterministic text chunking logic. Embedding generation and vector storage are handled by task_002.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement a document chunking service (`IDocumentChunkingService` / `DocumentChunkingService`) in the UPACIP.Service project that splits knowledge base documents into 512-token segments with 20% (approximately 102 tokens) overlap between consecutive chunks per AIR-R01. The service uses a BPE tokenizer compatible with OpenAI's text-embedding-3-small model (cl100k_base encoding) to ensure accurate token counting. It handles edge cases: documents shorter than 100 tokens are stored as a single chunk without splitting, embedded tables are converted to structured text rows, and images are replaced with an "image-content-excluded" placeholder. Each chunk includes metadata (source document ID, chunk index, total chunks, overlap range) for downstream traceability. The service also performs text preprocessing (whitespace normalization, encoding cleanup) before chunking.

## Dependent Tasks

- US_003 — Requires PostgreSQL database provisioning.

## Impacted Components

- **NEW** `src/UPACIP.Service/Rag/Chunking/IDocumentChunkingService.cs` — Interface defining ChunkDocumentAsync method
- **NEW** `src/UPACIP.Service/Rag/Chunking/DocumentChunkingService.cs` — Implementation with 512-token window, 102-token overlap, short-doc and table/image handling
- **NEW** `src/UPACIP.Service/Rag/Chunking/Models/DocumentChunk.cs` — DTO with content, chunk index, token count, overlap metadata
- **NEW** `src/UPACIP.Service/Rag/Chunking/Models/ChunkingRequest.cs` — Request DTO with document text, source ID, category
- **NEW** `src/UPACIP.Service/Rag/Chunking/Models/ChunkingResult.cs` — Result DTO with chunk list, total chunks, document metadata
- **NEW** `src/UPACIP.Service/Rag/Chunking/TextPreprocessor.cs` — Static helper for whitespace normalization, table-to-text conversion, image placeholder insertion
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IDocumentChunkingService in DI container

## Implementation Plan

1. **Define chunking models**: Create `ChunkingRequest` with properties: `string DocumentText` (raw document content), `Guid SourceDocumentId` (for traceability), `string SourceName` (document name/title), `EmbeddingCategory Category` (MedicalTerminology, IntakeTemplate, CodingGuideline — reuses enum from US_009). Create `DocumentChunk` with: `int ChunkIndex` (0-based), `string Content` (chunk text), `int TokenCount` (BPE-counted tokens), `int OverlapTokens` (number of overlapping tokens with previous chunk, 0 for first chunk), `Guid SourceDocumentId`, `string SourceName`. Create `ChunkingResult` with: `IReadOnlyList<DocumentChunk> Chunks`, `int TotalChunks`, `int TotalTokens`, `Guid SourceDocumentId`, `EmbeddingCategory Category`.

2. **Define `IDocumentChunkingService` interface**: Single method:
   - `Task<ChunkingResult> ChunkDocumentAsync(ChunkingRequest request, CancellationToken cancellationToken = default)` — Accepts document text and returns chunked segments.

3. **Implement BPE tokenizer integration**: Use `Microsoft.ML.Tokenizers` (or `tiktoken-sharp`) NuGet package to initialize a cl100k_base tokenizer compatible with OpenAI's text-embedding-3-small model. The tokenizer is used for two operations: (a) counting tokens in the input document to determine if chunking is needed, and (b) splitting text at precise 512-token boundaries. Initialize the tokenizer once as a singleton and inject via DI. Token encoding/decoding: `Encode(text)` returns token IDs, `Decode(tokenIds)` returns text.

4. **Implement text preprocessing**: Create `TextPreprocessor` with static methods:
   - `NormalizeWhitespace(string text)` — Collapse consecutive whitespace/newlines into single spaces, trim leading/trailing whitespace, normalize Unicode characters (NFC form).
   - `ConvertTablesToText(string text)` — Detect HTML/Markdown table patterns and convert rows to "Column1: Value1 | Column2: Value2" structured text. This preserves tabular information in a tokenizer-friendly format.
   - `ReplaceImages(string text)` — Detect HTML `<img>` tags and Markdown `![alt](url)` patterns, replace each with `[image-content-excluded]` placeholder.
   - Call all three preprocessors in sequence before tokenization.

5. **Implement sliding window chunking**: In `DocumentChunkingService.ChunkDocumentAsync`:
   - Preprocess the text using `TextPreprocessor`.
   - Tokenize the full text: `int[] allTokens = tokenizer.Encode(preprocessedText)`.
   - Check short document edge case: if `allTokens.Length < 100`, return a single chunk containing the full text with `OverlapTokens = 0`.
   - Otherwise, apply sliding window with `windowSize = 512` tokens and `stepSize = 410` tokens (512 - 102 overlap). For each window position `i` starting at 0 with step `stepSize`:
     - Extract token slice: `allTokens[i..Math.Min(i + windowSize, allTokens.Length)]`.
     - Decode slice back to text: `tokenizer.Decode(tokenSlice)`.
     - Create `DocumentChunk` with `ChunkIndex`, decoded `Content`, `TokenCount = tokenSlice.Length`, `OverlapTokens = (i == 0 ? 0 : 102)`.
   - Continue until all tokens are covered. The last chunk may be shorter than 512 tokens.

6. **Handle chunk boundary alignment**: After decoding each token slice, verify the text boundaries don't split mid-word. If the last token in a slice decodes to a partial word, extend the slice by up to 5 tokens to include the complete word, then adjust the next window's start position accordingly. This prevents chunks from starting or ending with garbled text.

7. **Validate chunking output**: After chunking, verify: (a) every token in the original document appears in at least one chunk (no data loss), (b) consecutive chunks share approximately 102 tokens of overlap, (c) no chunk exceeds 520 tokens (512 + boundary adjustment tolerance). Log a warning via `ILogger` if any chunk exceeds the soft limit.

8. **Register service in DI**: In `Program.cs`, register `builder.Services.AddSingleton<IDocumentChunkingService, DocumentChunkingService>()`. The service is stateless and thread-safe (tokenizer is also thread-safe) so singleton registration is appropriate. Register the tokenizer as a singleton: `builder.Services.AddSingleton(sp => Tokenizer.CreateTiktokenForModel("text-embedding-3-small"))`.

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
│   │   ├── Caching/
│   │   │   ├── ICacheService.cs
│   │   │   └── RedisCacheService.cs
│   │   └── VectorSearch/
│   │       ├── IVectorSearchService.cs
│   │       ├── VectorSearchService.cs
│   │       └── EmbeddingCategory.cs
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── MedicalTerminologyEmbedding.cs
│       │   ├── IntakeTemplateEmbedding.cs
│       │   └── CodingGuidelineEmbedding.cs
│       └── Configurations/
├── app/
└── scripts/
    ├── provision-pgvector.sql
    └── rebuild-vector-indexes.ps1
```

> Assumes US_003 (PostgreSQL) and US_009 (pgvector schema + vector search service) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Rag/Chunking/IDocumentChunkingService.cs | Interface with ChunkDocumentAsync method |
| CREATE | src/UPACIP.Service/Rag/Chunking/DocumentChunkingService.cs | Sliding window chunking: 512-token window, 102-token overlap, short-doc bypass, boundary alignment |
| CREATE | src/UPACIP.Service/Rag/Chunking/Models/DocumentChunk.cs | DTO: ChunkIndex, Content, TokenCount, OverlapTokens, SourceDocumentId, SourceName |
| CREATE | src/UPACIP.Service/Rag/Chunking/Models/ChunkingRequest.cs | DTO: DocumentText, SourceDocumentId, SourceName, Category |
| CREATE | src/UPACIP.Service/Rag/Chunking/Models/ChunkingResult.cs | DTO: Chunks list, TotalChunks, TotalTokens, SourceDocumentId, Category |
| CREATE | src/UPACIP.Service/Rag/Chunking/TextPreprocessor.cs | Static helpers: NormalizeWhitespace, ConvertTablesToText, ReplaceImages |
| MODIFY | src/UPACIP.Service/UPACIP.Service.csproj | Add Microsoft.ML.Tokenizers NuGet package reference |
| MODIFY | src/UPACIP.Api/Program.cs | Register IDocumentChunkingService (singleton) and tokenizer in DI |

## External References

- [Microsoft.ML.Tokenizers NuGet Package](https://www.nuget.org/packages/Microsoft.ML.Tokenizers)
- [OpenAI Tokenizer — cl100k_base Encoding](https://platform.openai.com/tokenizer)
- [Text Splitting Best Practices for RAG](https://docs.llamaindex.ai/en/stable/understanding/loading/loading/)
- [Unicode Normalization Forms (NFC)](https://unicode.org/reports/tr15/)

## Build Commands

```powershell
# Add tokenizer package
dotnet add src/UPACIP.Service/UPACIP.Service.csproj package Microsoft.ML.Tokenizers

# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for UPACIP.Service project
- [ ] 512-token document produces exactly 1 chunk (no splitting needed)
- [ ] 1024-token document produces 3 chunks: [0-511], [410-921], [820-1023] with ~102-token overlap
- [ ] Document with <100 tokens produces single chunk without splitting (edge case)
- [ ] HTML table `<table>` is converted to "Column: Value" structured text
- [ ] Markdown image `![alt](url)` is replaced with `[image-content-excluded]`
- [ ] No chunk exceeds 520 tokens (512 + 8 boundary tolerance)
- [ ] All tokens from original document appear in at least one chunk (no data loss)

## Implementation Checklist

- [ ] Create `ChunkingRequest`, `DocumentChunk`, and `ChunkingResult` model classes in `src/UPACIP.Service/Rag/Chunking/Models/`
- [ ] Define `IDocumentChunkingService` interface with `ChunkDocumentAsync` method
- [ ] Integrate BPE tokenizer (cl100k_base) via `Microsoft.ML.Tokenizers` for accurate token counting and splitting
- [ ] Implement `TextPreprocessor` with `NormalizeWhitespace`, `ConvertTablesToText`, `ReplaceImages` static methods
- [ ] Implement sliding window chunking: 512-token window, 410-token step (102 overlap), short-doc (<100 tokens) bypass
- [ ] Implement chunk boundary alignment to prevent mid-word splits (extend up to 5 tokens)
- [ ] Add output validation: verify no data loss, overlap consistency, and max-token soft limit (520)
- [ ] Register `IDocumentChunkingService` as singleton and tokenizer in `Program.cs` DI container
