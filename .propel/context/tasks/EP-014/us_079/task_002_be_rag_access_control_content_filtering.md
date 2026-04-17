# Task - task_002_be_rag_access_control_content_filtering

## Requirement Reference

- User Story: us_079
- Story Location: .propel/context/tasks/EP-014/us_079/us_079.md
- Acceptance Criteria:
  - AC-2: Given RAG retrieves clinical data chunks, When results are filtered, Then access control rules ensure the user can only view data from documents they are authorized to see.
  - AC-3: Given AI generates a response, When content filtering runs, Then harmful, discriminatory, or medically dangerous content is blocked and replaced with a safe fallback message.

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
| Library | System.Text.RegularExpressions | 8.x |
| AI Gateway | Custom .NET Service with Polly | 8.x |
| Database | PostgreSQL | 16.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-S05, AIR-S07, AIR-S04 |
| **AI Pattern** | RAG |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | config/content-filter-rules.json |
| **Model Provider** | N/A (rule-based filtering, no LLM inference) |

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

Implement two complementary safety layers for the RAG pipeline: (1) a RAG access control filter (`IRagAccessControlFilter` / `RagAccessControlFilter`) that enforces document-level permissions on retrieved chunks per AIR-S07, ensuring users can only view AI-grounded data from clinical documents they are authorized to access; and (2) a content filter (`IContentFilterService` / `ContentFilterService`) that scans AI-generated responses for harmful, discriminatory, or medically dangerous content per AIR-S05, blocking unsafe output and replacing it with a safe fallback message. The access control filter operates post-retrieval (after `IRagRetrievalService` returns chunks) by cross-referencing each chunk's source document against the user's document access permissions stored in the database. The content filter operates post-inference (after the AI Gateway returns the LLM response) using rule-based pattern matching against configurable categories: harmful medical advice (e.g., dosage recommendations without qualification), discriminatory language, and clinically dangerous suggestions (e.g., contraindicated drug combinations). Both components integrate into the AI Gateway pipeline and log all filtering events to the audit trail per AIR-S04.

## Dependent Tasks

- US_067 — Requires AI Gateway service for pipeline integration.
- US_005 — Requires JWT authentication with user role claims for access control decisions.
- US_077 task_001_be_vector_retrieval_service — Requires `IRagRetrievalService` returning `RetrievalResult` with source document metadata.
- US_079 task_001_be_prompt_injection_sanitization — Should execute after input sanitization in pipeline ordering.

## Impacted Components

- **NEW** `src/UPACIP.Service/AiSafety/IRagAccessControlFilter.cs` — Interface defining FilterByAccessAsync method
- **NEW** `src/UPACIP.Service/AiSafety/RagAccessControlFilter.cs` — Implementation: cross-references retrieved chunks against user document permissions
- **NEW** `src/UPACIP.Service/AiSafety/IContentFilterService.cs` — Interface defining FilterResponseAsync method
- **NEW** `src/UPACIP.Service/AiSafety/ContentFilterService.cs` — Rule-based content filtering: harmful, discriminatory, medically dangerous detection
- **NEW** `src/UPACIP.Service/AiSafety/Models/AccessControlResult.cs` — Result DTO: AllowedChunks, DeniedCount, DeniedDocumentIds
- **NEW** `src/UPACIP.Service/AiSafety/Models/ContentFilterResult.cs` — Result DTO: IsBlocked, BlockedCategories, SafeResponse, OriginalResponseHash
- **NEW** `src/UPACIP.Service/AiSafety/Models/ContentFilterCategory.cs` — Enum: HarmfulMedicalAdvice, DiscriminatoryLanguage, DangerousClinicalSuggestion
- **NEW** `src/UPACIP.Service/AiSafety/ContentFilterMiddleware.cs` — AI Gateway middleware for post-inference content filtering
- **NEW** `config/content-filter-rules.json` — Externalized content filter pattern definitions
- **MODIFY** `src/UPACIP.Service/Rag/RagRetrievalService.cs` — Inject IRagAccessControlFilter and apply post-retrieval filtering
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IRagAccessControlFilter, IContentFilterService, and ContentFilterMiddleware

## Implementation Plan

1. **Define access control models**: Create `AccessControlResult` with: `IReadOnlyList<RetrievedChunk> AllowedChunks` (chunks the user is authorized to see), `int DeniedCount` (number of chunks filtered out), `IReadOnlyList<Guid> DeniedDocumentIds` (document IDs that were denied for audit trail). The result preserves the original ordering and scores of allowed chunks.

2. **Define `IRagAccessControlFilter` interface**: Single method: `Task<AccessControlResult> FilterByAccessAsync(IReadOnlyList<RetrievedChunk> chunks, Guid userId, CancellationToken cancellationToken = default)`. The filter takes the raw retrieval results and the authenticated user's ID, returning only chunks from documents the user is permitted to access.

