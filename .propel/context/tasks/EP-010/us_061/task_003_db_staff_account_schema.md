# Task - task_003_db_staff_account_schema

## Requirement Reference

- User Story: us_061
- Story Location: .propel/context/tasks/EP-010/us_061/us_061.md
- Acceptance Criteria:
    - AC-1: **Given** the admin opens user management, **When** they create a new staff account, **Then** the system provisions the account with role assignment (Staff or Admin), temporary password, and email invitation.
    - AC-2: **Given** the admin views staff accounts, **When** the list loads, **Then** it displays name, email, role, status (active/deactivated), last login, and creation date.
    - AC-3: **Given** the admin deactivates a staff account, **When** they confirm deactivation, **Then** the account is disabled (cannot log in) but all historical data (audit logs, actions, verifications) is preserved.
    - AC-4: **Given** a staff account is deactivated, **When** an admin reactivates it, **Then** the account is restored with previous role and permissions intact.
- Edge Cases:
    - EC-1: Admin tries to deactivate their own account → Enforced at application layer (task_002)
    - EC-2: Admin deactivates the last admin account → Enforced at application layer (task_002); schema supports the query pattern

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
| Frontend | N/A | - |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| AI/ML | N/A | - |
| Vector Store | N/A | - |
| AI Gateway | N/A | - |
| Mobile | N/A | - |

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

Add the database schema changes required for staff account management to the existing User entity. This includes adding fields for account status tracking (Active/Deactivated), deactivation metadata (who/when), and last login timestamp. Create an EF Core code-first migration, add appropriate indexes for query performance on the staff list endpoint, and configure audit log event types for staff management operations. The schema preserves all historical data on deactivation (soft-disable, not delete) per FR-088 and HIPAA compliance requirements.

**Effort Estimate**: 3 hours

**Traceability**: US_061 AC-1, AC-2, AC-3, AC-4 | FR-087, FR-088 | DR-001, DR-009, NFR-012, NFR-033

## Dependent Tasks

- EP-DATA tasks — Requires base User entity and AppDbContext established via EP-DATA epic
- US_005 tasks — Requires ASP.NET Core Identity User entity scaffold with base fields (Id, Email, PasswordHash, Role)

## Impacted Components

| Action | Component / Module | Project |
|--------|--------------------|---------|
| MODIFY | `User` entity — Add Status, DeactivatedAt, DeactivatedBy, LastLoginAt fields | Server (Backend) |
| MODIFY | `AppDbContext` — Add entity configuration for new User fields, indexes | Server (Backend) |
| CREATE | EF Core migration — `AddStaffAccountManagementFields` migration file | Server (Backend) |
| CREATE | Seed data script — Sample staff accounts for development environment | Server (Backend) |
| MODIFY | `AuditLog` event type enum — Add StaffCreated, StaffDeactivated, StaffReactivated event types | Server (Backend) |

## Implementation Plan

1. **Extend User entity**: Add the following properties to the existing User entity class:
   - `Status` (`UserStatus` enum: Active = 0, Deactivated = 1) — Default: Active. Used to control login access; deactivated users are rejected at authentication.
   - `DeactivatedAt` (`DateTime?`) — Nullable timestamp set when account is deactivated; cleared on reactivation.
   - `DeactivatedBy` (`Guid?`) — Nullable FK to User.Id recording which admin performed deactivation; cleared on reactivation.
   - `LastLoginAt` (`DateTime?`) — Updated on each successful authentication event.
   - `CreatedAt` (`DateTime`) — Set on account creation (if not already present from Identity scaffold).

2. **Configure entity in AppDbContext**: In `OnModelCreating`, add Fluent API configuration:
   - Map `Status` as integer column with default value `0` (Active).
   - Configure `DeactivatedBy` as optional FK to User table (self-referencing relationship).
   - Add composite index on `(Status, Role)` for efficient staff list filtering (`IX_User_Status_Role`).
   - Add index on `Email` for search queries (may already exist from Identity — verify).
   - Add index on `LastLoginAt` for sort ordering on staff list.

3. **Create EF Core migration**: Run `dotnet ef migrations add AddStaffAccountManagementFields` to generate migration. Verify the generated migration SQL:
   - ALTER TABLE adds new columns with appropriate defaults and nullability.
   - CREATE INDEX for the composite and single-column indexes.
   - Includes DOWN migration for rollback support.

