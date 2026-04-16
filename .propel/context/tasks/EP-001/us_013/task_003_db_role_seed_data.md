# Task - TASK_003

## Requirement Reference

- User Story: US_013
- Story Location: .propel/context/tasks/EP-001/us_013/us_013.md
- Acceptance Criteria:
  - AC-1: RBAC enforcement requires roles (Patient, Staff, Admin) to exist in the database for role assignment.
  - AC-2: Staff access requires the "Staff" role to be seeded and assignable to user accounts.
  - AC-3: Admin access requires the "Admin" role to be seeded and assignable to user accounts.
  - AC-4: JWT role claim population depends on role data in AspNetRoles table.
- Edge Cases:
  - Role changed during active session: role assignment change in the database propagates on next token refresh.

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

Create the database seed data and EF Core migration for the three RBAC roles (Patient, Staff, Admin) using ASP.NET Core Identity's `AspNetRoles` table. This task also creates a default admin account for initial system access, configures the role-permission mapping structure for future granular permission expansion, and ensures the seed data migration is idempotent (safe to re-run). The roles are required by the RBAC authorization middleware (task_002) and the JWT token service to populate role claims.

## Dependent Tasks

- US_005 — Authentication scaffold (provides ASP.NET Core Identity `ApplicationDbContext` and `RoleManager`)

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `RoleSeedData` | Server (Backend) | CREATE — seed data class for roles and default admin |
| `ApplicationDbContext` | Server (Backend) | MODIFY — call seed data in `OnModelCreating` or via `IHostedService` |
| EF Core migration | Server (Backend) | CREATE — migration for role seed data |

## Implementation Plan

1. **Create RoleSeedData class** — Implement a data seeding class that seeds the three roles into ASP.NET Core Identity's `AspNetRoles` table using `HasData()` in EF Core's `OnModelCreating` or alternatively via a startup `IHostedService`. Roles to seed:
   - `Patient` (NormalizedName: "PATIENT") — default role for self-registered users
   - `Staff` (NormalizedName: "STAFF") — assigned by admin to clinical/administrative staff
   - `Admin` (NormalizedName: "ADMIN") — assigned during initial setup, restricted reassignment
   Use deterministic GUIDs for role IDs to ensure idempotent seeding across environments.

2. **Seed default admin account** — Create an initial admin user during database seeding for first-time system access:
   - Email: read from configuration (`DefaultAdmin:Email` in appsettings.json) — never hardcode
   - Password: read from configuration (`DefaultAdmin:Password` in appsettings.json) — must meet complexity rules (8+ chars, 1 uppercase, 1 number, 1 special char)
   - Role: Admin
   - AccountStatus: Active (skip email verification for seed admin)
   - Mark as `MustChangePassword = true` on first login for security
   Log seed admin creation to console/audit trail. Skip creation if admin already exists (idempotent).

3. **Configure role-user assignment for registration** — Ensure the registration flow (from US_012 task_002) assigns the "Patient" role to newly registered users by default via `UserManager.AddToRoleAsync(user, "Patient")`. Staff and Admin roles are assigned only through admin user management endpoints (future US).

4. **Generate EF Core migration** — Create migration via `dotnet ef migrations add SeedRbacRoles`. Verify the generated SQL inserts the three roles into `AspNetRoles`. Verify rollback (`Down()`) removes the seeded data cleanly.

5. **Validate idempotency** — Ensure running the migration or seed multiple times does not create duplicate roles. Use `HasData()` with fixed IDs or `IHostedService` with existence checks (`RoleManager.RoleExistsAsync()`).

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
Server/                                # Backend .NET 8 solution
├── Data/                              # Data access
│   ├── ApplicationDbContext.cs         # EF Core DbContext (to be modified)
│   └── Seed/                          # Seed data
│       └── RoleSeedData.cs            # NEW
├── Migrations/                        # EF Core migrations
│   └── YYYYMMDD_SeedRbacRoles.cs      # NEW
└── Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Data/Seed/RoleSeedData.cs | Seed data for Patient, Staff, Admin roles and default admin user |
| MODIFY | Server/Data/ApplicationDbContext.cs | Call role seed data in OnModelCreating or register IHostedService |
| CREATE | Server/Migrations/*_SeedRbacRoles.cs | EF Core migration for role seed data insertion |
| MODIFY | Server/Program.cs | Register seed service if using IHostedService approach |

## External References

- [ASP.NET Core Identity — Seed Roles and Admin User](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-8.0)
- [EF Core — Data Seeding (HasData)](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding)
- [ASP.NET Core — IHostedService for Startup Tasks](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0)
- [OWASP — Secure Default Configuration](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html#default-credentials)

## Build Commands

- `dotnet ef migrations add SeedRbacRoles --project Server` — Generate migration
- `dotnet ef database update --project Server` — Apply migration
- `dotnet ef database update <previous-migration> --project Server` — Rollback migration
- `dotnet build` — Build solution

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies cleanly on empty database
- [ ] Migration rollback (`Down()`) reverts seed data without errors
- [ ] AspNetRoles table contains exactly 3 rows: Patient, Staff, Admin
- [ ] NormalizedName values are uppercase (PATIENT, STAFF, ADMIN)
- [ ] Role IDs are deterministic (same across environments)
- [ ] Default admin account created with Admin role
- [ ] Default admin reads credentials from configuration (not hardcoded)
- [ ] Default admin has `MustChangePassword = true`
- [ ] Running migration twice does not create duplicate roles (idempotent)
- [ ] Newly registered patients auto-assigned "Patient" role

## Implementation Checklist

- [ ] Create `RoleSeedData` class seeding Patient, Staff, Admin roles with deterministic GUIDs
- [ ] Seed default admin account from configuration (email, password from appsettings.json)
- [ ] Configure `OnModelCreating` or `IHostedService` to invoke role seed data
- [ ] Generate and verify EF Core migration with `dotnet ef migrations add SeedRbacRoles`
- [ ] Validate idempotency: re-running seed does not create duplicates
- [ ] Test migration apply and rollback on clean database
