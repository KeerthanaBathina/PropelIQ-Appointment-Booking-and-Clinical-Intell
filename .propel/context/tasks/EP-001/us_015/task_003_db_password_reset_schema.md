# Task - TASK_003

## Requirement Reference

- User Story: US_015
- Story Location: .propel/context/tasks/EP-001/us_015/us_015.md
- Acceptance Criteria:
  - AC-1: Reset token generation requires a `password_reset_tokens` table to store token hashes with expiry timestamps.
  - AC-2: Token validation within 1-hour window requires `expires_at` column comparison.
  - AC-3: Expired token detection requires `expires_at` < current UTC time check.
  - AC-4: Password reset success requires token marked as used (`is_used`) to prevent reuse.
- Edge Cases:
  - Multiple reset requests: prior tokens must be invalidatable — requires `invalidated_at` column or bulk update by `user_id`.
  - Token reuse prevention: `is_used` flag prevents replaying a consumed token.

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
| Authentication | ASP.NET Core Identity | 8.x |
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

Create the `password_reset_tokens` table in PostgreSQL via EF Core migration. This table stores hashed password reset tokens with 1-hour expiry, tracks token usage and invalidation, and supports the password reset flow from task_002. The schema is analogous to the `email_verification_tokens` table from US_012 task_003 but serves a distinct purpose (password reset vs. email verification) and includes additional columns for multi-request invalidation tracking. Tokens store hashed values only (never plaintext) per OWASP secure token storage guidelines.

## Dependent Tasks

- US_005 — Authentication scaffold (provides ApplicationDbContext and ApplicationUser entity)

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `PasswordResetToken` entity | Server (Backend) | CREATE — new entity for password reset tokens |
| `ApplicationDbContext` | Server (Backend) | MODIFY — add DbSet<PasswordResetToken> and Fluent API configuration |
| EF Core migration | Server (Backend) | CREATE — migration for password_reset_tokens table |

## Implementation Plan

1. **Create PasswordResetToken entity** — New table `password_reset_tokens` with fields:
   - `Id` (UUID, primary key)
   - `UserId` (UUID, foreign key to AspNetUsers)
   - `TokenHash` (string, required — SHA-256 hash of the token; never store plaintext)
   - `ExpiresAt` (DateTime, UTC — 1 hour from creation per FR-005/AC-2/AC-3)
   - `IsUsed` (bool, default false — prevent token reuse after successful reset)
   - `InvalidatedAt` (DateTime?, nullable — set when a newer token is generated, invalidating this one)
   - `CreatedAt` (DateTime, UTC)

2. **Configure Fluent API mappings** — In `ApplicationDbContext.OnModelCreating()`:
   - Index on `TokenHash` for fast lookup during validation (primary query path).
   - Index on `UserId` for bulk invalidation of prior tokens on new request.
   - Index on `ExpiresAt` for potential cleanup job querying expired tokens.
   - Foreign key from `UserId` to `ApplicationUser` with cascade delete (if user is deleted, their tokens are removed).
   - Column constraints: `TokenHash` max length 64 (SHA-256 hex), `UserId` not null, `ExpiresAt` not null.

3. **Generate EF Core migration** — Create migration via `dotnet ef migrations add AddPasswordResetTokens`. Verify generated SQL includes:
   - CREATE TABLE `password_reset_tokens` with all columns and correct types.
   - CREATE INDEX on `token_hash` for fast token lookup.
   - CREATE INDEX on `user_id` for per-user queries and bulk invalidation.
   - CREATE INDEX on `expires_at` for cleanup queries.
   - Foreign key constraint to AspNetUsers with ON DELETE CASCADE.
   Verify `Down()` method cleanly drops the table.

4. **Document token lifecycle** — Token states and transitions:
   - **Active**: `is_used = false` AND `invalidated_at IS NULL` AND `expires_at > NOW()`.
   - **Used**: `is_used = true` — token consumed by successful password reset; cannot be reused.
   - **Expired**: `expires_at <= NOW()` — 1-hour window elapsed; return 410 to client.
   - **Invalidated**: `invalidated_at IS NOT NULL` — newer token generated for same user; treat as invalid.
   - Query for token validation: `WHERE token_hash = @hash AND is_used = false AND invalidated_at IS NULL AND expires_at > @now`.

5. **Plan token cleanup** — Expired and used tokens should be periodically cleaned up to prevent table bloat. Add a comment documenting the recommended cleanup strategy: background job (IHostedService) running daily to delete tokens where `expires_at < NOW() - INTERVAL '7 days'` (retain 7 days for audit trail, then purge). Implementation of the cleanup job is out of scope for this task (future infrastructure task).

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
Server/                                # Backend .NET 8 solution
├── Models/                            # Entity models
│   └── PasswordResetToken.cs          # NEW
├── Data/                              # Data access
│   └── ApplicationDbContext.cs         # To be modified
├── Migrations/                        # EF Core migrations
│   └── YYYYMMDD_AddPasswordResetTokens.cs  # NEW
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/PasswordResetToken.cs | Entity: Id, UserId, TokenHash, ExpiresAt, IsUsed, InvalidatedAt, CreatedAt |
| MODIFY | Server/Data/ApplicationDbContext.cs | Add DbSet<PasswordResetToken>, indexes on TokenHash/UserId/ExpiresAt, FK with CASCADE |
| CREATE | Server/Migrations/*_AddPasswordResetTokens.cs | EF Core migration for password_reset_tokens table |

## External References

- [EF Core — Migrations Overview (.NET 8)](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [EF Core — Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [OWASP — Forgot Password Cheat Sheet (Token Storage)](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html#url-tokens)
- [PostgreSQL — UUID Generation (gen_random_uuid)](https://www.postgresql.org/docs/16/functions-uuid.html)
- [SHA-256 — .NET System.Security.Cryptography](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256)

## Build Commands

- `dotnet ef migrations add AddPasswordResetTokens --project Server` — Generate migration
- `dotnet ef database update --project Server` — Apply migration
- `dotnet ef database update <previous-migration> --project Server` — Rollback migration
- `dotnet build` — Build solution

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies cleanly on empty database
- [ ] Migration rollback (`Down()`) drops password_reset_tokens table without errors
- [ ] `password_reset_tokens` table created with correct columns and types
- [ ] Index on `token_hash` exists for fast token lookup
- [ ] Index on `user_id` exists for bulk invalidation queries
- [ ] Index on `expires_at` exists for cleanup queries
- [ ] Foreign key from `user_id` to AspNetUsers with ON DELETE CASCADE
- [ ] `TokenHash` max length enforced at 64 characters (SHA-256 hex)
- [ ] `IsUsed` defaults to false
- [ ] `InvalidatedAt` is nullable
- [ ] All DateTime columns store UTC values
- [ ] UUID primary keys generated correctly

## Implementation Checklist

- [X] Create `PasswordResetToken` entity with Id, UserId, TokenHash, ExpiresAt, IsUsed, InvalidatedAt, CreatedAt
- [X] Configure Fluent API: indexes on TokenHash, UserId, ExpiresAt; FK with CASCADE delete; column constraints
- [X] Generate and verify EF Core migration with `dotnet ef migrations add AddPasswordResetTokens`
- [X] Document token lifecycle states (Active, Used, Expired, Invalidated) and validation query
- [ ] Test migration apply and rollback on clean database
