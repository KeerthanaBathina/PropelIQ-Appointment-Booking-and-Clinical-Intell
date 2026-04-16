# Task - task_001_be_identity_configuration

## Requirement Reference

- User Story: us_005
- Story Location: .propel/context/tasks/EP-TECH/us_005/us_005.md
- Acceptance Criteria:
  - AC-2: Given a JWT token is issued, When the token is decoded, Then it contains user ID, email, and role claims (Patient, Staff, or Admin).
  - AC-5: Given bcrypt is configured, When a password is hashed, Then the hash uses minimum 10 rounds and the plaintext password is never stored.
- Edge Case:
  - None directly applicable (Identity configuration is prerequisite infrastructure).

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
| Authentication | ASP.NET Core Identity | 8.x |
| ORM | Entity Framework Core | 8.x |
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

Configure ASP.NET Core Identity with Entity Framework Core on the existing `ApplicationDbContext`, define custom `ApplicationUser` and `ApplicationRole` entities, configure the bcrypt-compatible password hasher with a minimum work factor of 10 iterations, set up RBAC with three roles (Patient, Staff, Admin) seeded via a migration, and configure Identity options (lockout, password complexity). This task establishes the user management and password hashing foundation that the JWT token service (task_002) will consume.

## Dependent Tasks

- US_001 task_001_be_solution_scaffold — Backend solution with layered architecture must exist.
- US_003 task_002_be_efcore_integration — EF Core with ApplicationDbContext and PostgreSQL connection must be configured.

## Impacted Components

- **MODIFY** `src/UPACIP.DataAccess/UPACIP.DataAccess.csproj` — Add Microsoft.AspNetCore.Identity.EntityFrameworkCore NuGet package
- **NEW** `src/UPACIP.DataAccess/Entities/ApplicationUser.cs` — Custom IdentityUser extending with application-specific fields
- **NEW** `src/UPACIP.DataAccess/Entities/ApplicationRole.cs` — Custom IdentityRole for RBAC
- **MODIFY** `src/UPACIP.DataAccess/ApplicationDbContext.cs` — Change base class to `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>` and configure Identity table mappings
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register Identity services with bcrypt password hasher, lockout policy, and password complexity rules
- **NEW** `src/UPACIP.DataAccess/Migrations/{timestamp}_AddIdentitySchema.cs` — EF Core migration adding Identity tables and seeding roles

## Implementation Plan

1. **Install NuGet packages**: Add `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (8.x) to the DataAccess project. This provides the `IdentityDbContext` base class and EF Core stores for Identity.
2. **Create ApplicationUser entity**: Define `ApplicationUser` extending `IdentityUser<Guid>` in the DataAccess/Entities folder. Add application-specific properties from design.md: `FullName` (string), `DateOfBirth` (DateOnly?), `PhoneNumber` (inherited), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset), `DeletedAt` (DateTimeOffset?, soft delete per DR-021). Use Guid as the primary key type for security (non-enumerable IDs).
3. **Create ApplicationRole entity**: Define `ApplicationRole` extending `IdentityRole<Guid>` with a `Description` property. The three roles (Patient, Staff, Admin) will be seeded via migration data.
4. **Update ApplicationDbContext**: Change the base class from `DbContext` to `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`. Override `OnModelCreating` to configure Identity table names (use `asp_net_` prefix convention for PostgreSQL snake_case) and seed the three roles with stable GUIDs.
5. **Configure Identity in Program.cs**: Register Identity using `AddIdentity<ApplicationUser, ApplicationRole>()` with `AddEntityFrameworkStores<ApplicationDbContext>()`. Configure options: password requires minimum 8 characters, uppercase, lowercase, digit, and special character; lockout after 5 failed attempts for 30 minutes (NFR-016); require unique email.
6. **Configure bcrypt password hasher**: ASP.NET Core Identity uses PBKDF2 by default. To meet NFR-013 (bcrypt with 10+ rounds), register a custom `IPasswordHasher<ApplicationUser>` that uses `BCrypt.Net-Next` NuGet package with work factor 10. Add `BCrypt.Net-Next` to the Api project.
7. **Generate and apply migration**: Run `dotnet ef migrations add AddIdentitySchema` to create the migration with Identity tables and seeded roles, then `dotnet ef database update` to apply.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlerMiddleware.cs
│   │   │   └── CorrelationIdMiddleware.cs
│   │   └── Controllers/
│   │       └── WeatherForecastController.cs
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   └── Caching/
│   │       ├── ICacheService.cs
│   │       └── RedisCacheService.cs
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       └── Migrations/
└── scripts/
    └── ...
```

