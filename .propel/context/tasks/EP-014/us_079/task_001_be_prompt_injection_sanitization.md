# Task - task_001_be_prompt_injection_sanitization

## Requirement Reference

- User Story: us_079
- Story Location: .propel/context/tasks/EP-014/us_079/us_079.md
- Acceptance Criteria:
  - AC-1: Given user input is received for AI intake, When it is preprocessed, Then prompt injection patterns are detected and sanitized before being included in the AI prompt.
- Edge Case:
  - What happens when legitimate medical text triggers the prompt injection detector? System uses a medical terminology allowlist to reduce false positives from clinical terms.

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
| Caching | Upstash Redis | 7.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-S06, AIR-S04 |
| **AI Pattern** | RAG |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | config/prompt-injection-patterns.json |
| **Model Provider** | N/A (pattern-based detection, no LLM inference) |

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

Implement a prompt injection detection and sanitization pipeline (`IPromptInjectionDetector` / `PromptInjectionDetector`) in the UPACIP.Service AiSafety namespace that intercepts all user-supplied text before it is included in AI prompts per AIR-S06. The detector uses a layered pattern-matching approach to identify common prompt injection categories: role impersonation ("ignore previous instructions", "you are now"), instruction override ("system:", "assistant:"), data exfiltration ("output all previous", "list all patients"), and delimiter injection (triple backtick escaping, XML/HTML tag injection). Detection patterns are loaded from an external configuration file (`config/prompt-injection-patterns.json`) for maintainability without redeployment. To address the edge case where legitimate medical terminology triggers false positives (e.g., "the patient should ignore previous medication" or "system review of symptoms"), the detector integrates with the existing `MedicalTermAllowlist` (US_074 task_001) and adds a clinical context scoring heuristic that evaluates whether the flagged phrase appears within a medical context window. Detected injections are sanitized by escaping control characters and stripping dangerous patterns while preserving clinical content. All detection events are logged to the audit trail per AIR-S04 with the injection category, sanitized input, and user ID — but never the raw unsanitized input containing potential attack payloads. A `PromptSanitizationMiddleware` integrates the detector into the AI Gateway request pipeline, ensuring no unsanitized user input reaches downstream prompt assembly.

## Dependent Tasks

- US_067 — Requires AI Gateway service for middleware pipeline integration.
- US_074 task_001_be_pii_redaction_pipeline — Requires `MedicalTermAllowlist` for false-positive reduction on clinical terms.

## Impacted Components

- **NEW** `src/UPACIP.Service/AiSafety/IPromptInjectionDetector.cs` — Interface defining DetectAsync, SanitizeAsync methods
- **NEW** `src/UPACIP.Service/AiSafety/PromptInjectionDetector.cs` — Pattern-based detection engine with medical context scoring
- **NEW** `src/UPACIP.Service/AiSafety/Models/InjectionDetectionResult.cs` — Result DTO: IsInjectionDetected, DetectedPatterns, SanitizedText, RiskScore
- **NEW** `src/UPACIP.Service/AiSafety/Models/InjectionPattern.cs` — Pattern definition: Category, Regex, Severity, Description
- **NEW** `src/UPACIP.Service/AiSafety/PromptSanitizationMiddleware.cs` — AI Gateway middleware that intercepts and sanitizes user inputs
- **NEW** `config/prompt-injection-patterns.json` — Externalized injection pattern definitions (role impersonation, instruction override, data exfiltration, delimiter injection)
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IPromptInjectionDetector in DI container and insert PromptSanitizationMiddleware into AI Gateway pipeline

## Implementation Plan

1. **Define injection pattern categories and model**: Create `InjectionPattern` with: `string Category` (enum-backed: RoleImpersonation, InstructionOverride, DataExfiltration, DelimiterInjection), `string RegexPattern` (compiled regex), `InjectionSeverity Severity` (Low, Medium, High, Critical), `string Description` (human-readable explanation for audit logs). Create `InjectionDetectionResult` with: `bool IsInjectionDetected`, `IReadOnlyList<DetectedPattern> DetectedPatterns` (list of matched patterns with match position and category), `string SanitizedText` (cleaned input), `float RiskScore` (0.0–1.0 aggregate severity), `bool WasMedicalFalsePositive` (true if pattern was matched but suppressed by medical context check).

