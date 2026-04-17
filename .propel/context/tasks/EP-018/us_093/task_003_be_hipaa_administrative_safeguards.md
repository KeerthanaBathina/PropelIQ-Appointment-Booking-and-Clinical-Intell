# Task - task_003_be_hipaa_administrative_safeguards

## Requirement Reference

- User Story: us_093
- Story Location: .propel/context/tasks/EP-018/us_093/us_093.md
- Acceptance Criteria:
  - AC-2: Given administrative safeguards are required, When the system is audited, Then evidence of security policies, staff training requirements, and incident response procedures is documented and accessible.
  - AC-4: Given a zero-downtime database migration is needed, When migrations execute, Then PHI remains protected and accessible throughout the migration process without exposure.
- Edge Case:
  - How does the system handle compliance when new regulations are published? System architecture supports configurable compliance rules that can be updated without code changes.

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
| Backend | Serilog | 8.x |
| Database | PostgreSQL | 16.x |

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

Implement HIPAA administrative safeguards as a configurable compliance policy system and a PHI migration protection guard (AC-2, AC-4, edge case 2). The administrative safeguards component manages structured compliance policy documents — security policies, staff training requirements, and incident response procedures — as database-backed `CompliancePolicy` entities that can be created, versioned, and queried via an admin API to provide audit evidence during HIPAA inspections (AC-2, NFR-042). The configurable compliance rules architecture stores rules as data-driven `ComplianceRule` entities with JSON-based evaluation criteria, enabling compliance officers to add or update rules (e.g., new regulations) without code changes (edge case 2). The PHI migration protection guard wraps EF Core migration execution to ensure that PHI columns remain encrypted and accessible during zero-downtime migrations — verifying pre-migration encryption state, blocking migrations that would expose plaintext PHI, and validating post-migration data accessibility (AC-4, DR-031).

## Dependent Tasks

- US_063 — Requires encryption implementation for PHI at-rest protection.
- US_064 — Requires audit log system for recording policy changes and migration events.
- US_091 task_001 — Requires migration pipeline engine for migration execution hooks.

## Impacted Components

- **NEW** `src/UPACIP.Service/Compliance/CompliancePolicyService.cs` — ICompliancePolicyService: CRUD for security policies, training requirements, incident response docs
- **NEW** `src/UPACIP.Service/Compliance/ComplianceRuleEngine.cs` — IComplianceRuleEngine: evaluates configurable JSON-based compliance rules
- **NEW** `src/UPACIP.Service/Compliance/PhiMigrationGuard.cs` — IPhiMigrationGuard: pre/post migration PHI protection verification
- **NEW** `src/UPACIP.Service/Compliance/Models/CompliancePolicy.cs` — Entity: versioned compliance policy documents
- **NEW** `src/UPACIP.Service/Compliance/Models/ComplianceRule.cs` — Entity: configurable compliance evaluation rules
- **NEW** `src/UPACIP.Service/Compliance/Models/ComplianceRuleEvaluation.cs` — DTO: rule evaluation result
- **NEW** `src/UPACIP.Service/Compliance/Models/PhiMigrationCheckResult.cs` — DTO: PHI protection verification result
- **MODIFY** `src/UPACIP.Api/Controllers/ComplianceController.cs` — Add policy CRUD, rule management, migration guard endpoints
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<CompliancePolicy>, DbSet<ComplianceRule>
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register CompliancePolicyService, ComplianceRuleEngine, PhiMigrationGuard

## Implementation Plan

1. **Create `CompliancePolicy` entity (AC-2)**: Create in `src/UPACIP.Service/Compliance/Models/CompliancePolicy.cs`:
   - `Guid Id` (PK).
   - `string PolicyType` — categorizes the policy: `"SecurityPolicy"`, `"TrainingRequirement"`, `"IncidentResponseProcedure"`.
   - `string Title` — human-readable policy title (e.g., "PHI Access Control Policy").
   - `string Content` — full policy text (Markdown format for rich formatting).
   - `int Version` — monotonically increasing version number. New versions create new records; old versions are retained for audit trail.
   - `string HipaaReference` — HIPAA section this policy addresses (e.g., "§164.308(a)(1) — Security Management Process").
   - `string Status` — "Draft", "Active", "Superseded", "Archived".
   - `string CreatedBy` — admin who created this version.
   - `string? ApprovedBy` — compliance officer who approved it.
   - `DateTime CreatedAtUtc`.
   - `DateTime? ApprovedAtUtc`.
   - `DateTime? EffectiveDate` — when the policy becomes enforceable.
   - `DateTime? ExpirationDate` — when the policy must be reviewed/renewed.
   Add `DbSet<CompliancePolicy>` to `ApplicationDbContext` with a composite index on `(PolicyType, Version)`.

