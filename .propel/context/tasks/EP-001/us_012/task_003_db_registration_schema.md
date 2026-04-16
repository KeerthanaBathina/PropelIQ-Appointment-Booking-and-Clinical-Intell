# Task - TASK_003

## Requirement Reference

- User Story: US_012
- Story Location: .propel/context/tasks/EP-001/us_012/us_012.md
- Acceptance Criteria:
  - AC-1: Account creation with "pending verification" status requires User entity with account_status field and EmailVerificationToken storage.
  - AC-2: Verification flow requires token lookup with expiration check, and account_status update to "active".
  - AC-3: Expired token handling requires expires_at column on email_verification_tokens table.
  - AC-4: Duplicate email detection requires unique constraint on email column in users table.
- Edge Cases:
  - Rate limiting counters may use Redis (not DB), but resend tracking column (resend_count, last_resend_at) provides audit trail.

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

Create the database schema and EF Core migrations for patient registration and email verification. This includes extending the ASP.NET Core Identity `ApplicationUser` entity with patient-specific fields (first_name, last_name, phone_number, date_of_birth, account_status), creating the `email_verification_tokens` table for secure token storage with 1-hour expiry, and adding required indexes and constraints. The schema supports email uniqueness (DR-001), password hash storage (FR-004), and audit trail entries for authentication events (FR-006, NFR-012).

## Dependent Tasks

- US_005 — Authentication scaffold (provides base ASP.NET Core Identity `ApplicationUser` entity and DbContext)

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `ApplicationUser` entity | Server (Backend) | MODIFY — extend with patient registration fields |
| `EmailVerificationToken` entity | Server (Backend) | CREATE — new entity for verification tokens |
| `ApplicationDbContext` | Server (Backend) | MODIFY — add DbSet and configure entity mappings |
| EF Core migration | Server (Backend) | CREATE — migration for schema changes |

## Implementation Plan

1. **Extend ApplicationUser entity** — Add properties to the ASP.NET Core Identity User model:
   - `FirstName` (string, required, max 100)
   - `LastName` (string, required, max 100)
   - `DateOfBirth` (DateOnly, required)
   - `AccountStatus` (enum: PendingVerification, Active, Suspended, Deactivated — default PendingVerification)
   - `CreatedAt` (DateTime, UTC, set on creation)
   - `UpdatedAt` (DateTime, UTC, updated on modification)
   - `DeletedAt` (DateTime?, nullable, for soft delete per DR-021)
   - Note: `Email`, `PhoneNumber`, `PasswordHash` are inherited from IdentityUser.

2. **Create EmailVerificationToken entity** — New table `email_verification_tokens`:
   - `Id` (UUID, primary key)
   - `UserId` (UUID, foreign key to AspNetUsers)
   - `TokenHash` (string, required — store hashed token, never plaintext)
   - `ExpiresAt` (DateTime, UTC — 1 hour from creation per AC-3)
   - `IsUsed` (bool, default false — prevent token reuse)
   - `CreatedAt` (DateTime, UTC)

3. **Configure entity mappings in DbContext** — Use Fluent API:
   - `ApplicationUser`: unique index on Email (DR-001), global query filter for soft delete (`DeletedAt == null`).
   - `EmailVerificationToken`: index on `TokenHash` for fast lookup, index on `UserId` for per-user queries, foreign key to `ApplicationUser` with cascade delete.

4. **Create EF Core migration** — Generate migration via `dotnet ef migrations add AddPatientRegistrationSchema`. Verify generated SQL includes:
   - ALTER TABLE `AspNetUsers` ADD columns for FirstName, LastName, DateOfBirth, AccountStatus, CreatedAt, UpdatedAt, DeletedAt.
   - CREATE TABLE `email_verification_tokens` with all columns, indexes, and FK constraint.
   - CREATE INDEX on `email_verification_tokens.token_hash`.
   - CREATE INDEX on `email_verification_tokens.user_id`.

5. **Add rollback support** — Verify `Down()` method in migration correctly reverts all changes (drop table, drop columns). Test rollback with `dotnet ef database update <previous-migration>`.

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
Server/                               # Backend .NET 8 solution
├── Models/                           # Entity models
│   ├── ApplicationUser.cs            # ASP.NET Core Identity user (to be extended)
│   └── EmailVerificationToken.cs     # NEW
├── Data/                             # Data access
│   └── ApplicationDbContext.cs       # EF Core DbContext (to be modified)
├── Migrations/                       # EF Core migrations
│   └── YYYYMMDD_AddPatientRegistrationSchema.cs  # NEW
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Models/ApplicationUser.cs | Add FirstName, LastName, DateOfBirth, AccountStatus, CreatedAt, UpdatedAt, DeletedAt properties |
| CREATE | Server/Models/EmailVerificationToken.cs | New entity with Id, UserId, TokenHash, ExpiresAt, IsUsed, CreatedAt |
| CREATE | Server/Models/AccountStatus.cs | Enum: PendingVerification, Active, Suspended, Deactivated |
| MODIFY | Server/Data/ApplicationDbContext.cs | Add DbSet<EmailVerificationToken>, configure Fluent API mappings and indexes |
| CREATE | Server/Migrations/*_AddPatientRegistrationSchema.cs | EF Core migration for schema changes |

## External References

- [EF Core — Migrations Overview (.NET 8)](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [ASP.NET Core Identity — Customize Identity Model](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/customize-identity-model?view=aspnetcore-8.0)
- [EF Core — Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [PostgreSQL — UUID Generation (gen_random_uuid)](https://www.postgresql.org/docs/16/functions-uuid.html)
- [EF Core — Global Query Filters for Soft Delete](https://learn.microsoft.com/en-us/ef/core/querying/filters)

## Build Commands

- `dotnet ef migrations add AddPatientRegistrationSchema --project Server` — Generate migration
- `dotnet ef database update --project Server` — Apply migration
- `dotnet ef database update <previous-migration> --project Server` — Rollback migration
- `dotnet build` — Build solution

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies cleanly on empty database
- [ ] Migration rollback (`Down()`) reverts all changes without errors
- [ ] Unique constraint on email prevents duplicate registration
- [ ] `email_verification_tokens` table created with correct columns and types
- [ ] Index on `token_hash` exists for fast token lookup
- [ ] Foreign key from `email_verification_tokens.user_id` to `AspNetUsers.Id` with cascade delete
- [ ] `AccountStatus` column defaults to `PendingVerification`
- [ ] Soft delete filter excludes records with non-null `DeletedAt`
- [ ] All DateTime columns store UTC values
- [ ] UUID primary keys generated correctly

## Implementation Checklist

- [ ] Extend `ApplicationUser` with FirstName, LastName, DateOfBirth, AccountStatus, CreatedAt, UpdatedAt, DeletedAt
- [ ] Create `AccountStatus` enum (PendingVerification, Active, Suspended, Deactivated)
- [ ] Create `EmailVerificationToken` entity (Id, UserId, TokenHash, ExpiresAt, IsUsed, CreatedAt)
- [ ] Configure Fluent API: unique index on Email, indexes on TokenHash and UserId, FK constraint, soft delete filter
- [ ] Generate and verify EF Core migration with `dotnet ef migrations add`
- [ ] Test migration apply and rollback on clean database
