# Task - task_001_be_technical_safeguards_verification

## Requirement Reference

- User Story: us_093
- Story Location: .propel/context/tasks/EP-018/us_093/us_093.md
- Acceptance Criteria:
  - AC-1: Given the system handles PHI, When technical safeguards are verified, Then encryption at rest (AES-256), encryption in transit (TLS 1.2+), and access controls (RBAC) are all active and verified.
- Edge Case:
  - What happens when a HIPAA audit reveals a gap in compliance? System audit log provides complete evidence trail; any identified gap triggers immediate remediation with a documented timeline.

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

Implement a HIPAA technical safeguards verification service that programmatically validates the three required technical controls — encryption at rest (AES-256 via TR-019), encryption in transit (TLS 1.2+ via TR-018), and role-based access control (RBAC via NFR-011) — and produces a structured compliance verification report (AC-1). The service provides an admin-only API endpoint that executes all verification checks on demand, returning a `ComplianceVerificationReport` with per-control pass/fail status, evidence details, and timestamps. Each verification run is persisted as a `ComplianceVerificationLog` entity for audit evidence during HIPAA audits. When any control fails verification, the service logs a critical Serilog alert and creates a `ComplianceGap` record with a remediation timeline (edge case 1). The service supports NFR-041 (HIPAA Privacy Rule) and NFR-042 (HIPAA Security Rule) by providing auditable proof that technical safeguards are continuously enforced.

## Dependent Tasks

- US_063 — Requires encryption implementation (AES-256 at rest, TLS 1.2+ in transit).
- US_064 — Requires audit log system for recording compliance verification events.

## Impacted Components

- **NEW** `src/UPACIP.Service/Compliance/HipaaComplianceVerificationService.cs` — IHipaaComplianceVerificationService: orchestrates all technical safeguard checks
- **NEW** `src/UPACIP.Service/Compliance/Checks/EncryptionAtRestCheck.cs` — IComplianceCheck: verifies AES-256 encryption via PostgreSQL pg_stat_ssl and pgcrypto
- **NEW** `src/UPACIP.Service/Compliance/Checks/EncryptionInTransitCheck.cs` — IComplianceCheck: verifies TLS 1.2+ via connection inspection
- **NEW** `src/UPACIP.Service/Compliance/Checks/RbacEnforcementCheck.cs` — IComplianceCheck: verifies RBAC roles and authorization policies
- **NEW** `src/UPACIP.Service/Compliance/Models/ComplianceVerificationReport.cs` — DTO: per-control results with evidence
- **NEW** `src/UPACIP.Service/Compliance/Models/ComplianceGap.cs` — Entity: tracks compliance gaps with remediation timeline
- **NEW** `src/UPACIP.Service/Compliance/Models/ComplianceVerificationLog.cs` — Entity: audit trail for verification runs
- **NEW** `src/UPACIP.Api/Controllers/ComplianceController.cs` — Admin-only API for triggering and viewing compliance checks
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Add DbSet<ComplianceVerificationLog>, DbSet<ComplianceGap>

## Implementation Plan

1. **Define `IComplianceCheck` interface and result models**: Create in `src/UPACIP.Service/Compliance/`:
   ```csharp
   public interface IComplianceCheck
   {
       string ControlName { get; }
       string ControlCategory { get; } // "Technical", "Administrative", "Physical"
       string HipaaReference { get; } // e.g., "§164.312(a)(2)(iv)"
       Task<ComplianceCheckResult> ExecuteAsync(CancellationToken ct);
   }
   ```
   **`ComplianceCheckResult`**: `bool Passed`, `string ControlName`, `string Evidence` (human-readable proof), `string? FailureReason`, `DateTime VerifiedAtUtc`, `Dictionary<string, string> Details` (key-value pairs of verification data — e.g., TLS version, cipher suite, encryption algorithm).

