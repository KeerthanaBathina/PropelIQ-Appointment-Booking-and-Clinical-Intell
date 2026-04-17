# Task - TASK_003

## Requirement Reference

- User Story: US_065
- Story Location: .propel/context/tasks/EP-011/us_065/us_065.md
- Acceptance Criteria:
  - AC-3: Given a user account exists, When 5 consecutive failed login attempts occur, Then the account is locked for 30 minutes and an audit log entry records the lockout event.
- Edge Cases:
  - Admin account lockout: Same 5-attempt lockout applies; a separate admin recovery process (database-level unlock) is documented for emergencies.

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
| Backend | .NET 8 (ASP.NET Core) | 8.x |
| AI/ML | N/A | - |
| Mobile | N/A | - |

**Note**: All code and libraries MUST be compatible with versions above.

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

Extend the audit log schema to include lockout-specific event types, add a database index for efficient lockout event querying, and create a documented admin account recovery SQL procedure for emergency lockout bypass. This task supports NFR-016 (account lockout after 5 failed attempts for 30 minutes) at the database layer, FR-094 and NFR-014 (session timeout audit events), and NFR-015 (concurrent session replacement audit events). The `AuditLog` entity from EP-DATA already supports generic audit entries, but this task ensures the `action` enum explicitly covers `account_lockout`, `login_failed`, and `session_replaced` event types for HIPAA-compliant security event tracking (DR-016: 7-year retention). The admin recovery procedure addresses the edge case where all admin accounts are locked out simultaneously, providing a documented, auditable database-level unlock mechanism.

## Dependent Tasks

- EP-DATA — Core Data Entities (provides AuditLog entity, ApplicationDbContext)
- US_005 / task_001_be_identity_configuration — ASP.NET Core Identity schema with lockout fields (failed_login_attempts, account_locked_until on AspNetUsers table)

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `AuditLog` entity | Server (Backend) | MODIFY — extend action enum with `account_lockout`, `login_failed`, `session_replaced` values |
| `ApplicationDbContext` | Server (Backend) | MODIFY — add index on AuditLog for action + timestamp composite queries |
| EF Core migration | Server (Backend) | CREATE — migration adding new enum values and index |
| Admin recovery SQL script | scripts/ | CREATE — documented SQL procedure for emergency admin lockout bypass |

## Implementation Plan

1. **Extend AuditLog action enum**: Add the following values to the `AuditLog.Action` enum (or string-based action field if using strings):
   - `account_lockout` — recorded when an account reaches 5 failed attempts and is locked (AC-3).
   - `login_failed` — recorded on each individual failed login attempt (security tracking).
   - `session_replaced` — recorded when a concurrent session is terminated and replaced (AC-2, US_065).
   - Verify existing enum values remain unchanged: `login`, `logout`, `data_access`, `data_modify`, `data_delete` (from EP-DATA AuditLog entity).
   - If the action field is a string column (not a database-level enum), add application-level constants and validation to ensure only known values are stored.

2. **Add composite index on AuditLog for security queries**: Create a PostgreSQL composite index to optimize security event queries (compliance reporting per DR-016):
   - Index: `IX_AuditLog_Action_Timestamp` on `(action, timestamp DESC)`.
   - This supports queries like: "Show all `account_lockout` events in the last 30 days" and "Show all `login_failed` events for user X."
   - Add filtered index: `IX_AuditLog_SecurityEvents` on `(user_id, timestamp DESC) WHERE action IN ('account_lockout', 'login_failed', 'session_replaced')` for fast per-user security event lookup.
   - Use EF Core Fluent API in `ApplicationDbContext.OnModelCreating` with `HasIndex()` and `HasFilter()`.

3. **Generate EF Core migration**: Run `dotnet ef migrations add AddSecurityAuditSchema` to create the migration:
   - Adds new enum values (if using PostgreSQL enum type) or validates string column accepts new values.
   - Creates the two new indexes.
   - Include `Down()` method to remove indexes and revert enum changes for rollback support (DR-029).

