# Task - TASK_003

## Requirement Reference

- User Story: US_016
- Story Location: .propel/context/tasks/EP-001/us_016/us_016.md
- Acceptance Criteria:
  - AC-1: MFA TOTP requires encrypted secret storage per user and one-time backup codes — DB must store TotpSecretEncrypted and MfaRecoveryCodes on the User entity.
  - AC-2: Account lockout after 5 failed attempts for 30 minutes — DB must persist FailedLoginAttempts and AccountLockedUntil fields on the User entity.
  - AC-4: Last login timestamp and IP address recorded on successful login — DB must store LastLoginAt and LastLoginIp on the User entity.
  - AC-5: Immutable audit log entries for all authentication events — DB must provide the audit_logs table with insert-only semantics.
- Edge Cases:
  - Concurrent failed login attempts: Redis handles atomic counting; PostgreSQL fields serve as persistent backup.
  - Audit log volume: indexed for efficient querying, partitioning considered for future scaling.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Database | PostgreSQL | 16.x |
| Cache | Upstash Redis | 7.x |
| ORM | Entity Framework Core | 8.x |
| Backend | ASP.NET Core | .NET 8 |
| Language | C# | 12 |
| Frontend | N/A | - |
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

Create the database schema changes to support MFA, account lockout, login tracking, and immutable audit logging. **(1) User Entity Extensions**: Add columns for TOTP secret storage (encrypted), backup codes (hashed), MFA enabled flag, failed login attempt counter, lockout expiry timestamp, last login timestamp, and last login IP. **(2) AuditLog Table**: Create the `audit_logs` table with insert-only design (no UPDATE/DELETE at application level), indexed for efficient querying by user and timestamp. **(3) Redis Key Structure**: Define Redis key patterns for atomic lockout counters and MFA rate limiting. **(4) EF Core Migration**: Generate and validate the migration covering all schema changes.

## Dependent Tasks

- US_005 — Authentication scaffold (provides ApplicationUser entity and initial migration)
- US_012 task_003 — Registration schema (provides base User entity extension pattern)

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `ApplicationUser` entity | Server (Backend) | MODIFY — add MFA, lockout, and login tracking columns |
| `AuditLog` entity | Server (Backend) | CREATE — new entity with EF Core configuration |
| `ApplicationDbContext` | Server (Backend) | MODIFY — add DbSet<AuditLog>, configure entity mappings |
| EF Core Migration | Server (Backend) | CREATE — migration for all schema changes |
| Redis Key Structure | Infrastructure | DEFINE — key patterns for lockout counters |

## Implementation Plan

1. **Extend ApplicationUser with MFA columns** — Add the following columns to the `AspNetUsers` table (or equivalent ApplicationUser table) via EF Core migration:

   | Column | Type | Nullable | Default | Description |
   |--------|------|----------|---------|-------------|
   | `totp_secret_encrypted` | `VARCHAR(512)` | YES | NULL | AES-256 encrypted TOTP secret (Base64-encoded ciphertext) |
   | `mfa_recovery_codes` | `TEXT` | YES | NULL | Comma-separated bcrypt-hashed backup codes |
   | `mfa_enabled` | `BOOLEAN` | NO | FALSE | Whether MFA is enabled for this user |

   EF Core property mapping:
   ```csharp
   builder.Property(u => u.TotpSecretEncrypted).HasColumnName("totp_secret_encrypted").HasMaxLength(512);
   builder.Property(u => u.MfaRecoveryCodes).HasColumnName("mfa_recovery_codes").HasColumnType("TEXT");
   builder.Property(u => u.MfaEnabled).HasColumnName("mfa_enabled").HasDefaultValue(false);
   ```

