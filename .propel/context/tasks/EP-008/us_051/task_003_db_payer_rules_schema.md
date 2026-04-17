# Task - TASK_003_DB_PAYER_RULES_SCHEMA

## Requirement Reference
- User Story: US_051
- Story Location: .propel/context/tasks/EP-008/us_051/us_051.md
- Acceptance Criteria:
    - AC-1: Given ICD-10 and CPT codes are assigned to a patient, When payer validation runs, Then the system checks code combinations against payer-specific rules and flags potential claim denial risks.
    - AC-4: Given multi-code assignment is complete, When all codes are verified, Then the system validates the complete code set against bundling rules and modifier requirements.
- Edge Cases:
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
| Database | PostgreSQL | 16.x |
| ORM | Entity Framework Core | 8.x |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
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
Create the database schema, EF Core entity models, migration scripts, and seed data for payer-specific rules, bundling rules (NCCI edits), and modifier requirements. This task establishes the data foundation required by the backend payer validation service. It extends the existing `MedicalCode` entity with payer validation status fields and creates new tables for payer rules, bundling edits, and code combination validation results.

## Dependent Tasks
- US_008 tasks — Requires MedicalCode entity and base EF Core DbContext to be established

## Impacted Components
- **NEW**: `PayerRule` entity — Stores payer-specific code validation rules
- **NEW**: `BundlingEdit` entity — Stores NCCI procedure-to-procedure edit pairs
- **NEW**: `CodeModifier` entity — Stores applicable modifiers for code combinations
- **NEW**: `PayerRuleViolation` entity — Stores payer validation results per encounter
- **NEW**: EF Core migration for payer rules schema
- **NEW**: Seed data for CMS default rules and common NCCI edits
- **MODIFY**: `MedicalCode` entity — Add `payer_validation_status`, `bundling_check_result`, `sequence_order` columns
- **MODIFY**: `ApplicationDbContext` — Register new entity DbSets and configure relationships

## Implementation Plan
1. **Create `PayerRule` entity**: Define entity with columns: `rule_id` (UUID PK), `payer_id` (string, indexed), `payer_name` (string), `rule_type` (enum: combination_invalid, modifier_required, documentation_required, frequency_limit), `code_type` (enum: icd10, cpt), `primary_code` (string), `secondary_code` (string, nullable), `rule_description` (text), `denial_reason` (text), `corrective_action` (text), `severity` (enum: error, warning, info), `is_cms_default` (boolean), `effective_date` (date), `expiration_date` (date, nullable), `created_at`, `updated_at`. Add composite index on (`payer_id`, `primary_code`, `secondary_code`) for fast lookup.
2. **Create `BundlingEdit` entity**: Define entity with columns: `edit_id` (UUID PK), `column1_code` (string, indexed), `column2_code` (string, indexed), `edit_type` (enum: mutually_exclusive, component_part, standard), `modifier_allowed` (boolean), `allowed_modifiers` (string array / JSONB), `effective_date` (date), `expiration_date` (date, nullable), `source` (string — "NCCI" default), `created_at`. Add composite index on (`column1_code`, `column2_code`).
3. **Create `CodeModifier` entity**: Define entity with columns: `modifier_id` (UUID PK), `modifier_code` (string, unique — e.g., "59", "25", "76"), `modifier_description` (text), `applicable_code_types` (string array / JSONB — ["cpt"]), `documentation_required` (boolean), `created_at`.
4. **Create `PayerRuleViolation` entity**: Define entity with columns: `violation_id` (UUID PK), `patient_id` (FK → Patient), `encounter_date` (date), `rule_id` (FK → PayerRule), `violating_codes` (JSONB — array of code values), `severity` (enum), `resolution_status` (enum: pending, accepted, overridden, dismissed), `resolved_by_user_id` (FK → User, nullable), `resolution_justification` (text, nullable), `resolved_at` (timestamp, nullable), `created_at`. Add index on (`patient_id`, `encounter_date`).
5. **Extend `MedicalCode` entity**: Add columns: `payer_validation_status` (enum: not_validated, valid, warning, denied — default: not_validated), `bundling_check_result` (enum: not_checked, passed, failed — default: not_checked), `sequence_order` (int, default: 0 — billing priority).
6. **Create EF Core migration**: Generate migration with rollback support (per DR-029). Include foreign key constraints, indexes, and enum type mappings. Ensure backward-compatible with existing schema (DR-031).
7. **Create seed data**: Populate CMS default rules covering common denial scenarios (e.g., E/M code with procedure same-day, duplicate diagnosis codes, mutually exclusive procedure pairs). Seed ~20 common NCCI bundling edits and ~10 standard modifiers. Mark all seeded rules with `is_cms_default = true`.

