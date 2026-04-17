# Task - task_002_be_medical_code_validation

## Requirement Reference

- User Story: us_085
- Story Location: .propel/context/tasks/EP-016/us_085/us_085.md
- Acceptance Criteria:
  - AC-4: Given an ICD-10 or CPT code is submitted, When code validation runs, Then the system validates the code against the current code library and suggests alternatives if the code is invalid or deprecated.

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
| Database | PostgreSQL with pgvector | 16.x / 0.5.x |
| AI | OpenAI text-embedding-3-small | 384 dimensions |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Indirect — uses embedding similarity search for suggested alternatives |
| **AIR Requirements** | N/A |
| **AI Pattern** | Embedding similarity search (pgvector cosine distance) |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | OpenAI (embedding only, no LLM inference) |

> Code validation itself is deterministic (exact match lookup). Suggested alternatives use pgvector cosine similarity on pre-computed embeddings from the RAG knowledge base (US_078 task_002). No LLM prompting involved.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement ICD-10 and CPT code validation against the current code library with suggested alternatives for invalid or deprecated codes (DR-015). This task creates an `IMedicalCodeValidationService` that validates submitted medical codes against the `RagKnowledgeBaseEntry` data maintained by the knowledge base refresh pipeline (US_078 task_002). Validation performs an exact-match lookup of the `code_value` and `code_system` (ICD-10 or CPT) in the database. If the code is not found, the service queries the embedding vectors via pgvector cosine similarity to find up to 5 semantically similar codes as suggestions. If the code exists but is marked as deprecated (`IsDeprecated = true` on `CodeLibraryEntry`), the service returns a validation warning with the deprecated status and suggested replacement codes. The validation is invoked at the service layer when medical codes are submitted (e.g., during AI-assisted coding approval or manual code entry) and returns a structured `CodeValidationResult` with validity status, deprecation info, and alternative suggestions. This integrates with the `MedicalCode` entity (US_008) and the RAG embedding infrastructure (US_078).

## Dependent Tasks

- US_008 task_001_be_entity_models — Requires `MedicalCode` entity with `CodeType`, `CodeValue`, `Description`.
- US_008 task_002_be_efcore_configuration_migrations — Requires `MedicalCodeConfiguration` with composite index on `(PatientId, CodeType, CodeValue)`.
- US_078 task_002_be_knowledge_base_refresh_pipeline — Requires `CodeLibraryEntry` model and `RagKnowledgeBaseEntry` with embeddings for ICD-10/CPT codes.

## Impacted Components

- **NEW** `src/UPACIP.Service/Validation/MedicalCodeValidationService.cs` — Core validation: exact match lookup + deprecated check + embedding similarity suggestions
- **NEW** `src/UPACIP.Service/Validation/Models/CodeValidationResult.cs` — Result model: IsValid, IsDeprecated, ValidationMessage, SuggestedAlternatives
- **NEW** `src/UPACIP.Service/Validation/Models/CodeSuggestion.cs` — Alternative code DTO: CodeValue, CodeSystem, Description, SimilarityScore
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IMedicalCodeValidationService as scoped

## Implementation Plan

1. **Define `CodeValidationResult` model**: Create in `src/UPACIP.Service/Validation/Models/CodeValidationResult.cs`:
   - `bool IsValid` — true if code exists and is not deprecated.
   - `bool IsDeprecated` — true if code exists but is marked deprecated.
   - `string ValidationMessage` — human-readable validation outcome.
   - `string SubmittedCode` — the code that was validated.
   - `string SubmittedCodeSystem` — "ICD-10" or "CPT".
   - `IReadOnlyList<CodeSuggestion> SuggestedAlternatives` — up to 5 suggested alternative codes (populated when invalid or deprecated).