2. **Implement `EncryptionAtRestCheck` (AC-1 — AES-256)**: Create in `src/UPACIP.Service/Compliance/Checks/EncryptionAtRestCheck.cs`. Constructor injection of `ApplicationDbContext` and `ILogger<EncryptionAtRestCheck>`.
   - `ControlName`: "Encryption at Rest (AES-256)".
   - `HipaaReference`: "§164.312(a)(2)(iv) — Encryption and Decryption".
   - **Verification logic**:
     - (a) Query PostgreSQL `pg_settings` for `data_checksums` and verify `ssl` setting is `on`: `SELECT name, setting FROM pg_settings WHERE name IN ('ssl', 'data_checksums')`.
     - (b) Verify PostgreSQL Transparent Data Encryption (TDE) or application-level AES-256 by checking that the `pgcrypto` extension is installed: `SELECT extname FROM pg_extension WHERE extname = 'pgcrypto'`.
     - (c) Verify application-level `EncryptionOptions.Enabled = true` and algorithm is AES-256 by reading `IOptionsMonitor<EncryptionOptions>` (from US_089 backup encryption).
     - (d) Check that backup files are encrypted (`.dump.enc` extension) by querying the `BackupLog` table for the latest backup and verifying `EncryptedFilePath IS NOT NULL`.
   - If all checks pass, return `Passed = true` with evidence: `"AES-256 encryption verified: pgcrypto extension active, application encryption enabled, backup encryption confirmed"`.
   - If any check fails, return `Passed = false` with specific failure reason.

3. **Implement `EncryptionInTransitCheck` (AC-1 — TLS 1.2+)**: Create in `src/UPACIP.Service/Compliance/Checks/EncryptionInTransitCheck.cs`. Constructor injection of `ApplicationDbContext` and `ILogger<EncryptionInTransitCheck>`.
   - `ControlName`: "Encryption in Transit (TLS 1.2+)".
   - `HipaaReference`: "§164.312(e)(1) — Transmission Security".
   - **Verification logic**:
     - (a) Query PostgreSQL `pg_stat_ssl` to verify the current database connection uses TLS: `SELECT ssl, version, cipher FROM pg_stat_ssl WHERE pid = pg_backend_pid()`.
     - (b) Verify the TLS version is 1.2 or higher (parse the `version` field — accept `TLSv1.2`, `TLSv1.3`).
     - (c) Verify the cipher suite is strong (reject `NULL`, `RC4`, `DES` ciphers). Accept `AES128`, `AES256`, `CHACHA20` families.
     - (d) Verify application-level HTTPS enforcement by checking Kestrel HTTPS configuration: read `IServer` and inspect `ServerFeatures` for HTTPS listener bindings.
   - Log evidence: `"TLS {Version} active with cipher {Cipher}, HTTPS enforced on all endpoints"`.

4. **Implement `RbacEnforcementCheck` (AC-1 — RBAC)**: Create in `src/UPACIP.Service/Compliance/Checks/RbacEnforcementCheck.cs`. Constructor injection of `ApplicationDbContext`, `RoleManager<IdentityRole>`, and `ILogger<RbacEnforcementCheck>`.
   - `ControlName`: "Role-Based Access Control (RBAC)".
   - `HipaaReference`: "§164.312(a)(1) — Access Control".
   - **Verification logic**:
     - (a) Query ASP.NET Core Identity to verify expected roles exist: `Patient`, `Staff`, `Admin`. Error if any role is missing.
     - (b) Verify that no user has multiple conflicting roles (e.g., a `Patient` should not also have `Admin` role unless explicitly configured).
     - (c) Verify that controller-level `[Authorize(Roles = "...")]` attributes are enforced by querying the list of API endpoints and their authorization metadata. Use `IActionDescriptorCollectionProvider` to enumerate controller actions and check for `AuthorizeAttribute` presence. Flag endpoints missing `[Authorize]` as gaps.
     - (d) Verify that admin-only endpoints (paths containing `/admin/`) require `Admin` role.
   - Log evidence: `"RBAC verified: {RoleCount} roles configured, {ProtectedEndpoints}/{TotalEndpoints} endpoints protected"`.

