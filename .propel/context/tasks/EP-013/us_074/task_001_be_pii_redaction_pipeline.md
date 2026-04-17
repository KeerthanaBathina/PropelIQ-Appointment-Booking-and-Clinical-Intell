# Task - task_001_be_pii_redaction_pipeline

## Requirement Reference

- User Story: us_074
- Story Location: .propel/context/tasks/EP-013/us_074/us_074.md
- Acceptance Criteria:
  - AC-3: Given patient data is sent to external AI providers, When the request is prepared, Then all PII (name, DOB, SSN, phone, email, address) is redacted before transmission.
  - AC-4: Given PII is redacted for AI processing, When results are returned, Then the system re-associates results with the original patient context using internal reference IDs only.
- Edge Case:
  - How does the system handle medical terminology that resembles PII (e.g., a drug named after a person)? PII redaction uses context-aware rules that skip recognized medical terminology.

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
| Database | PostgreSQL | 16.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-S01 |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | OpenAI GPT-4o-mini / Anthropic Claude 3.5 Sonnet |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement a PII redaction pipeline that intercepts all outbound requests from the AI Gateway to external AI providers (OpenAI, Anthropic). The pipeline: (1) detects and redacts six PII categories (name, DOB, SSN, phone, email, address) from prompt text before transmission per AIR-S01; (2) replaces PII with deterministic placeholder tokens (e.g., `[NAME_1]`, `[DOB_1]`) and maintains a per-request mapping table; (3) on response receipt, re-associates AI results with the original patient context using internal reference IDs only — never transmitting real patient identifiers to external APIs; (4) applies context-aware rules with a medical terminology allowlist to prevent false-positive redaction of drug names, conditions, or procedures that resemble personal names per edge case.

## Dependent Tasks

- US_067 — Requires AI Gateway service foundation for request/response interception pipeline.
- US_008 task_001_be_domain_entity_models — Requires `Patient` entity for PII field definitions.

## Impacted Components

- **NEW** `src/UPACIP.Service/AiSafety/IPiiRedactionService.cs` — Service interface for PII detection, redaction, and re-association
- **NEW** `src/UPACIP.Service/AiSafety/PiiRedactionService.cs` — Implementation: regex-based PII detection, placeholder tokenization, mapping management
- **NEW** `src/UPACIP.Service/AiSafety/PiiRedactionContext.cs` — Per-request context holding token-to-PII mappings for response re-association
- **NEW** `src/UPACIP.Service/AiSafety/MedicalTermAllowlist.cs` — Static allowlist of medical terms exempt from PII redaction rules
- **NEW** `src/UPACIP.Service/AiSafety/PiiRedactionMiddleware.cs` — AI Gateway middleware that wraps outbound requests with redaction and inbound responses with re-association
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register `IPiiRedactionService` in DI container
- **MODIFY** AI Gateway service pipeline — Insert `PiiRedactionMiddleware` into outbound request chain

## Implementation Plan

1. **Define PII detection patterns**: Create regex patterns for six PII categories per AC-3. Name: pattern matching against patient `FullName` from the request context (exact match + fuzzy token match). DOB: `\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b` and ISO 8601 date patterns. SSN: `\b\d{3}-?\d{2}-?\d{4}\b`. Phone: `\b(\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b`. Email: RFC 5322 simplified pattern `\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b`. Address: structured pattern matching street numbers, common suffixes (St, Ave, Rd, Blvd) and 5-digit zip codes.

2. **Implement `PiiRedactionContext`**: A per-request object storing `Dictionary<string, string>` mapping placeholder tokens to original PII values. Tokens follow the pattern `[CATEGORY_N]` where CATEGORY is NAME, DOB, SSN, PHONE, EMAIL, ADDRESS and N is a sequential counter per request. The context also stores the internal `PatientId` (Guid) used for re-association. The context is scoped to the request lifetime and never persisted to database or logs.

