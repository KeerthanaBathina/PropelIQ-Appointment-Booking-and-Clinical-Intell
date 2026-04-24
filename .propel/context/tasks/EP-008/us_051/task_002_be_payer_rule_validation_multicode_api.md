# Task - TASK_002_BE_PAYER_RULE_VALIDATION_MULTICODE_API

## Requirement Reference
- User Story: US_051
- Story Location: .propel/context/tasks/EP-008/us_051/us_051.md
- Acceptance Criteria:
    - AC-1: Given ICD-10 and CPT codes are assigned to a patient, When payer validation runs, Then the system checks code combinations against payer-specific rules and flags potential claim denial risks.
    - AC-2: Given a claim denial risk is flagged, When the staff member views the alert, Then the system displays the specific rule violation and suggests corrective actions.
    - AC-3: Given clinical documentation supports multiple billable diagnoses, When the coding workflow runs, Then the system supports multi-code assignment with each code individually verified.
    - AC-4: Given multi-code assignment is complete, When all codes are verified, Then the system validates the complete code set against bundling rules and modifier requirements.
- Edge Cases:
    - When payer rules conflict with clinical documentation: System flags the conflict, shows both the clinical rationale and payer rule, and lets the staff member decide.
    - When payer rules are unknown or new: System applies general CMS rules as default and flags the encounter for manual payer rule verification.

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
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Frontend | N/A | - |
| AI/ML | N/A | - |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)
| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)
| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview
Implement the backend API layer for payer rule validation and multi-code assignment. This includes the Coding Service methods for validating code combinations against payer-specific rules, detecting claim denial risks, generating corrective action suggestions, supporting multi-code assignment with individual code verification, and enforcing bundling rules and modifier requirements. The service integrates with the existing CodingSvc and MedicalCode entity from design.md.

## Dependent Tasks
- task_003_db_payer_rules_schema.md — Requires payer rules, bundling rules, and modifier tables to exist in the database
- US_049 tasks — Requires base coding service with code suggestion and verification workflows
- US_008 tasks — Requires MedicalCode entity and EF Core model

## Impacted Components
- **NEW**: `IPayerRuleValidationService` / `PayerRuleValidationService` — Service for validating code combinations against payer rules
- **NEW**: `PayerRuleValidationController` — API controller exposing payer validation endpoints
- **NEW**: `MultiCodeAssignmentService` — Service for managing multi-code assignment and individual code verification
- **NEW**: DTOs: `PayerValidationResultDto`, `ClaimDenialRiskDto`, `CorrectiveActionDto`, `MultiCodeAssignmentDto`, `BundlingRuleResultDto`
- **MODIFY**: `CodingService` — Integrate payer rule validation into existing coding workflow pipeline
- **MODIFY**: `MedicalCode` entity — Add payer_validation_status and bundling_check_result fields

## Implementation Plan
1. **Create `PayerRuleValidationService`**: Implement `IPayerRuleValidationService` with methods: `ValidateCodeCombinationsAsync(patientId, payerId, codes)` — validates ICD-10 + CPT code combinations against payer-specific rules loaded from the `PayerRule` table. Returns a list of `PayerValidationResultDto` with severity (Error/Warning/Info), rule_id, description, affected codes, and suggested corrective actions. When payer rules are not found, apply CMS default rules and flag the encounter for manual verification.
2. **Create claim denial risk detection**: Implement `DetectClaimDenialRisksAsync(codes, payerId)` — checks code pairs against known denial patterns. Return `ClaimDenialRiskDto` with risk level (high/medium/low), denial reason, historical denial rate for the code combination, and list of `CorrectiveActionDto` (alternative code suggestions, modifier additions, documentation requirements).
3. **Create multi-code assignment service**: Implement `MultiCodeAssignmentService` with `AssignMultipleCodesAsync(patientId, codes)` — assigns multiple ICD-10/CPT codes to a single encounter. Each code is validated individually via `DR-015` (ICD-10/CPT library validation) and stored with `suggested_by_ai` and `approved_by_user_id` fields. Support billing priority ordering with a `sequence_order` field.
4. **Create bundling rule validation**: Implement `ValidateBundlingRulesAsync(codes)` — checks the complete code set against NCCI bundling edits. Detect code pairs that cannot be billed together and suggest applicable modifiers (e.g., modifier 59 for distinct procedures, modifier 25 for separate E/M). Return `BundlingRuleResultDto` with violating code pairs, required modifiers, and CCI edit type (column 1/column 2).
5. **Create API endpoints**: Add `PayerRuleValidationController` with:
   - `GET /api/coding/payer-rules/{patientId}` — Get payer validation results for a patient's assigned codes
   - `POST /api/coding/multi-assign` — Assign multiple codes to an encounter with individual verification
   - `POST /api/coding/validate-bundling` — Validate complete code set against bundling rules
   - `POST /api/coding/resolve-conflict` — Record staff decision when payer rule conflicts with clinical documentation