5. **Create `ComplianceVerificationLog` and `ComplianceGap` entities**: Create in `src/UPACIP.Service/Compliance/Models/`:

   **`ComplianceVerificationLog`**:
   - `Guid Id` (PK).
   - `DateTime ExecutedAtUtc` — when the verification was run.
   - `string ExecutedBy` — admin user who triggered it.
   - `int TotalChecks` — number of controls verified.
   - `int PassedChecks` — number of controls that passed.
   - `int FailedChecks` — number of controls that failed.
   - `string Status` — "AllPassed", "HasGaps".
   - `string ReportJson` — JSON-serialized full `ComplianceVerificationReport`.
   Add `DbSet<ComplianceVerificationLog>` to `ApplicationDbContext`.

   **`ComplianceGap`**:
   - `Guid Id` (PK).
   - `Guid VerificationLogId` (FK to ComplianceVerificationLog).
   - `string ControlName` — which control failed.
   - `string HipaaReference` — HIPAA section reference.
   - `string FailureReason` — what failed.
   - `string RemediationPlan` — auto-generated remediation guidance.
   - `DateTime IdentifiedAtUtc` — when the gap was discovered.
   - `DateTime RemediationDeadlineUtc` — target date for remediation (default: 30 days from identification per edge case 1).
   - `string Status` — "Open", "InProgress", "Remediated".
   - `DateTime? RemediatedAtUtc` — when the gap was closed.
   Add `DbSet<ComplianceGap>` to `ApplicationDbContext`.

6. **Implement `IHipaaComplianceVerificationService`**: Create in `src/UPACIP.Service/Compliance/HipaaComplianceVerificationService.cs`. Constructor injection of `IEnumerable<IComplianceCheck>`, `ApplicationDbContext`, and `ILogger<HipaaComplianceVerificationService>`.

   **Method `Task<ComplianceVerificationReport> RunVerificationAsync(string executedBy, CancellationToken ct)`**:
   - (a) Execute all registered `IComplianceCheck` implementations sequentially.
   - (b) Aggregate results into `ComplianceVerificationReport`:
     - `List<ComplianceCheckResult> Results`, `DateTime ExecutedAtUtc`, `string ExecutedBy`, `bool AllPassed`, `int TotalChecks`, `int PassedCount`, `int FailedCount`.
   - (c) Persist `ComplianceVerificationLog` with JSON-serialized report.
   - (d) For each failed check, create a `ComplianceGap` record with auto-generated remediation guidance:
     - Encryption at rest failed → `"Enable AES-256 encryption: install pgcrypto extension, enable application EncryptionOptions"`.
     - TLS failed → `"Configure TLS 1.2+: update PostgreSQL ssl settings, verify Kestrel HTTPS bindings"`.
     - RBAC failed → `"Add [Authorize] attributes to unprotected endpoints, verify role configuration"`.
   - (e) If any check fails, log critical alert: `Log.Critical("HIPAA_COMPLIANCE_GAP: Control={ControlName}, Reason={Reason}")`.
   - (f) Record audit log entry: action = `compliance_verification`, resource_type = `ComplianceVerificationLog`.

7. **Implement `ComplianceController` API**: Create in `src/UPACIP.Api/Controllers/ComplianceController.cs`. All endpoints require `[Authorize(Roles = "Admin")]`.

   **POST `/api/admin/compliance/verify`** — Trigger compliance verification:
   - Call `IHipaaComplianceVerificationService.RunVerificationAsync()`.
   - Return `200 OK` with `ComplianceVerificationReport`.

   **GET `/api/admin/compliance/history`** — View verification history:
   - Return paginated `ComplianceVerificationLog` entries ordered by `ExecutedAtUtc` desc.

   **GET `/api/admin/compliance/gaps`** — View open compliance gaps:
   - Return `ComplianceGap` records filtered by `Status != "Remediated"`.
   - Support query parameters: `status` filter, `controlName` filter.

   **PATCH `/api/admin/compliance/gaps/{gapId}`** — Update gap remediation status:
   - Accept `{ status, remediationNotes }`.
   - Update `ComplianceGap.Status` and set `RemediatedAtUtc` when status = "Remediated".