4. **Add audit log event types**: Extend the `AuditEventType` enum (or equivalent) with:
   - `StaffAccountCreated` — Logged when admin creates a new staff account
   - `StaffAccountDeactivated` — Logged when admin deactivates a staff account
   - `StaffAccountReactivated` — Logged when admin reactivates a staff account
   
   Ensure AuditLog table can store these events with TargetUserId, PerformedByUserId, and Details (JSON).

5. **Create seed data**: Add development seed data with 5-8 sample staff accounts across roles (3 Staff, 2 Admin) with varied statuses (active/deactivated) and last login dates for testing the staff list UI and filtering.

## Current Project State

```text
[Placeholder — to be updated based on completion of dependent tasks EP-DATA, US_005]
Server/
├── Models/
│   └── Entities/
│       ├── User.cs           # ASP.NET Core Identity User entity
│       └── AuditLog.cs       # Audit log entity
├── Data/
│   ├── AppDbContext.cs        # EF Core DbContext
│   ├── Migrations/            # EF Core migration files
│   └── Seeds/                 # Seed data scripts
└── Enums/
    ├── UserStatus.cs          # [NEW] User account status enum
    └── AuditEventType.cs      # Audit log event types
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Enums/UserStatus.cs` | Enum with Active = 0, Deactivated = 1 values |
| MODIFY | `Server/Models/Entities/User.cs` | Add Status, DeactivatedAt, DeactivatedBy, LastLoginAt, CreatedAt properties |
| MODIFY | `Server/Data/AppDbContext.cs` | Add Fluent API configuration for new User fields, indexes (IX_User_Status_Role, IX_User_LastLoginAt), and self-referencing FK |
| CREATE | `Server/Data/Migrations/YYYYMMDD_AddStaffAccountManagementFields.cs` | EF Core migration adding columns and indexes with rollback support |
| MODIFY | `Server/Enums/AuditEventType.cs` | Add StaffAccountCreated, StaffAccountDeactivated, StaffAccountReactivated values |
| CREATE | `Server/Data/Seeds/StaffAccountSeedData.cs` | Seed data with 5-8 sample staff accounts (varied roles, statuses, login dates) |

## External References

- [EF Core 8 Migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli) — Code-first migration creation and application
- [EF Core Fluent API - Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes?tabs=fluent-api) — Composite and single-column index configuration
- [PostgreSQL 16 ALTER TABLE](https://www.postgresql.org/docs/16/sql-altertable.html) — Column addition with defaults and constraints
- [ASP.NET Core Identity IdentityUser](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.identityuser) — Base user entity extension patterns

## Build Commands

```bash
# Backend build
cd Server
dotnet restore
dotnet build

# Create migration
dotnet ef migrations add AddStaffAccountManagementFields

# Apply migration
dotnet ef database update

# Rollback migration
dotnet ef database update <PreviousMigrationName>

# Run tests
dotnet test
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Migration applies successfully to local PostgreSQL database
- [ ] Migration rollback (DOWN) executes without errors
- [ ] User entity has Status (default: Active), DeactivatedAt, DeactivatedBy, LastLoginAt, CreatedAt fields
- [ ] Composite index IX_User_Status_Role exists and improves query plan for staff list filtering
- [ ] Index IX_User_LastLoginAt exists for sort ordering
- [ ] Self-referencing FK on DeactivatedBy -> User.Id configured correctly (nullable)
- [ ] AuditEventType enum includes StaffAccountCreated, StaffAccountDeactivated, StaffAccountReactivated
- [ ] Seed data creates 5-8 sample staff accounts with varied roles and statuses
- [ ] Deactivated users retain all historical audit logs and associated records (no cascade delete)

## Implementation Checklist

- [ ] Create `UserStatus` enum (Active = 0, Deactivated = 1) in `Server/Enums/UserStatus.cs`
- [ ] Extend `User` entity with Status (UserStatus, default Active), DeactivatedAt (DateTime?), DeactivatedBy (Guid?), LastLoginAt (DateTime?), and CreatedAt (DateTime) properties
- [ ] Configure Fluent API in AppDbContext: Status column default, DeactivatedBy self-referencing FK, composite index (Status, Role), index on LastLoginAt, index on Email (verify not duplicate)
- [ ] Generate and verify EF Core migration `AddStaffAccountManagementFields` with UP and DOWN methods
- [ ] Extend AuditEventType enum with StaffAccountCreated, StaffAccountDeactivated, StaffAccountReactivated event types