2. **Implement `ICompliancePolicyService` / `CompliancePolicyService` (AC-2)**: Create in `src/UPACIP.Service/Compliance/CompliancePolicyService.cs`. Constructor injection of `ApplicationDbContext`, `ILogger<CompliancePolicyService>`.

   **`Task<CompliancePolicy> CreatePolicyAsync(CompliancePolicy policy, CancellationToken ct)`**:
   - Auto-assign `Version = 1` for new policies or `max(version) + 1` for existing `PolicyType + Title` combinations.
   - Set previous active version to `Status = "Superseded"`.
   - Persist and log: `Log.Information("COMPLIANCE_POLICY_CREATED: Type={Type}, Title={Title}, Version={Version}")`.

   **`Task<CompliancePolicy> ApprovePolicyAsync(Guid policyId, string approvedBy, CancellationToken ct)`**:
   - Set `Status = "Active"`, `ApprovedBy`, `ApprovedAtUtc`.
   - Record audit log entry: action = `data_modify`, resource_type = `CompliancePolicy`.

   **`Task<List<CompliancePolicy>> GetActivePoliciesAsync(string? policyType, CancellationToken ct)`**:
   - Return all policies with `Status = "Active"`, optionally filtered by `PolicyType`.
   - Used during HIPAA audits to demonstrate that security policies, training requirements, and incident response procedures are documented and accessible (AC-2).

   **`Task<List<CompliancePolicy>> GetPolicyHistoryAsync(string title, CancellationToken ct)`**:
   - Return all versions of a policy for audit trail, ordered by `Version` descending.

3. **Create `ComplianceRule` entity (edge case 2)**: Create in `src/UPACIP.Service/Compliance/Models/ComplianceRule.cs`:
   - `Guid Id` (PK).
   - `string RuleName` — unique name (e.g., "AuditLogRetention", "PasswordComplexity", "SessionTimeout").
   - `string Category` — "Technical", "Administrative", "Physical".
   - `string Description` — human-readable rule description.
   - `string EvaluationCriteriaJson` — JSON object defining the rule's evaluation logic:
     ```json
     {
       "type": "config_check",
       "configPath": "Authentication:SessionTimeoutMinutes",
       "operator": "<=",
       "expectedValue": "15",
       "hipaaReference": "§164.312(a)(2)(iii)"
     }
     ```
     Supported types: `config_check` (verify appsettings value), `db_query` (execute a PostgreSQL query and check result), `service_check` (verify a service/feature is enabled).
   - `bool IsActive` (default: true).
   - `string Severity` — "Critical", "High", "Medium", "Low".
   - `string? RemediationGuidance` — what to do if the rule fails.
   - `DateTime CreatedAtUtc`.
   - `DateTime UpdatedAtUtc`.
   Add `DbSet<ComplianceRule>` to `ApplicationDbContext`.

4. **Implement `IComplianceRuleEngine` / `ComplianceRuleEngine` (edge case 2)**: Create in `src/UPACIP.Service/Compliance/ComplianceRuleEngine.cs`. Constructor injection of `ApplicationDbContext`, `IConfiguration`, `ILogger<ComplianceRuleEngine>`.

   **`Task<List<ComplianceRuleEvaluation>> EvaluateAllRulesAsync(CancellationToken ct)`**:
   - Load all active `ComplianceRule` entities.
   - For each rule, parse `EvaluationCriteriaJson` and evaluate:
     - **`config_check`**: Read the configuration value from `IConfiguration` using the `configPath`. Compare against `expectedValue` using the `operator` (`==`, `!=`, `<=`, `>=`, `<`, `>`, `contains`). Example: verify session timeout is ≤ 15 minutes.
     - **`db_query`**: Execute the SQL query via `context.Database.ExecuteSqlRawAsync` (parameterized — no user input in query). Compare the result count or value against `expectedValue`. Example: verify audit logs exist for the last 24 hours.
     - **`service_check`**: Verify a boolean configuration flag is enabled. Example: `Security:EnableInputSanitization` = true.
   - Return `ComplianceRuleEvaluation` list: `string RuleName`, `bool Passed`, `string Category`, `string Severity`, `string? FailureReason`, `string HipaaReference`, `DateTime EvaluatedAtUtc`.

   **`Task<ComplianceRule> UpsertRuleAsync(ComplianceRule rule, CancellationToken ct)`**:
   - Insert or update a compliance rule — enables compliance officers to add new rules for new regulations without code changes (edge case 2).
   - Validate `EvaluationCriteriaJson` schema before persisting.
   - Log: `Log.Information("COMPLIANCE_RULE_UPDATED: Rule={RuleName}, Category={Category}")`.