3. **Implement `RagAccessControlFilter`**: The filter resolves the user's accessible document set by querying the `ClinicalDocument` table via EF Core for documents where the user has access (based on the existing document permission model: documents belong to patients, and staff/admin users have role-based access). For each retrieved chunk, extract the `SourceDocumentId` from the chunk metadata and check against the user's accessible document set. Patient users can only access chunks from their own documents. Staff users can access chunks from documents of patients assigned to them. Admin users can access all document chunks. Build the accessible document ID set once per request (not per chunk) for performance. If all chunks are denied, return an empty `AllowedChunks` list — the downstream pipeline handles no-grounding-available gracefully (US_077 task_001). Log denied access attempts at `Warning` level: "RAG access control denied {DeniedCount} chunks from {DeniedDocumentCount} documents for user {UserId}."

4. **Integrate access control into retrieval pipeline**: Modify `RagRetrievalService.RetrieveContextAsync` to inject `IRagAccessControlFilter` and apply it after vector retrieval but before re-ranking. This ensures the re-ranker only processes authorized chunks, and unauthorized chunks never appear in the grounding context. If `AllowedChunks` is empty after filtering, set `RetrievalResult.IsGrounded = false` and `GroundingStatus = "access-denied-no-authorized-chunks"`.

5. **Define content filter models**: Create `ContentFilterCategory` enum: `HarmfulMedicalAdvice`, `DiscriminatoryLanguage`, `DangerousClinicalSuggestion`. Create `ContentFilterResult` with: `bool IsBlocked` (true if any Critical-severity category matched), `IReadOnlyList<ContentFilterCategory> BlockedCategories`, `string SafeResponse` (fallback message when blocked), `string OriginalResponseHash` (SHA-256 hash of blocked response for audit without storing harmful content).

6. **Create externalized content filter rules**: Create `config/content-filter-rules.json` with three categories:
   - **HarmfulMedicalAdvice** (Critical): Patterns detecting unqualified dosage instructions (e.g., `(take|administer)\s+\d+\s*(mg|ml|units)\s+(of|daily|twice)`), self-diagnosis encouragement (`you\s+(have|definitely\s+have|are\s+diagnosed\s+with)`), treatment discontinuation without qualification (`(stop|discontinue|quit)\s+(taking|using|your)\s+(medication|treatment|prescription)`).
   - **DiscriminatoryLanguage** (Critical): Patterns detecting bias based on protected characteristics in clinical recommendations. Configurable keyword lists for race, gender, age, disability discrimination in treatment suggestions.
   - **DangerousClinicalSuggestion** (Critical): Patterns detecting known contraindicated drug combinations (loaded from a configurable list), suggestions that conflict with standard-of-care guidelines, dosage recommendations outside safe ranges for common medications.

7. **Implement `ContentFilterService`**: Load filter rules from `config/content-filter-rules.json` via `IOptions<List<ContentFilterRule>>` with hot-reload. In `FilterResponseAsync`, scan the AI-generated response text against all category patterns using compiled regex with 100ms timeout (ReDoS protection). If any Critical-severity pattern matches: set `IsBlocked = true`, compute `OriginalResponseHash = SHA256(responseText)`, generate `SafeResponse` = "This response has been filtered for safety. Please consult your healthcare provider directly for medical guidance. If you believe this is an error, contact support." Log the blocking event at `Warning` level with the matched category and response hash (never the blocked content itself). Return the `ContentFilterResult` to the caller.