8. **Register services in DI**: In `Program.cs`:
   - Register `services.AddScoped<IComplianceCheck, EncryptionAtRestCheck>()`.
   - Register `services.AddScoped<IComplianceCheck, EncryptionInTransitCheck>()`.
   - Register `services.AddScoped<IComplianceCheck, RbacEnforcementCheck>()`.
   - Register `services.AddScoped<IHipaaComplianceVerificationService, HipaaComplianceVerificationService>()`.
   - Add `DbSet<ComplianceVerificationLog>` and `DbSet<ComplianceGap>` to `ApplicationDbContext`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Backup/
│   │   ├── Migration/
│   │   ├── Import/
│   │   └── Monitoring/
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs                   ← from US_008
│       ├── Entities/
│       └── Configurations/
├── Server/
│   └── Services/
├── app/
├── config/
└── scripts/
```

> Assumes US_063 (encryption), US_064 (audit log), US_066 (input sanitization) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Compliance/HipaaComplianceVerificationService.cs | IHipaaComplianceVerificationService: orchestrates technical safeguard checks |
| CREATE | src/UPACIP.Service/Compliance/Checks/EncryptionAtRestCheck.cs | IComplianceCheck: verifies AES-256 via pgcrypto and application config |
| CREATE | src/UPACIP.Service/Compliance/Checks/EncryptionInTransitCheck.cs | IComplianceCheck: verifies TLS 1.2+ via pg_stat_ssl |
| CREATE | src/UPACIP.Service/Compliance/Checks/RbacEnforcementCheck.cs | IComplianceCheck: verifies RBAC roles and endpoint authorization |
| CREATE | src/UPACIP.Service/Compliance/Models/ComplianceVerificationReport.cs | DTO: aggregated per-control verification results |
| CREATE | src/UPACIP.Service/Compliance/Models/ComplianceGap.cs | Entity: tracks gaps with remediation timeline |
| CREATE | src/UPACIP.Service/Compliance/Models/ComplianceVerificationLog.cs | Entity: audit trail for verification runs |
| CREATE | src/UPACIP.Api/Controllers/ComplianceController.cs | Admin-only API: verify, history, gaps, gap remediation |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add DbSet<ComplianceVerificationLog>, DbSet<ComplianceGap> |
| MODIFY | src/UPACIP.Api/Program.cs | Register IComplianceCheck implementations, verification service |

## External References

- [HIPAA Security Rule §164.312 — Technical Safeguards](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)
- [PostgreSQL pg_stat_ssl — TLS Connection Monitoring](https://www.postgresql.org/docs/16/monitoring-stats.html#MONITORING-PG-STAT-SSL-VIEW)
- [PostgreSQL pgcrypto Extension](https://www.postgresql.org/docs/16/pgcrypto.html)
- [ASP.NET Core Authorization — Role-Based](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- [IActionDescriptorCollectionProvider — ASP.NET Core](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.infrastructure.iactiondescriptorcollectionprovider)

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
- [ ] Encryption at rest check verifies pgcrypto extension and application-level AES-256 (AC-1)
- [ ] Encryption in transit check verifies TLS 1.2+ via pg_stat_ssl (AC-1)
- [ ] RBAC check verifies roles exist and endpoints have `[Authorize]` attributes (AC-1)
- [ ] Compliance verification report aggregates all check results with evidence
- [ ] Failed checks create ComplianceGap records with 30-day remediation deadline (edge case 1)
- [ ] Critical Serilog alert fires on any failed compliance check
- [ ] Audit log records each verification run with admin identity
- [ ] API endpoints require Admin role authorization
- [ ] Verification history and gap tracking are queryable via API

## Implementation Checklist

- [ ] Define IComplianceCheck interface and ComplianceCheckResult model
- [ ] Implement EncryptionAtRestCheck with pgcrypto and application config verification
- [ ] Implement EncryptionInTransitCheck with pg_stat_ssl and Kestrel HTTPS verification
- [ ] Implement RbacEnforcementCheck with role and endpoint authorization verification
- [ ] Create ComplianceVerificationLog and ComplianceGap entities with DbSets
- [ ] Implement HipaaComplianceVerificationService orchestrating all checks with gap creation
- [ ] Implement ComplianceController with verify, history, gaps, and gap update endpoints
- [ ] Register all compliance services and checks in Program.cs DI container