4. **Create admin account recovery SQL script**: Write a documented, auditable SQL script for emergency admin lockout bypass:
   - File: `scripts/admin-lockout-recovery.sql`
   - Purpose: Unlock a specific admin account when locked out and no other admin accounts are available to perform the unlock through the application.
   - The script MUST:
     - Accept the admin email as a parameter (prevent blanket unlock of all accounts).
     - Verify the target user has the `Admin` role before unlocking (prevent privilege escalation).
     - Reset `AccessFailedCount` to 0 and `LockoutEnd` to NULL on the `asp_net_users` table.
     - Insert an `AuditLog` entry recording the manual unlock: `action = 'admin_manual_unlock'`, `resource_type = 'User'`, `resource_id = user_id`, `ip_address = 'database-console'`, `user_agent = 'emergency-recovery-script'`.
     - Wrap all operations in a single transaction (atomicity per DR-029).
     - Print confirmation message with the unlocked user's email and the audit log entry ID.
   - The script MUST NOT:
     - Unlock non-admin accounts (enforce role check).
     - Run without explicit user email parameter (prevent mass unlock).
     - Skip audit logging (HIPAA compliance — all access modifications must be logged per FR-093).
   - Include header comments documenting: purpose, prerequisites, usage instructions, and post-execution verification steps.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   └── ...
│   ├── UPACIP.Service/
│   │   └── ...
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   └── AuditLog.cs
│       └── Migrations/
│           └── ...
└── scripts/
    └── admin-lockout-recovery.sql      # NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.DataAccess/Entities/AuditLog.cs | Extend action enum/constants with `account_lockout`, `login_failed`, `session_replaced`, `admin_manual_unlock` |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Add composite index `IX_AuditLog_Action_Timestamp` and filtered index `IX_AuditLog_SecurityEvents` via Fluent API |
| CREATE | src/UPACIP.DataAccess/Migrations/{timestamp}_AddSecurityAuditSchema.cs | Migration adding new action values and indexes with rollback support |
| CREATE | scripts/admin-lockout-recovery.sql | Parameterized SQL script for emergency admin lockout bypass with audit logging and role validation |

## External References

- [EF Core — Indexes and Constraints](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=fluent-api)
- [EF Core — Filtered Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=fluent-api#index-filter)
- [PostgreSQL — Partial Indexes](https://www.postgresql.org/docs/16/indexes-partial.html)
- [ASP.NET Core Identity — Schema Reference (AspNetUsers)](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-8.0)
- [HIPAA Security Rule — Audit Controls (§164.312(b))](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)

## Build Commands

```powershell
# Restore and build
dotnet restore UPACIP.sln
dotnet build UPACIP.sln --configuration Debug

# Generate migration
dotnet ef migrations add AddSecurityAuditSchema --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Apply migration
dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Verify indexes created
psql -h localhost -p 5432 -U upacip_app -d upacip -c "\di+ ix_audit_log_*"

# Test admin recovery script (dry run with ROLLBACK)
psql -h localhost -p 5432 -U upacip_app -d upacip -f scripts/admin-lockout-recovery.sql
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] `dotnet ef migrations add` generates migration without errors
- [ ] `dotnet ef database update` applies migration and creates indexes
- [ ] AuditLog entity accepts `account_lockout`, `login_failed`, `session_replaced`, `admin_manual_unlock` action values
- [ ] Composite index `IX_AuditLog_Action_Timestamp` exists and is used for action+timestamp queries (verify via `EXPLAIN ANALYZE`)
- [ ] Filtered index `IX_AuditLog_SecurityEvents` exists and filters on security event action types
- [ ] Admin recovery script unlocks only admin-role accounts (rejects non-admin emails)
- [ ] Admin recovery script creates audit log entry for manual unlock
- [ ] Admin recovery script runs within a single transaction (all-or-nothing)
- [ ] Migration `Down()` method successfully rolls back indexes and enum changes
- [ ] No PII exposed in index definitions or script output (NFR-017)

## Implementation Checklist

- [ ] Extend `AuditLog` action enum/constants with `account_lockout`, `login_failed`, `session_replaced`, `admin_manual_unlock` values
- [ ] Add composite index `IX_AuditLog_Action_Timestamp` on `(action, timestamp DESC)` via EF Core Fluent API
- [ ] Add filtered index `IX_AuditLog_SecurityEvents` on `(user_id, timestamp DESC)` filtered to security event actions
- [ ] Generate and apply EF Core migration `AddSecurityAuditSchema` with rollback support
- [ ] Create `scripts/admin-lockout-recovery.sql` with parameterized admin-only unlock, audit logging, transaction wrapping, and documentation header