8. **Implement `ContentFilterMiddleware`**: Create a middleware for the AI Gateway response pipeline. After the LLM returns a response: (a) call `IContentFilterService.FilterResponseAsync` with the response text; (b) if `IsBlocked`, replace the response body with `SafeResponse` and set an `X-Content-Filtered: true` response header; (c) if not blocked, pass the response through unchanged. The middleware executes after the LLM response is received but before it is returned to the calling service. Log all filtering events per AIR-S04.

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
│   │   ├── AiSafety/
│   │   │   ├── IPiiRedactionService.cs            ← from US_074 task_001
│   │   │   ├── PiiRedactionService.cs             ← from US_074 task_001
│   │   │   ├── PiiRedactionContext.cs             ← from US_074 task_001
│   │   │   ├── MedicalTermAllowlist.cs            ← from US_074 task_001
│   │   │   ├── PiiRedactionMiddleware.cs          ← from US_074 task_001
│   │   │   ├── IPromptInjectionDetector.cs        ← from task_001
│   │   │   ├── PromptInjectionDetector.cs         ← from task_001
│   │   │   ├── PromptSanitizationMiddleware.cs    ← from task_001
│   │   │   └── Models/
│   │   │       ├── InjectionDetectionResult.cs    ← from task_001
│   │   │       └── InjectionPattern.cs            ← from task_001
│   │   ├── Caching/
│   │   ├── VectorSearch/
│   │   └── Rag/
│   │       ├── IRagRetrievalService.cs
│   │       ├── RagRetrievalService.cs
│   │       ├── IHybridSearchOrchestrator.cs
│   │       ├── HybridSearchOrchestrator.cs
│   │       ├── Chunking/
│   │       ├── Embedding/
│   │       ├── Refresh/
│   │       └── Models/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       └── Entities/
│           ├── Patient.cs
│           └── ClinicalDocument.cs
├── Server/
│   └── AI/
│       └── AiGatewayService.cs                    ← from US_067
├── app/
├── config/
│   ├── medical-term-allowlist.json                ← from US_074 task_001
│   └── prompt-injection-patterns.json             ← from task_001
└── scripts/
```

> Assumes US_067 (AI Gateway), US_074 (PII redaction), US_077 (retrieval), US_078 (hybrid search), and task_001 (prompt injection) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/AiSafety/IRagAccessControlFilter.cs | Interface: FilterByAccessAsync method |
| CREATE | src/UPACIP.Service/AiSafety/RagAccessControlFilter.cs | Document permission cross-referencing against user roles |
| CREATE | src/UPACIP.Service/AiSafety/IContentFilterService.cs | Interface: FilterResponseAsync method |
| CREATE | src/UPACIP.Service/AiSafety/ContentFilterService.cs | Rule-based harmful/discriminatory/dangerous content detection |
| CREATE | src/UPACIP.Service/AiSafety/Models/AccessControlResult.cs | DTO: AllowedChunks, DeniedCount, DeniedDocumentIds |
| CREATE | src/UPACIP.Service/AiSafety/Models/ContentFilterResult.cs | DTO: IsBlocked, BlockedCategories, SafeResponse, OriginalResponseHash |
| CREATE | src/UPACIP.Service/AiSafety/Models/ContentFilterCategory.cs | Enum: HarmfulMedicalAdvice, DiscriminatoryLanguage, DangerousClinicalSuggestion |
| CREATE | src/UPACIP.Service/AiSafety/ContentFilterMiddleware.cs | AI Gateway response middleware: block or pass-through |
| CREATE | config/content-filter-rules.json | Externalized content filter regex patterns for 3 categories |
| MODIFY | src/UPACIP.Service/Rag/RagRetrievalService.cs | Inject IRagAccessControlFilter, apply post-retrieval filtering |
| MODIFY | src/UPACIP.Api/Program.cs | Register IRagAccessControlFilter, IContentFilterService, ContentFilterMiddleware |

## External References

- [OWASP LLM Top 10 — LLM02: Insecure Output Handling](https://genai.owasp.org/llmrisk/llm02-insecure-output-handling/)
- [ASP.NET Core Authorization — Claims-Based](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/claims)
- [EF Core — Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [SHA-256 Hashing in .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256)
- [Serilog Structured Logging](https://serilog.net/)
- [HIPAA Minimum Necessary Standard](https://www.hhs.gov/hipaa/for-professionals/privacy/guidance/minimum-necessary-requirement/index.html)

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
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)
- [ ] Patient users can only see RAG chunks from their own clinical documents
- [ ] Staff users can only see RAG chunks from documents of patients assigned to them
- [ ] Admin users can see chunks from all documents
- [ ] Access-denied chunks never appear in re-ranked context or AI prompt
- [ ] Harmful medical advice patterns (unqualified dosage) are blocked with safe fallback
- [ ] Discriminatory language in AI responses is blocked with safe fallback
- [ ] Blocked responses include `X-Content-Filtered: true` header
- [ ] Safe fallback message directs user to healthcare provider

## Implementation Checklist

- [ ] Create `AccessControlResult`, `ContentFilterResult`, and `ContentFilterCategory` models in `src/UPACIP.Service/AiSafety/Models/`
- [ ] Define `IRagAccessControlFilter` interface with `FilterByAccessAsync` method
- [ ] Implement `RagAccessControlFilter` with role-based document permission checks (Patient → own docs, Staff → assigned patients, Admin → all)
- [ ] Integrate access control filter into `RagRetrievalService` post-retrieval pipeline
- [ ] Define `IContentFilterService` interface with `FilterResponseAsync` method
- [ ] Implement `ContentFilterService` with compiled regex scanning against harmful, discriminatory, and dangerous content patterns
- [ ] Create `config/content-filter-rules.json` with pattern definitions for all three categories
- [ ] Implement `ContentFilterMiddleware` for AI Gateway response pipeline
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-S05, AIR-S07, and AIR-S04 requirements are met (content filtering, access control, audit logging)