5. **Implement `IPhiMigrationGuard` / `PhiMigrationGuard` (AC-4)**: Create in `src/UPACIP.Service/Compliance/PhiMigrationGuard.cs`. Constructor injection of `ApplicationDbContext`, `ILogger<PhiMigrationGuard>`.

   The guard wraps migration execution to ensure PHI protection during zero-downtime migrations (AC-4, DR-031):

   **`Task<PhiMigrationCheckResult> PreMigrationCheckAsync(CancellationToken ct)`**:
   - (a) Identify PHI columns by querying `information_schema.columns` for tables containing sensitive data (Patient, IntakeData, ClinicalDocument, ExtractedData). PHI columns: `email`, `full_name`, `date_of_birth`, `phone_number`, `emergency_contact`, `password_hash`, `insurance_info`, `data_content`, `file_path`.
   - (b) Verify that PostgreSQL SSL is enabled for the migration connection: query `pg_stat_ssl` for the current session.
   - (c) Verify that the migration does not contain `ALTER COLUMN ... TYPE` on PHI columns that would cause data re-encoding in plaintext. Parse the pending migration SQL and check for operations targeting PHI columns.
   - (d) Verify that the migration does not `DROP` or `RENAME` PHI columns without a corresponding `ADD` (expand-contract pattern per DR-031).
   - (e) Return `PhiMigrationCheckResult`: `bool Safe`, `List<string> Warnings`, `List<string> BlockingIssues`, `DateTime CheckedAtUtc`.

   **`Task<PhiMigrationCheckResult> PostMigrationVerifyAsync(CancellationToken ct)`**:
   - (a) Re-query PHI columns in `information_schema.columns` to verify they still exist and have the expected data types.
   - (b) Execute a sample read on each PHI table (SELECT COUNT(*)) to verify data accessibility.
   - (c) Verify SSL is still active on the connection.
   - (d) Log: `Log.Information("PHI_MIGRATION_VERIFIED: AllPhiColumnsAccessible={Accessible}, SslActive={Ssl}")`.

   **`PhiMigrationCheckResult`**: `bool Safe`, `List<string> Warnings` (non-blocking issues), `List<string> BlockingIssues` (migration must be halted), `int PhiColumnsVerified`, `bool SslActive`, `DateTime CheckedAtUtc`.

6. **Extend `ComplianceController` with new endpoints**: Add to `src/UPACIP.Api/Controllers/ComplianceController.cs` (created in task_001). All endpoints require `[Authorize(Roles = "Admin")]`.

   **Compliance Policy CRUD (AC-2):**
   - **POST `/api/admin/compliance/policies`** — Create a new policy version. Accept `CompliancePolicy` body. Return `201 Created`.
   - **PATCH `/api/admin/compliance/policies/{policyId}/approve`** — Approve a policy. Accept `{ approvedBy }`. Return `200 OK`.
   - **GET `/api/admin/compliance/policies`** — List active policies. Support `policyType` query filter. Return paginated results.
   - **GET `/api/admin/compliance/policies/{policyId}/history`** — View version history for a policy.

   **Compliance Rule Management (edge case 2):**
   - **GET `/api/admin/compliance/rules`** — List all compliance rules. Support `category`, `isActive` filters.
   - **POST `/api/admin/compliance/rules`** — Create or update a compliance rule.
   - **POST `/api/admin/compliance/rules/evaluate`** — Evaluate all active rules and return results.

   **PHI Migration Guard (AC-4):**
   - **POST `/api/admin/compliance/migration/pre-check`** — Run pre-migration PHI protection check.
   - **POST `/api/admin/compliance/migration/post-verify`** — Run post-migration PHI accessibility verification.

7. **Seed default compliance policies**: Create seed data for three default policy documents:
   - **Security Policy** (`PolicyType = "SecurityPolicy"`): Covers PHI access control, encryption requirements (AES-256, TLS 1.2+), password policy (bcrypt, 10 rounds), session management (15-min timeout), audit logging. References §164.308(a)(1).
   - **Training Requirement** (`PolicyType = "TrainingRequirement"`): Covers annual HIPAA training requirement, PHI handling procedures, incident reporting responsibility, new employee onboarding security training. References §164.308(a)(5).
   - **Incident Response Procedure** (`PolicyType = "IncidentResponseProcedure"`): Covers breach notification timeline (60 days per HIPAA), incident classification (minor/major/critical), escalation chain, evidence preservation, post-incident review. References §164.308(a)(6).

8. **Seed default compliance rules (edge case 2)**: Create seed data for configurable rules:
   - `SessionTimeout`: config_check, `Authentication:SessionTimeoutMinutes` <= 15, Critical.
   - `PasswordHashRounds`: config_check, `Authentication:BcryptRounds` >= 10, Critical.
   - `AuditLogRetention`: db_query, `SELECT COUNT(*) FROM audit_logs WHERE timestamp < NOW() - INTERVAL '7 years'` = 0, High.
   - `InputSanitizationEnabled`: service_check, `Security:EnableInputSanitization` = true, Critical.
   - `EncryptionEnabled`: service_check, `Encryption:Enabled` = true, Critical.
   These rules can be updated or extended by compliance officers without code deployments.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── ComplianceController.cs              ← from task_001
