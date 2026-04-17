# Task - task_002_be_staff_account_management_api

## Requirement Reference

- User Story: us_061
- Story Location: .propel/context/tasks/EP-010/us_061/us_061.md
- Acceptance Criteria:
    - AC-1: **Given** the admin opens user management, **When** they create a new staff account, **Then** the system provisions the account with role assignment (Staff or Admin), temporary password, and email invitation.
    - AC-2: **Given** the admin views staff accounts, **When** the list loads, **Then** it displays name, email, role, status (active/deactivated), last login, and creation date.
    - AC-3: **Given** the admin deactivates a staff account, **When** they confirm deactivation, **Then** the account is disabled (cannot log in) but all historical data (audit logs, actions, verifications) is preserved.
    - AC-4: **Given** a staff account is deactivated, **When** an admin reactivates it, **Then** the account is restored with previous role and permissions intact.
- Edge Cases:
    - EC-1: Admin tries to deactivate their own account → System prevents self-deactivation with error "Cannot deactivate your own account."
    - EC-2: Admin deactivates the last admin account → System prevents it with error "At least one active admin account required."

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
| API Framework | ASP.NET Core MVC | 8.x |
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Library | Polly (resilience) | 8.x |
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

Implement the backend API endpoints for Staff Account Management under the Admin module. This task covers four endpoints: creating staff accounts with role assignment and temporary password generation, listing staff accounts with pagination/search/filter, deactivating accounts (soft-disable preserving all historical data), and reactivating previously deactivated accounts. Business rules enforce self-deactivation prevention and last-admin protection. All operations generate immutable audit log entries per NFR-012. The API follows ASP.NET Core Web API patterns with `[Authorize(Roles = "Admin")]` authorization, idempotent endpoints (NFR-034), structured logging with correlation IDs (NFR-035), and input sanitization (NFR-018).

**Effort Estimate**: 6 hours

**Traceability**: US_061 AC-1, AC-2, AC-3, AC-4, EC-1, EC-2 | FR-087, FR-088 | NFR-011, NFR-012, NFR-013, NFR-018, NFR-034, NFR-035

## Dependent Tasks

- task_003_db_staff_account_schema — Requires User entity with Status, DeactivatedAt, DeactivatedBy fields and migrations applied
- US_005 tasks — Requires ASP.NET Core Identity + JWT authentication middleware and RBAC infrastructure
- US_058 tasks — Requires admin controller base patterns and admin route configuration

## Impacted Components

| Action | Component / Module | Project |
|--------|--------------------|---------|
| CREATE | `StaffController` — Admin API controller for staff management | Server (Backend) |
| CREATE | `IStaffService` / `StaffService` — Business logic for staff CRUD | Server (Backend) |
| CREATE | `StaffDtos` — Request/response DTOs (CreateStaffRequest, StaffListResponse, StaffAccountDto) | Server (Backend) |
| CREATE | `StaffMappingProfile` — AutoMapper or manual mapping configuration | Server (Backend) |
| MODIFY | `Program.cs` or service registration — Register StaffService in DI container | Server (Backend) |
| MODIFY | Email notification service — Add staff invitation email template | Server (Backend) |

## Implementation Plan

1. **Create DTOs**: Define `CreateStaffRequest` (Name, Email, Role), `StaffAccountDto` (Id, Name, Email, Role, Status, LastLoginAt, CreatedAt), `StaffListResponse` (Items, TotalCount, Page, PageSize), and `StaffListFilter` (SearchTerm, Role, Status, Page, PageSize). Apply `[Required]`, `[EmailAddress]`, `[StringLength]` data annotations for model validation. Sanitize all string inputs against XSS (NFR-018).

2. **Create IStaffService interface and StaffService implementation**:
   - `CreateStaffAsync(CreateStaffRequest)`: Generate temporary password using `RandomNumberGenerator`, create user via ASP.NET Core Identity `UserManager.CreateAsync()`, assign role via `UserManager.AddToRoleAsync()`, trigger email invitation, log audit event.
   - `GetStaffListAsync(StaffListFilter)`: Query users with Staff/Admin roles using EF Core with pagination, search (name/email ILIKE), and status/role filtering. Return projected DTOs.
   - `DeactivateStaffAsync(targetUserId, currentUserId)`: Validate target ≠ current user (EC-1), count active admins if target is admin and prevent if count ≤ 1 (EC-2), set Status to Deactivated, set DeactivatedAt and DeactivatedBy, revoke active sessions, log audit event.
   - `ReactivateStaffAsync(targetUserId, currentUserId)`: Validate account is currently deactivated, set Status to Active, clear DeactivatedAt/DeactivatedBy, log audit event. Role and permissions preserved (no changes needed — AC-4).

3. **Create StaffController**:
   - `[ApiController]`, `[Route("api/admin/users")]`, `[Authorize(Roles = "Admin")]`
   - `POST /` — Create staff account; returns 201 Created with StaffAccountDto
   - `GET /` — List staff accounts with `[FromQuery]` StaffListFilter; returns 200 OK with StaffListResponse
   - `PUT /{id}/deactivate` — Deactivate account; returns 200 OK or 400/409 for business rule violations
   - `PUT /{id}/reactivate` — Reactivate account; returns 200 OK or 404 if not found
   - Extract current user ID from `HttpContext.User.Claims` for self-deactivation check
   - Return standardized error responses with `ProblemDetails` (RFC 7807)