6. **Integrate with existing CodingSvc pipeline**: Hook payer validation into the post-code-suggestion step in the existing coding workflow (after AI suggests codes, before staff review). Auto-trigger `ValidateCodeCombinationsAsync` and `ValidateBundlingRulesAsync` when codes are assigned or approved.
7. **Implement caching for payer rules**: Cache frequently accessed payer rule sets in Redis with 5-minute TTL (per NFR-030). Cache key pattern: `payer-rules:{payerId}`. Invalidate on payer rule updates.
8. **Add audit logging**: Log all payer validation events, multi-code assignments, conflict resolutions, and override decisions with user attribution via the existing AuditService (per FR-066, NFR-012).

## Current Project State
- [Placeholder — to be updated during task execution based on dependent task completion]

## Expected Changes
| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IPayerRuleValidationService.cs | Interface for payer rule validation operations |
| CREATE | Server/Services/PayerRuleValidationService.cs | Implementation of payer rule validation, denial risk detection, and corrective actions |
| CREATE | Server/Services/MultiCodeAssignmentService.cs | Service for multi-code assignment with individual code verification |
| CREATE | Server/Controllers/PayerRuleValidationController.cs | API controller with endpoints for payer validation, multi-assign, bundling check, conflict resolution |
| CREATE | Server/DTOs/PayerValidationResultDto.cs | DTO for payer validation results with severity, rule details, and corrective actions |
| CREATE | Server/DTOs/ClaimDenialRiskDto.cs | DTO for claim denial risk details and historical denial rates |
| CREATE | Server/DTOs/CorrectiveActionDto.cs | DTO for suggested corrective actions (alternative codes, modifiers) |
| CREATE | Server/DTOs/MultiCodeAssignmentDto.cs | DTO for multi-code assignment request and response |
| CREATE | Server/DTOs/BundlingRuleResultDto.cs | DTO for bundling rule validation results |
| MODIFY | Server/Services/CodingService.cs | Integrate payer validation into post-code-suggestion pipeline |
| MODIFY | Server/Models/MedicalCode.cs | Add payer_validation_status and bundling_check_result properties |

## External References
- [ASP.NET Core 8 Web API Controllers](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0) — Controller patterns for API endpoints
- [Entity Framework Core 8 Querying](https://learn.microsoft.com/en-us/ef/core/querying/) — EF Core query patterns for payer rules lookup
- [CMS NCCI Procedure-to-Procedure Edits](https://www.cms.gov/medicare/coding-billing/national-correct-coding-initiative-edits) — Bundling rules reference data
- [CMS ICD-10-CM Official Guidelines](https://www.cms.gov/medicare/coding-billing/icd-10-codes) — Multi-code assignment rules
- [Polly Resilience Library](https://github.com/App-vNext/Polly) — Retry and circuit breaker patterns for service calls
- [StackExchange.Redis Documentation](https://stackexchange.github.io/StackExchange.Redis/) — Redis caching for payer rules

## Build Commands
- `cd Server && dotnet build` — Build backend
- `cd Server && dotnet test` — Run unit tests

## Implementation Validation Strategy
- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Payer rule validation returns correct violations for known invalid code combinations
- [ ] Claim denial risk detection identifies high/medium/low risk codes with corrective actions
- [ ] Multi-code assignment correctly stores multiple codes per encounter with individual verification status
- [ ] Bundling rule validation detects NCCI edit violations and suggests appropriate modifiers
- [ ] CMS default rules applied when payer-specific rules are unavailable (edge case)
- [ ] Conflict resolution endpoint records staff decision with audit trail
- [ ] Redis caching works for payer rules with 5-minute TTL
- [ ] All endpoints return proper HTTP status codes (200, 400, 404, 500)
- [ ] Audit logs created for all validation, assignment, and override events
- [ ] API endpoints validate and sanitize all input parameters (NFR-018)
- [ ] Idempotent endpoints for state-changing operations (NFR-034)

## Implementation Checklist
- [x] Create `IPayerRuleValidationService` interface with `ValidateCodeCombinationsAsync`, `DetectClaimDenialRisksAsync`, `ValidateBundlingRulesAsync` methods
- [x] Implement `PayerRuleValidationService` with payer rule lookup, CMS default fallback, and corrective action generation
- [x] Implement `MultiCodeAssignmentService` with individual code verification and billing priority ordering
- [x] Create DTOs: `PayerValidationResultDto`, `ClaimDenialRiskDto`, `CorrectiveActionDto`, `MultiCodeAssignmentDto`, `BundlingRuleResultDto`
- [x] Create `PayerRuleValidationController` with 4 endpoints (GET payer-rules, POST multi-assign, POST validate-bundling, POST resolve-conflict)
- [x] Integrate payer validation into existing `CodingService` post-code-suggestion pipeline
- [x] Add Redis caching for payer rule sets with 5-minute TTL and cache invalidation
- [x] Add audit logging for all payer validation, assignment, and conflict resolution events