2. **Create externalized pattern configuration**: Create `config/prompt-injection-patterns.json` with four categories:
   - **RoleImpersonation** (Critical): `ignore\s+(all\s+)?previous\s+instructions`, `you\s+are\s+now\s+a`, `pretend\s+(you\s+are|to\s+be)`, `act\s+as\s+(if|a)`, `forget\s+(everything|all)\s+(above|previous)`.
   - **InstructionOverride** (High): `^system\s*:`, `^assistant\s*:`, `\[INST\]`, `<<SYS>>`, `\[\/INST\]`, `<\|im_start\|>`.
   - **DataExfiltration** (Critical): `(output|print|show|list|reveal|display)\s+(all|every|the)\s+(previous|prior|above|system)`, `(what\s+is|tell\s+me)\s+your\s+(system\s+)?prompt`, `repeat\s+(the|your)\s+(instructions|prompt)`.
   - **DelimiterInjection** (High): triple backtick sequences not preceded by "code" context, unmatched `<script>` or `<iframe>` tags, `\x00`–`\x1F` control characters (except `\n`, `\t`).
   Load patterns via `IOptions<List<InjectionPattern>>` bound from configuration at startup with hot-reload support.

3. **Define `IPromptInjectionDetector` interface**: Two methods:
   - `Task<InjectionDetectionResult> DetectAsync(string userInput, CancellationToken cancellationToken = default)` — Scans input against all patterns, returns detection result with matched patterns and risk score.
   - `Task<string> SanitizeAsync(string userInput, CancellationToken cancellationToken = default)` — Convenience method: detects and returns sanitized text (strips dangerous patterns, escapes control characters). Logs detection events.

4. **Implement medical context scoring (edge case)**: When a pattern match is found, before flagging it as an injection, evaluate the surrounding context window (±50 characters around the match). Check if: (a) the matched phrase contains terms from `MedicalTermAllowlist` (e.g., "system" in "review of systems", "ignore" in "ignore previous medication if allergic"); (b) the context window contains medical terminology indicators (ICD-10 codes like `[A-Z]\d{2}\.\d+`, drug names from allowlist, anatomical terms). If medical context score exceeds 0.7, mark the detection as `WasMedicalFalsePositive = true` and skip sanitization for that match. Log the false-positive suppression event for review. This ensures clinical phrases like "the patient should ignore previous dosage instructions" are not incorrectly flagged.

5. **Implement `PromptInjectionDetector`**: In `DetectAsync`, iterate through loaded patterns in severity order (Critical first). For each regex pattern, scan the input text using compiled `Regex` with `RegexOptions.IgnoreCase | RegexOptions.Compiled` and a 100ms timeout per pattern (prevent ReDoS). For each match: (a) extract the matched text and position; (b) run medical context scoring; (c) if not a false positive, add to `DetectedPatterns` list. Calculate aggregate `RiskScore` as the maximum severity weight among detected patterns (Critical=1.0, High=0.8, Medium=0.5, Low=0.2). In `SanitizeAsync`, call `DetectAsync` first, then for each non-false-positive match: strip the matched text and replace with `[SANITIZED]` placeholder. Additionally, strip all ASCII control characters (`\x00`–`\x1F` except `\n`, `\t`, `\r`) and normalize Unicode to NFC form to prevent homoglyph attacks.

6. **Implement `PromptSanitizationMiddleware`**: Create a middleware component for the AI Gateway request pipeline. On each outbound AI request: (a) extract the user-input portion of the prompt (distinct from system prompt and grounding context which are trusted); (b) call `IPromptInjectionDetector.DetectAsync`; (c) if `IsInjectionDetected` and `RiskScore >= 0.8` (High/Critical), block the request entirely, return a structured error `{ "error": "Input rejected for safety reasons", "code": "PROMPT_INJECTION_DETECTED" }`, and log a security audit event via Serilog with `LogLevel.Warning` including user ID, injection category, and sanitized (not raw) input; (d) if `RiskScore < 0.8` (Low/Medium), sanitize the input and proceed with the cleaned text; (e) if no injection detected, pass through unchanged. The middleware must execute before `PiiRedactionMiddleware` (US_074) in the pipeline since injection detection operates on raw user text.

7. **Implement audit logging per AIR-S04**: All detection events (both blocked and sanitized-and-passed) are logged to the structured audit trail via Serilog. Log fields: `UserId`, `Timestamp`, `InjectionCategory`, `RiskScore`, `Action` (Blocked/Sanitized/FalsePositiveSuppressed), `SanitizedInput` (never raw input). Use `ILogger<PromptInjectionDetector>` with structured logging: `_logger.LogWarning("Prompt injection detected: {Category} (Risk: {RiskScore}) for user {UserId}. Action: {Action}", ...)`. For false-positive suppressions, log at `Information` level to enable pattern tuning.