## Current Project State
- [Placeholder — to be updated during task execution based on dependent task completion]

## Expected Changes
| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/PayerRule.cs | PayerRule entity with payer-specific code validation rules |
| CREATE | Server/Models/BundlingEdit.cs | BundlingEdit entity for NCCI procedure-to-procedure edit pairs |
| CREATE | Server/Models/CodeModifier.cs | CodeModifier entity for applicable modifiers |
| CREATE | Server/Models/PayerRuleViolation.cs | PayerRuleViolation entity for validation results per encounter |
| CREATE | Server/Models/Enums/PayerRuleType.cs | Enum: combination_invalid, modifier_required, documentation_required, frequency_limit |
| CREATE | Server/Models/Enums/BundlingEditType.cs | Enum: mutually_exclusive, component_part, standard |
| CREATE | Server/Models/Enums/PayerValidationStatus.cs | Enum: not_validated, valid, warning, denied |
| CREATE | Server/Models/Enums/BundlingCheckResult.cs | Enum: not_checked, passed, failed |
| CREATE | Server/Models/Enums/ViolationResolutionStatus.cs | Enum: pending, accepted, overridden, dismissed |
| CREATE | Server/Models/Enums/RuleSeverity.cs | Enum: error, warning, info |
| CREATE | Server/Data/Migrations/XXXXXX_AddPayerRulesSchema.cs | EF Core migration with rollback for all new tables and columns |
| CREATE | Server/Data/Seeds/PayerRuleSeedData.cs | Seed data for CMS default rules, NCCI edits, and standard modifiers |
| MODIFY | Server/Models/MedicalCode.cs | Add payer_validation_status, bundling_check_result, sequence_order columns |
| MODIFY | Server/Data/ApplicationDbContext.cs | Register PayerRule, BundlingEdit, CodeModifier, PayerRuleViolation DbSets and configure relationships |

## External References
- [EF Core 8 Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli) — Migration creation and rollback patterns
- [EF Core 8 Value Conversions](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions) — Enum to string/int conversions
- [PostgreSQL JSONB](https://www.postgresql.org/docs/16/datatype-json.html) — JSONB column for allowed_modifiers and violating_codes
- [CMS NCCI Edits Data Files](https://www.cms.gov/medicare/coding-billing/national-correct-coding-initiative-edits) — Source data for bundling edit seed records
- [ICD-10-CM Code Structure](https://www.cms.gov/medicare/coding-billing/icd-10-codes) — Code format reference for validation constraints

## Build Commands
- `cd Server && dotnet ef migrations add AddPayerRulesSchema` — Create migration
- `cd Server && dotnet ef database update` — Apply migration
- `cd Server && dotnet ef migrations script --idempotent` — Generate idempotent SQL script
- `cd Server && dotnet build` — Build backend

## Implementation Validation Strategy
- [ ] Unit tests pass
- [ ] Migration applies successfully with `dotnet ef database update`
- [ ] Migration rolls back successfully with `dotnet ef database update <previous_migration>`
- [ ] All foreign key constraints enforced (patient_id, rule_id, resolved_by_user_id)
- [ ] Composite indexes created on (`payer_id`, `primary_code`, `secondary_code`) and (`column1_code`, `column2_code`)
- [ ] Enum values map correctly to database storage (string or int per convention)
- [ ] JSONB columns (`allowed_modifiers`, `violating_codes`) accept valid JSON and reject invalid
- [ ] Seed data loads CMS default rules with `is_cms_default = true`
- [ ] MedicalCode entity retains backward compatibility — existing records default to `not_validated` / `not_checked`
- [ ] Data integrity validated — no orphaned records after migration

## Implementation Checklist
- [ ] Create `PayerRule` entity with composite index on (payer_id, primary_code, secondary_code)
- [ ] Create `BundlingEdit` entity with composite index on (column1_code, column2_code)
- [ ] Create `CodeModifier` entity with unique constraint on modifier_code
- [ ] Create `PayerRuleViolation` entity with FK constraints to Patient, PayerRule, and User
- [ ] Create enum types: PayerRuleType, BundlingEditType, PayerValidationStatus, BundlingCheckResult, ViolationResolutionStatus, RuleSeverity
- [ ] Extend `MedicalCode` entity with payer_validation_status, bundling_check_result, sequence_order columns
- [ ] Generate EF Core migration with transaction block and rollback support
- [ ] Create seed data for ~20 CMS default rules, ~20 NCCI bundling edits, and ~10 standard modifiers