2. **Define `CodeSuggestion` model**: Create in `src/UPACIP.Service/Validation/Models/CodeSuggestion.cs`:
   - `string CodeValue` — e.g., "E11.65".
   - `string CodeSystem` — "ICD-10" or "CPT".
   - `string Description` — full text description of the code.
   - `double SimilarityScore` — cosine similarity score (0.0–1.0) from pgvector.
   
3. **Implement `IMedicalCodeValidationService` / `MedicalCodeValidationService`**: Create the service in `src/UPACIP.Service/Validation/MedicalCodeValidationService.cs` with constructor injection of `ApplicationDbContext`. Primary method: `Task<CodeValidationResult> ValidateCodeAsync(string codeValue, string codeSystem, CancellationToken ct)`.

   Validation flow:
   - (a) **Normalize input**: Trim whitespace, convert `codeValue` to uppercase, validate `codeSystem` is "ICD-10" or "CPT" (throw `ArgumentException` for unsupported code systems).
   - (b) **Exact match lookup**: Query `RagKnowledgeBaseEntry` table: `dbContext.RagKnowledgeBaseEntries.FirstOrDefaultAsync(e => e.CodeValue == normalizedCode && e.Category == category, ct)` where `category` maps "ICD-10" → `EmbeddingCategory.Icd10Codes` and "CPT" → `EmbeddingCategory.CptCodes`.
   - (c) **Code found and active**: Return `CodeValidationResult { IsValid = true, IsDeprecated = false, ValidationMessage = "Code {codeValue} is valid.", SuggestedAlternatives = [] }`.
   - (d) **Code found but deprecated**: Return `CodeValidationResult { IsValid = false, IsDeprecated = true, ValidationMessage = "Code {codeValue} is deprecated. Consider using one of the suggested alternatives.", SuggestedAlternatives = FindSimilarCodes(...) }`.
   - (e) **Code not found**: Return `CodeValidationResult { IsValid = false, IsDeprecated = false, ValidationMessage = "Code {codeValue} was not found in the {codeSystem} library. Did you mean one of these?", SuggestedAlternatives = FindSimilarCodes(...) }`.

4. **Implement embedding similarity search for suggestions**: Private method `FindSimilarCodesAsync(string codeValue, string codeSystem, CancellationToken ct)`:
   - (a) Compute the embedding vector for the submitted code value using the existing `IEmbeddingService.GenerateEmbeddingAsync(codeValue)` (from US_076/US_078 infrastructure).
   - (b) Query pgvector for the top 5 nearest neighbors by cosine distance within the same code system category:
     ```sql
     SELECT code_value, code_system, description, 
            1 - (embedding <=> @queryVector) AS similarity_score
     FROM rag_knowledge_base_entries
     WHERE category = @category 
       AND is_deprecated = false
     ORDER BY embedding <=> @queryVector
     LIMIT 5
     ```
   - (c) Execute via `dbContext.Database.SqlQueryRaw<CodeSuggestion>(...)` or via a raw SQL query with parameterized embedding vector.
   - (d) Filter results with a minimum similarity threshold of 0.5 — codes below this threshold are not relevant enough to suggest.
   - (e) Map results to `List<CodeSuggestion>`.

5. **Handle bulk code validation**: Add method `Task<IReadOnlyList<CodeValidationResult>> ValidateCodesAsync(IReadOnlyList<(string CodeValue, string CodeSystem)> codes, CancellationToken ct)`:
   - (a) Batch the exact-match lookups into a single query: `WHERE (code_value, category) IN (...)` to avoid N+1 queries.
   - (b) For codes that fail validation, compute embedding suggestions in parallel using `Task.WhenAll()` (bounded to max 5 concurrent similarity searches to avoid overloading the embedding service).
   - (c) Return results in the same order as the input list.
   This is used when the AI coding service suggests multiple codes at once (e.g., during clinical document coding).