8. **Register services and configure middleware ordering**: Add `services.AddSingleton<IPromptInjectionDetector, PromptInjectionDetector>()` in `Program.cs` (singleton since patterns are loaded once and stateless). Bind `config/prompt-injection-patterns.json` to `IOptions<List<InjectionPattern>>`. Insert `PromptSanitizationMiddleware` into the AI Gateway pipeline as the first middleware (before PII redaction, before token budget enforcement). Configure hot-reload for pattern updates via `IOptionsMonitor<List<InjectionPattern>>` so new patterns take effect without restart.

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
│   │   │   └── PiiRedactionMiddleware.cs          ← from US_074 task_001
│   │   ├── Caching/
│   │   ├── VectorSearch/
│   │   └── Rag/
│   │       ├── IRagRetrievalService.cs
│   │       ├── RagRetrievalService.cs
│   │       ├── IHybridSearchOrchestrator.cs       ← from US_078 task_001
│   │       ├── HybridSearchOrchestrator.cs        ← from US_078 task_001
│   │       ├── Chunking/
│   │       ├── Embedding/
│   │       ├── Refresh/                           ← from US_078 task_002
│   │       └── Models/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       └── Entities/
├── Server/
│   └── AI/
│       └── AiGatewayService.cs                    ← from US_067
├── app/
├── config/
│   └── medical-term-allowlist.json                ← from US_074 task_001
└── scripts/
```

> Assumes US_067 (AI Gateway), US_074 task_001 (PII redaction + MedicalTermAllowlist), US_078 (hybrid search + refresh) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/AiSafety/IPromptInjectionDetector.cs | Interface: DetectAsync, SanitizeAsync methods |
| CREATE | src/UPACIP.Service/AiSafety/PromptInjectionDetector.cs | Pattern-based detection with medical context scoring, ReDoS-safe regex with timeout |
| CREATE | src/UPACIP.Service/AiSafety/Models/InjectionDetectionResult.cs | Result DTO: IsInjectionDetected, DetectedPatterns, SanitizedText, RiskScore |
| CREATE | src/UPACIP.Service/AiSafety/Models/InjectionPattern.cs | Pattern DTO: Category, RegexPattern, Severity, Description |
| CREATE | src/UPACIP.Service/AiSafety/PromptSanitizationMiddleware.cs | AI Gateway middleware: detect, block/sanitize, audit log |
| CREATE | config/prompt-injection-patterns.json | Externalized regex patterns for 4 injection categories |
| MODIFY | src/UPACIP.Api/Program.cs | Register IPromptInjectionDetector, bind pattern config, insert middleware |

## External References

- [OWASP LLM Top 10 — LLM01: Prompt Injection](https://genai.owasp.org/llmrisk/llm01-prompt-injection/)
- [NIST AI 100-2 — Adversarial Machine Learning](https://csrc.nist.gov/pubs/ai/100/2/e2023/final)
- [System.Text.RegularExpressions — Regex Timeout](https://learn.microsoft.com/en-us/dotnet/standard/base-types/best-practices-regex#use-time-out-values)
- [ASP.NET Core Middleware Pipeline](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware)
- [Serilog Structured Logging](https://serilog.net/)
- [Unicode NFC Normalization](https://learn.microsoft.com/en-us/dotnet/api/system.string.normalize)

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
- [ ] Prompt injection patterns detect all four categories (role impersonation, instruction override, data exfiltration, delimiter injection)
- [ ] Medical terminology false positives are correctly suppressed by context scoring (e.g., "review of systems" not flagged)
- [ ] High/Critical injections (RiskScore >= 0.8) are blocked with structured error response
- [ ] Low/Medium injections are sanitized and the request proceeds with cleaned text
- [ ] Regex patterns execute within 100ms timeout to prevent ReDoS
- [ ] Audit log entries contain injection category and sanitized input but never raw attack payloads

## Implementation Checklist

- [ ] Create `InjectionPattern` and `InjectionDetectionResult` models in `src/UPACIP.Service/AiSafety/Models/`
- [ ] Create `config/prompt-injection-patterns.json` with regex patterns for all four injection categories
- [ ] Define `IPromptInjectionDetector` interface with `DetectAsync` and `SanitizeAsync` methods
- [ ] Implement `PromptInjectionDetector` with compiled regex scanning, severity-ordered evaluation, and 100ms regex timeout
- [ ] Implement medical context scoring to suppress false positives using `MedicalTermAllowlist` and ±50-char context window
- [ ] Implement `PromptSanitizationMiddleware` with block/sanitize decision based on RiskScore threshold
- [ ] Register services in DI and configure middleware ordering (before PII redaction)
- [ ] Implement audit logging for all detection events (blocked, sanitized, false-positive suppressed)
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-S06 and AIR-S04 requirements are met (prompt injection sanitization and audit logging)