│   │   ├── Middleware/
│   │   │   ├── InputSanitizationMiddleware.cs        ← from task_002
│   │   │   └── SecurityHeadersMiddleware.cs          ← from task_002
│   │   ├── Filters/
│   │   │   └── SecurityValidationFilter.cs           ← from task_002
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   ├── Compliance/
│   │   │   ├── HipaaComplianceVerificationService.cs ← from task_001
│   │   │   ├── Checks/
│   │   │   │   ├── EncryptionAtRestCheck.cs           ← from task_001
│   │   │   │   ├── EncryptionInTransitCheck.cs        ← from task_001
│   │   │   │   └── RbacEnforcementCheck.cs            ← from task_001
│   │   │   └── Models/
│   │   │       ├── ComplianceVerificationReport.cs    ← from task_001
│   │   │       ├── ComplianceGap.cs                   ← from task_001
│   │   │       └── ComplianceVerificationLog.cs       ← from task_001
│   │   ├── Import/
│   │   ├── Migration/
│   │   ├── Monitoring/
│   │   └── Security/
│   │       ├── InputSanitizer.cs                     ← from task_002
│   │       └── Models/
│   │           ├── SecurityOptions.cs                ← from task_002
│   │           └── ThreatDetectionResult.cs          ← from task_002
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       └── Configurations/
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes US_093 task_001 and task_002 are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Compliance/CompliancePolicyService.cs | ICompliancePolicyService: CRUD for security policies, training, incident response |
| CREATE | src/UPACIP.Service/Compliance/ComplianceRuleEngine.cs | IComplianceRuleEngine: evaluates JSON-based configurable compliance rules |
| CREATE | src/UPACIP.Service/Compliance/PhiMigrationGuard.cs | IPhiMigrationGuard: pre/post migration PHI protection verification |
| CREATE | src/UPACIP.Service/Compliance/Models/CompliancePolicy.cs | Entity: versioned compliance policy documents |
| CREATE | src/UPACIP.Service/Compliance/Models/ComplianceRule.cs | Entity: configurable compliance evaluation rules with JSON criteria |
| CREATE | src/UPACIP.Service/Compliance/Models/ComplianceRuleEvaluation.cs | DTO: rule evaluation result with pass/fail and HIPAA reference |
| CREATE | src/UPACIP.Service/Compliance/Models/PhiMigrationCheckResult.cs | DTO: PHI protection verification result |
| MODIFY | src/UPACIP.Api/Controllers/ComplianceController.cs | Add policy CRUD, rule management, migration guard endpoints |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<CompliancePolicy>, DbSet<ComplianceRule> |
| MODIFY | src/UPACIP.Api/Program.cs | Register CompliancePolicyService, ComplianceRuleEngine, PhiMigrationGuard |

## External References

- [HIPAA Security Rule §164.308 — Administrative Safeguards](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)
- [HIPAA Breach Notification Rule §164.404](https://www.hhs.gov/hipaa/for-professionals/breach-notification/index.html)
- [EF Core Migrations — .NET 8](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations)
- [PostgreSQL information_schema.columns](https://www.postgresql.org/docs/16/infoschema-columns.html)
- [System.Text.Json — JsonDocument Parsing](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-dom)

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
- [ ] Active security policies are queryable by type (SecurityPolicy, TrainingRequirement, IncidentResponseProcedure) (AC-2)
- [ ] Policy versioning creates new records and supersedes previous versions
- [ ] Policy approval records approver identity and timestamp
- [ ] Compliance rules evaluate config_check, db_query, and service_check types (edge case 2)
- [ ] New compliance rules can be added via API without code changes (edge case 2)
- [ ] Pre-migration check identifies PHI columns and verifies SSL (AC-4)
- [ ] Pre-migration check blocks migrations that expose plaintext PHI (AC-4)
- [ ] Post-migration verify confirms PHI column accessibility (AC-4)
- [ ] Default seed data includes three policy types and five compliance rules

## Implementation Checklist

- [ ] Create CompliancePolicy entity with versioning and status tracking
- [ ] Implement CompliancePolicyService with create, approve, list, and history operations
- [ ] Create ComplianceRule entity with JSON-based evaluation criteria
- [ ] Implement ComplianceRuleEngine with config_check, db_query, and service_check evaluators
- [ ] Implement PhiMigrationGuard with pre-migration and post-migration verification
- [ ] Extend ComplianceController with policy, rule, and migration guard endpoints
- [ ] Seed default compliance policies (security, training, incident response)
- [ ] Seed default compliance rules (session timeout, password hash, audit retention, encryption, sanitization)