2. **Extend ApplicationUser with lockout and login tracking columns** — Add columns:

   | Column | Type | Nullable | Default | Description |
   |--------|------|----------|---------|-------------|
   | `failed_login_attempts` | `INTEGER` | NO | 0 | Consecutive failed login count |
   | `account_locked_until` | `TIMESTAMPTZ` | YES | NULL | Lockout expiry timestamp (UTC) |
   | `last_login_at` | `TIMESTAMPTZ` | YES | NULL | Most recent successful login timestamp |
   | `last_login_ip` | `VARCHAR(45)` | YES | NULL | IP address of most recent login (supports IPv6) |

   EF Core mapping:
   ```csharp
   builder.Property(u => u.FailedLoginAttempts).HasColumnName("failed_login_attempts").HasDefaultValue(0);
   builder.Property(u => u.AccountLockedUntil).HasColumnName("account_locked_until");
   builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
   builder.Property(u => u.LastLoginIp).HasColumnName("last_login_ip").HasMaxLength(45);
   ```

   > **Note**: ASP.NET Core Identity already provides `LockoutEnd` and `AccessFailedCount` on `IdentityUser`. Evaluate whether to use the built-in fields or custom fields. Recommendation: Use built-in `LockoutEnd`/`AccessFailedCount` for lockout (leverages Identity's built-in lockout feature) and add only `LastLoginAt`/`LastLoginIp` as custom fields. Add `TotpSecretEncrypted`/`MfaRecoveryCodes`/`MfaEnabled` as custom fields since Identity's MFA is token-provider based and we need TOTP-specific storage.

3. **Create audit_logs table** — New table per design.md AuditLog entity definition:

   | Column | Type | Nullable | Constraints | Description |
   |--------|------|----------|-------------|-------------|
   | `log_id` | `UUID` | NO | PK, DEFAULT gen_random_uuid() | Unique audit log identifier |
   | `user_id` | `UUID` | YES | FK → users(user_id) ON DELETE SET NULL | Acting user (NULL for system events) |
   | `action` | `VARCHAR(50)` | NO | NOT NULL | Event type (Login, Logout, FailedLogin, AccountLocked, MfaEnabled, MfaDisabled, MfaVerified, AdminMfaReset, PasswordChanged, PasswordReset) |
   | `resource_type` | `VARCHAR(100)` | YES | - | Target entity type (e.g., "User", "Session") |
   | `resource_id` | `VARCHAR(100)` | YES | - | Target entity ID |
   | `timestamp` | `TIMESTAMPTZ` | NO | DEFAULT NOW(), NOT NULL | Event timestamp (immutable once written) |
   | `ip_address` | `VARCHAR(45)` | YES | - | Client IP address (supports IPv6) |
   | `user_agent` | `VARCHAR(500)` | YES | - | Client user agent string |

   **Indexes**:
   - `IX_audit_logs_user_id_timestamp` — Composite index on (`user_id`, `timestamp` DESC) for per-user audit queries.
   - `IX_audit_logs_action_timestamp` — Composite index on (`action`, `timestamp` DESC) for event-type queries.
   - `IX_audit_logs_timestamp` — Index on `timestamp` DESC for chronological listing.

   **Constraints**:
   - FK on `user_id` → `users(user_id)` with `ON DELETE SET NULL` (preserve audit records when user is soft-deleted).
   - No UPDATE trigger at application level (enforced by service layer — no update method exposed).

   EF Core configuration:
   ```csharp
   builder.ToTable("audit_logs");
   builder.HasKey(a => a.LogId);
   builder.Property(a => a.LogId).HasColumnName("log_id").HasDefaultValueSql("gen_random_uuid()");
   builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(50).IsRequired();
   builder.Property(a => a.Timestamp).HasColumnName("timestamp").HasDefaultValueSql("NOW()").IsRequired();
   builder.Property(a => a.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
   builder.Property(a => a.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
   builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.SetNull);
   builder.HasIndex(a => new { a.UserId, a.Timestamp }).HasDatabaseName("IX_audit_logs_user_id_timestamp");
   builder.HasIndex(a => new { a.Action, a.Timestamp }).HasDatabaseName("IX_audit_logs_action_timestamp");
   builder.HasIndex(a => a.Timestamp).HasDatabaseName("IX_audit_logs_timestamp").IsDescending();
   ```

4. **Define Redis key structure for lockout counters** — Key patterns (aligned with US_014 Redis conventions):

   | Key Pattern | Type | TTL | Description |
   |-------------|------|-----|-------------|
   | `lockout:attempts:{userId}` | STRING (integer) | 30 minutes | Atomic failed attempt counter (INCR command) |
   | `lockout:until:{userId}` | STRING (ISO timestamp) | 30 minutes | Lockout expiry timestamp for fast lookup |
   | `mfa:rate:{mfaToken}` | STRING (integer) | 5 minutes | MFA verify attempt counter (max 5) |

   - `lockout:attempts:{userId}`: Use Redis `INCR` for atomic increment. Set `EXPIRE 1800` (30 min) on first increment. On successful login, `DEL` the key.
   - `lockout:until:{userId}`: Set when lockout triggers. Used for fast lockout-check without DB query. TTL auto-cleans.
   - `mfa:rate:{mfaToken}`: Rate limit MFA verification attempts. Auto-expires with the mfaToken.

5. **Generate and validate EF Core migration** — Run `dotnet ef migrations add AddMfaLockoutAuditLog`. Verify the migration:
   - Adds `totp_secret_encrypted`, `mfa_recovery_codes`, `mfa_enabled` to users table.
   - Adds `last_login_at`, `last_login_ip` to users table (use Identity's built-in `LockoutEnd`/`AccessFailedCount` for lockout).
   - Creates `audit_logs` table with all columns, indexes, and FK.
   - Down migration correctly reverses changes.
   - Run `dotnet ef database update` against dev database and verify with `\d audit_logs` and `\d+ "AspNetUsers"`.

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
Server/
├── Models/
│   ├── ApplicationUser.cs    # (existing) — MODIFY with MFA + login tracking columns
│   └── AuditLog.cs           # CREATE
├── Data/
│   ├── ApplicationDbContext.cs  # MODIFY — add DbSet, configure entities
│   └── Migrations/
│       └── YYYYMMDD_AddMfaLockoutAuditLog.cs  # CREATE — generated migration
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Models/ApplicationUser.cs | Add TotpSecretEncrypted, MfaRecoveryCodes, MfaEnabled, LastLoginAt, LastLoginIp properties |
| CREATE | Server/Models/AuditLog.cs | AuditLog entity: LogId, UserId, Action, ResourceType, ResourceId, Timestamp, IpAddress, UserAgent |
| MODIFY | Server/Data/ApplicationDbContext.cs | Add DbSet<AuditLog>, OnModelCreating config for AuditLog + ApplicationUser new columns |
| CREATE | Server/Data/Migrations/YYYYMMDD_AddMfaLockoutAuditLog.cs | EF Core migration for all schema changes |

## External References

- [PostgreSQL 16 — UUID Generation (gen_random_uuid)](https://www.postgresql.org/docs/16/functions-uuid.html)
- [EF Core 8 — Entity Configuration](https://learn.microsoft.com/en-us/ef/core/modeling/)
- [ASP.NET Core Identity — Lockout Configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration#lockout)
- [Redis Commands — INCR (Atomic Counter)](https://redis.io/commands/incr)
- [OWASP — Logging Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html)

## Build Commands

- `dotnet ef migrations add AddMfaLockoutAuditLog` — Generate migration
- `dotnet ef database update` — Apply migration
- `dotnet build` — Compile and verify no build errors
- `dotnet test` — Run tests

## Implementation Validation Strategy

- [ ] EF Core migration generated without errors
- [ ] Migration applies successfully to PostgreSQL dev database
- [ ] `totp_secret_encrypted` column is VARCHAR(512), nullable
- [ ] `mfa_recovery_codes` column is TEXT, nullable
- [ ] `mfa_enabled` column is BOOLEAN, NOT NULL, default FALSE
- [ ] `last_login_at` column is TIMESTAMPTZ, nullable
- [ ] `last_login_ip` column is VARCHAR(45), nullable
- [ ] `audit_logs` table created with all columns and correct types
- [ ] `audit_logs.log_id` has DEFAULT gen_random_uuid()
- [ ] `audit_logs.timestamp` has DEFAULT NOW() and is NOT NULL
- [ ] FK from `audit_logs.user_id` → users with ON DELETE SET NULL
- [ ] Composite index on (user_id, timestamp) exists
- [ ] Composite index on (action, timestamp) exists
- [ ] Index on timestamp DESC exists
- [ ] Down migration drops audit_logs table and removes added columns
- [ ] Redis key patterns documented and consistent with US_014 conventions

## Implementation Checklist

- [ ] Add MFA columns (TotpSecretEncrypted, MfaRecoveryCodes, MfaEnabled) to ApplicationUser entity with EF Core property mappings
- [ ] Add login tracking columns (LastLoginAt, LastLoginIp) to ApplicationUser entity; evaluate using Identity's built-in LockoutEnd/AccessFailedCount for lockout
- [ ] Create AuditLog entity with all columns, FK to User (ON DELETE SET NULL), and insert-only design intent
- [ ] Configure EF Core indexes: (UserId, Timestamp), (Action, Timestamp), (Timestamp DESC) on audit_logs
- [ ] Define Redis key patterns (lockout:attempts, lockout:until, mfa:rate) with TTL documentation
- [ ] Generate and validate EF Core migration, verify Up and Down methods