6. **Register service in DI**: In `Program.cs`, add `builder.Services.AddScoped<IMedicalCodeValidationService, MedicalCodeValidationService>()`. The service is scoped because it holds a reference to the scoped `ApplicationDbContext`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   └── Middleware/
│   │       └── GlobalExceptionHandlerMiddleware.cs
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Validation/
│   │   │   ├── EmailValidator.cs                    ← from US_010
│   │   │   ├── AppointmentDateValidator.cs          ← from US_010
│   │   │   ├── SoftDeleteReferenceValidator.cs      ← from US_010
│   │   │   └── DuplicateBookingValidator.cs         ← from task_001
│   │   ├── Rag/
│   │   │   ├── IEmbeddingService.cs                 ← from US_076
│   │   │   ├── EmbeddingService.cs                  ← from US_076
│   │   │   └── Refresh/
│   │   │       └── Models/
│   │   │           └── CodeLibraryEntry.cs          ← from US_078
│   │   └── Caching/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── MedicalCode.cs                       ← from US_008
│       │   └── RagKnowledgeBaseEntry.cs             ← from US_076
│       └── Configurations/
│           └── MedicalCodeConfiguration.cs          ← from US_008
├── Server/
│   ├── Services/
│   └── AI/
│       └── AiGatewayService.cs                      ← from US_076
├── app/
├── config/
└── scripts/
```

> Assumes US_008 (entities), US_010 (FluentValidation), US_076 (embedding service), US_078 (RAG knowledge base refresh with CodeLibraryEntry), and task_001 are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Validation/MedicalCodeValidationService.cs | IMedicalCodeValidationService: exact match + deprecated check + similarity suggestions |
| CREATE | src/UPACIP.Service/Validation/Models/CodeValidationResult.cs | Validation result: IsValid, IsDeprecated, ValidationMessage, SuggestedAlternatives |
| CREATE | src/UPACIP.Service/Validation/Models/CodeSuggestion.cs | Alternative code DTO: CodeValue, CodeSystem, Description, SimilarityScore |
| MODIFY | src/UPACIP.Api/Program.cs | Register IMedicalCodeValidationService as scoped |

## External References

- [pgvector — Cosine Distance](https://github.com/pgvector/pgvector#distances)
- [Npgsql — pgvector Support](https://www.npgsql.org/doc/types/misc.html)
- [ICD-10-CM Code Format](https://www.cms.gov/medicare/coding-billing/icd-10-codes)
- [CPT Code Format](https://www.ama-assn.org/practice-management/cpt)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Valid ICD-10 code (e.g., "E11.65") returns `IsValid = true, IsDeprecated = false` with empty suggestions
- [ ] Valid CPT code (e.g., "99213") returns `IsValid = true, IsDeprecated = false`
- [ ] Deprecated code returns `IsValid = false, IsDeprecated = true` with up to 5 non-deprecated alternatives
- [ ] Invalid code returns `IsValid = false, IsDeprecated = false` with similar alternatives from pgvector
- [ ] Suggested alternatives have similarity score ≥ 0.5
- [ ] Bulk validation of 10 codes executes in a single DB roundtrip for exact-match plus bounded parallel similarity searches
- [ ] Unsupported code system (e.g., "SNOMED") throws ArgumentException
- [ ] Code value is case-insensitive (normalized to uppercase)
- [ ] Empty or whitespace-only code value returns validation error

## Implementation Checklist

- [ ] Create `CodeValidationResult` model with IsValid, IsDeprecated, ValidationMessage, SuggestedAlternatives
- [ ] Create `CodeSuggestion` model with CodeValue, CodeSystem, Description, SimilarityScore
- [ ] Implement `IMedicalCodeValidationService` / `MedicalCodeValidationService` with exact-match lookup
- [ ] Implement deprecated code detection with warning message and alternative suggestions
- [ ] Implement pgvector cosine similarity search for suggested alternatives (top 5, threshold ≥ 0.5)
- [ ] Implement bulk validation method with batched exact-match and bounded parallel similarity searches
- [ ] Register `IMedicalCodeValidationService` as scoped in Program.cs