> Assumes US_001, US_003, US_004 tasks are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.DataAccess/UPACIP.DataAccess.csproj | Add `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 8.x NuGet package |
| CREATE | src/UPACIP.DataAccess/Entities/ApplicationUser.cs | Custom IdentityUser<Guid> with FullName, DateOfBirth, CreatedAt, UpdatedAt, DeletedAt fields |
| CREATE | src/UPACIP.DataAccess/Entities/ApplicationRole.cs | Custom IdentityRole<Guid> with Description property |
| MODIFY | src/UPACIP.DataAccess/ApplicationDbContext.cs | Change base to `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`, configure table names, seed Patient/Staff/Admin roles |
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add `BCrypt.Net-Next` NuGet package for bcrypt password hashing |
| CREATE | src/UPACIP.Api/Security/BcryptPasswordHasher.cs | Custom IPasswordHasher<ApplicationUser> implementation using BCrypt.Net with work factor 10 |
| MODIFY | src/UPACIP.Api/Program.cs | Register `AddIdentity<ApplicationUser, ApplicationRole>` with EF stores, password policy, lockout policy, and custom bcrypt hasher |
| CREATE | src/UPACIP.DataAccess/Migrations/{timestamp}_AddIdentitySchema.cs | Migration adding Identity tables and seeding three roles |

## External References

- [ASP.NET Core Identity with EF Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-8.0)
- [ASP.NET Core Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-8.0)
- [Custom password hasher in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-8.0#password-hasher-options)
- [BCrypt.Net-Next NuGet](https://www.nuget.org/packages/BCrypt.Net-Next)
- [ASP.NET Core Identity with PostgreSQL](https://www.npgsql.org/efcore/)

## Build Commands

```powershell
# Restore and build
dotnet restore UPACIP.sln
dotnet build UPACIP.sln --configuration Debug

# Generate Identity migration
dotnet ef migrations add AddIdentitySchema --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Apply migration
dotnet ef database update --project src/UPACIP.DataAccess --startup-project src/UPACIP.Api

# Verify roles seeded
psql -h localhost -p 5432 -U upacip_app -d upacip -c "SELECT * FROM asp_net_roles;"
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors and zero warnings
- [ ] `dotnet ef migrations add` generates Identity schema migration without errors
- [ ] `dotnet ef database update` creates Identity tables in PostgreSQL
- [ ] Three roles (Patient, Staff, Admin) exist in the roles table after migration
- [ ] Password hashing uses bcrypt with work factor 10 (verify by inspecting hash prefix `$2a$10$`)
- [ ] Plaintext passwords are never stored in the database
- [ ] Lockout activates after 5 failed login attempts for 30 minutes

## Implementation Checklist

- [ ] Add `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 8.x to `UPACIP.DataAccess.csproj`
- [ ] Create `src/UPACIP.DataAccess/Entities/ApplicationUser.cs` extending `IdentityUser<Guid>` with `FullName`, `DateOfBirth`, `CreatedAt`, `UpdatedAt`, `DeletedAt` properties
- [ ] Create `src/UPACIP.DataAccess/Entities/ApplicationRole.cs` extending `IdentityRole<Guid>` with `Description` property
- [ ] Update `ApplicationDbContext` to inherit `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>`, configure table naming, and seed Patient/Staff/Admin roles with stable GUIDs
- [ ] Add `BCrypt.Net-Next` NuGet to `UPACIP.Api.csproj` and create `Security/BcryptPasswordHasher.cs` implementing `IPasswordHasher<ApplicationUser>` with work factor 10
- [ ] Register Identity in `Program.cs` with `AddIdentity`, EF stores, password policy (8+ chars, upper, lower, digit, special), lockout (5 attempts / 30 min), unique email, and custom bcrypt hasher
- [ ] Run `dotnet ef migrations add AddIdentitySchema` and `dotnet ef database update` to create Identity tables and seed roles