3. **Implement medical terminology allowlist (edge case)**: Create `MedicalTermAllowlist` with a static `HashSet<string>` of known medical terms that resemble personal names (e.g., "Addison" as in Addison's disease, "Cushing" as in Cushing's syndrome, "Hodgkin" as in Hodgkin's lymphoma). Load from a configuration file at `config/medical-term-allowlist.json` for maintainability. Before applying name-pattern redaction, check if the matched term exists in the allowlist; if found, skip redaction for that occurrence.

4. **Implement `IPiiRedactionService` interface**: Methods: `PiiRedactionContext RedactPii(string inputText, Patient patientContext)` — scans input text for PII, replaces with tokens, returns redaction context; `string RestorePii(string aiResponseText, PiiRedactionContext context)` — replaces placeholder tokens in AI response with original values for internal storage; `bool ContainsPii(string text)` — validation check confirming no residual PII in outbound text.

5. **Implement `PiiRedactionService`**: In `RedactPii`, iterate through PII categories in priority order (SSN first — most specific, then email, phone, DOB, address, name — most ambiguous last to avoid false positives). For each match: (a) check medical terminology allowlist; (b) if not in allowlist, replace with `[CATEGORY_N]` token; (c) store mapping in `PiiRedactionContext`. In `RestorePii`, iterate through the mapping dictionary and replace tokens back to original values. In `ContainsPii`, run all regex patterns against the text and return true if any match — used as a post-redaction validation gate.

6. **Implement `PiiRedactionMiddleware`**: Create a middleware component that plugs into the AI Gateway request pipeline. On outbound request: (a) extract patient context from the request metadata; (b) call `RedactPii` on the prompt text; (c) call `ContainsPii` as a validation gate — if PII still detected, log a critical error and block the request; (d) attach the `PiiRedactionContext` to the request scope for response processing. On inbound response: (a) retrieve `PiiRedactionContext` from request scope; (b) do NOT call `RestorePii` on the response text sent to storage — instead, store AI results referencing the internal `PatientId` only per AC-4; (c) log redaction event (number of tokens redacted per category) via Serilog without logging actual PII values.

7. **Implement re-association logic (AC-4)**: AI responses reference patients exclusively via internal `PatientId` (Guid). The `PiiRedactionMiddleware` attaches the `PatientId` from the original request context to the AI response processing pipeline. Downstream services (document parsing, medical coding) use this `PatientId` to associate extracted data with `ExtractedData` and `MedicalCode` entities. PII placeholder tokens in AI output are stripped (not restored) because the AI should never need to reference patient names — only clinical content.

8. **Register service and configure middleware**: Add `services.AddScoped<IPiiRedactionService, PiiRedactionService>()` in `Program.cs`. Insert `PiiRedactionMiddleware` into the AI Gateway outbound pipeline before the HTTP client handler. Load medical terminology allowlist from `config/medical-term-allowlist.json` via `IConfiguration`.

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
│   │   └── UPACIP.Service.csproj
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── Patient.cs
│       │   ├── ExtractedData.cs
│       │   └── MedicalCode.cs
│       └── Enums/
├── app/
├── config/
└── scripts/
```

> Assumes US_067 (AI Gateway) and US_008 (domain entities) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/AiSafety/IPiiRedactionService.cs | Interface: RedactPii, RestorePii, ContainsPii |
| CREATE | src/UPACIP.Service/AiSafety/PiiRedactionService.cs | Regex-based PII detection, placeholder tokenization, allowlist integration |
| CREATE | src/UPACIP.Service/AiSafety/PiiRedactionContext.cs | Per-request token-to-PII mapping with PatientId for re-association |
| CREATE | src/UPACIP.Service/AiSafety/MedicalTermAllowlist.cs | Static HashSet loaded from config for context-aware redaction |
| CREATE | src/UPACIP.Service/AiSafety/PiiRedactionMiddleware.cs | AI Gateway middleware: outbound redaction, validation gate, inbound re-association |
| CREATE | config/medical-term-allowlist.json | JSON array of medical terms exempt from name-pattern PII redaction |
| MODIFY | src/UPACIP.Api/Program.cs | Register IPiiRedactionService and configure middleware in AI Gateway pipeline |

## External References

- [HIPAA Safe Harbor De-identification](https://www.hhs.gov/hipaa/for-professionals/privacy/special-topics/de-identification/index.html)
- [Regex for PII Detection Patterns](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference)
- [ASP.NET Core Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-8.0)
- [Serilog Structured Logging](https://serilog.net/)
- [OWASP Data Privacy Guidelines](https://owasp.org/www-project-top-ten/)

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

- [ ] `dotnet build` completes with zero errors for UPACIP.Service and UPACIP.Api projects
- [ ] `PiiRedactionService.RedactPii` correctly replaces all 6 PII categories with placeholder tokens
- [ ] `PiiRedactionService.ContainsPii` returns false after successful redaction (validation gate)
- [ ] Medical terminology allowlist prevents false-positive redaction of known medical terms
- [ ] `PiiRedactionMiddleware` blocks outbound request if post-redaction validation detects residual PII
- [ ] AI responses are associated with patients via internal `PatientId` only — no PII in AI-processed output
- [ ] Redaction event logging records token counts per category without logging actual PII values
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)

## Implementation Checklist

- [ ] Define regex patterns for 6 PII categories: name (exact + fuzzy), DOB, SSN, phone, email, address
- [ ] Implement `PiiRedactionContext` with `Dictionary<string, string>` token-to-PII mapping scoped per request, never persisted
- [ ] Create `MedicalTermAllowlist` loaded from `config/medical-term-allowlist.json` with known medical terms resembling personal names
- [ ] Implement `IPiiRedactionService` with `RedactPii` (priority-ordered detection), `RestorePii` (token replacement), `ContainsPii` (validation gate)
- [ ] Implement `PiiRedactionMiddleware` for AI Gateway: outbound redaction with validation gate, inbound re-association via internal `PatientId`
- [ ] Implement re-association logic: attach `PatientId` from request context, strip PII tokens from AI output, associate results via Guid only
- [ ] Create `config/medical-term-allowlist.json` with initial set of medical eponyms (Addison, Cushing, Hodgkin, etc.)
- [ ] Register `IPiiRedactionService` in DI and insert `PiiRedactionMiddleware` into AI Gateway outbound pipeline
- **[AI Tasks - MANDATORY]** Verify AIR-S01 (PII redaction before external API calls) requirement is met