4. **Implement audit logging**: For each staff management action (create, deactivate, reactivate), create an immutable `AuditLog` entry recording: EventType, TargetUserId, PerformedByUserId, Timestamp, Details (JSON). Use the existing `AuditService` from EP-TECH/EP-011.

5. **Implement email invitation**: On staff account creation, compose and send invitation email containing: welcome message, temporary password (or password reset link), login URL. Use existing email service integration from EP-005. Apply retry with exponential backoff per NFR-032.

6. **Register services**: Add `IStaffService`/`StaffService` to DI container in `Program.cs` or service registration module. Configure rate limiting for admin endpoints (100 req/min per user).

## Current Project State

```text
[Placeholder — to be updated based on completion of dependent tasks US_058, US_005]
Server/
├── Controllers/
│   └── Admin/              # Admin controllers (from US_058)
├── Services/
│   ├── Interfaces/         # Service interfaces
│   └── Implementations/    # Service implementations
├── Models/
│   ├── Entities/           # EF Core entities
│   └── Dtos/               # Request/Response DTOs
├── Data/
│   ├── AppDbContext.cs      # EF Core DbContext
│   └── Migrations/          # EF Core migrations
└── Program.cs              # Application entry point
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Models/Dtos/Staff/CreateStaffRequest.cs` | DTO with Name, Email, Role fields and validation annotations |
| CREATE | `Server/Models/Dtos/Staff/StaffAccountDto.cs` | Response DTO with Id, Name, Email, Role, Status, LastLoginAt, CreatedAt |
| CREATE | `Server/Models/Dtos/Staff/StaffListResponse.cs` | Paginated response with Items, TotalCount, Page, PageSize |
| CREATE | `Server/Models/Dtos/Staff/StaffListFilter.cs` | Query filter with SearchTerm, Role, Status, Page, PageSize |
| CREATE | `Server/Services/Interfaces/IStaffService.cs` | Interface for CreateStaffAsync, GetStaffListAsync, DeactivateStaffAsync, ReactivateStaffAsync |
| CREATE | `Server/Services/Implementations/StaffService.cs` | Business logic implementation with Identity integration, audit logging, and email invitation |
| CREATE | `Server/Controllers/Admin/StaffController.cs` | API controller with POST, GET, PUT /deactivate, PUT /reactivate endpoints |
| MODIFY | `Server/Program.cs` | Register IStaffService/StaffService in DI container |

## External References

- [ASP.NET Core Identity UserManager (v8)](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-8.0) — User creation, role assignment, password management
- [ASP.NET Core Web API authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-8.0) — Role-based authorization with `[Authorize(Roles)]`
- [EF Core 8 querying](https://learn.microsoft.com/en-us/ef/core/querying/) — Pagination, filtering, and projections
- [RFC 7807 Problem Details](https://www.rfc-editor.org/rfc/rfc7807) — Standardized error response format
- [RandomNumberGenerator for secure password generation](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator) — Cryptographically secure temporary password

## Build Commands

```bash
# Backend build
cd Server
dotnet restore
dotnet build

# Run backend
dotnet run

# Run tests
dotnet test
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] POST /api/admin/users creates staff account with correct role and returns 201
- [ ] POST /api/admin/users sends email invitation to new staff member
- [ ] POST /api/admin/users returns 400 for duplicate email
- [ ] POST /api/admin/users generates secure temporary password (bcrypt hashed per NFR-013)
- [ ] GET /api/admin/users returns paginated list with correct fields (name, email, role, status, last login, creation date)
- [ ] GET /api/admin/users search filters by name and email (case-insensitive)
- [ ] PUT /api/admin/users/{id}/deactivate disables account login while preserving all historical data
- [ ] PUT /api/admin/users/{id}/deactivate returns 400 "Cannot deactivate your own account" for self-deactivation
- [ ] PUT /api/admin/users/{id}/deactivate returns 409 "At least one active admin account required" for last admin
- [ ] PUT /api/admin/users/{id}/reactivate restores account with previous role and permissions intact
- [ ] All endpoints require Admin role authorization (401/403 for non-admin)
- [ ] All operations create immutable audit log entries (NFR-012)
- [ ] All inputs sanitized against injection attacks (NFR-018)
- [ ] Structured logging with correlation IDs on all requests (NFR-035)

## Implementation Checklist

- [ ] Create request/response DTOs (CreateStaffRequest, StaffAccountDto, StaffListResponse, StaffListFilter) with data annotations and input sanitization (NFR-018)
- [ ] Create IStaffService interface with CreateStaffAsync, GetStaffListAsync, DeactivateStaffAsync, ReactivateStaffAsync method signatures
- [ ] Implement StaffService.CreateStaffAsync — ASP.NET Core Identity user creation, role assignment, secure temp password generation (RandomNumberGenerator), email invitation trigger, audit log entry
- [ ] Implement StaffService.GetStaffListAsync — EF Core paginated query with ILIKE search on name/email, role/status filtering, and DTO projection
- [ ] Implement StaffService.DeactivateStaffAsync — Self-deactivation guard (EC-1), last-admin guard (EC-2), set Status=Deactivated with DeactivatedAt/DeactivatedBy, revoke sessions, audit log
- [ ] Implement StaffService.ReactivateStaffAsync — Validate deactivated status, restore to Active, clear deactivation fields, preserve role/permissions (AC-4), audit log
- [ ] Create StaffController with [Authorize(Roles="Admin")], POST/GET/PUT endpoints, ProblemDetails error responses (RFC 7807), and current user extraction from JWT claims
- [ ] Register IStaffService/StaffService in DI container and verify admin endpoint rate limiting configuration
