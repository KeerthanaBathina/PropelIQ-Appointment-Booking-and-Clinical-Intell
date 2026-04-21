# Task - TASK_002

## Requirement Reference

- User Story: US_013
- Story Location: .propel/context/tasks/EP-001/us_013/us_013.md
- Acceptance Criteria:
  - AC-1: Given RBAC is implemented, When a Patient user attempts to access a staff-only endpoint (e.g., queue management), Then the system returns 403 Forbidden.
  - AC-2: Given a Staff user is authenticated, When they access staff dashboard endpoints, Then the system grants access and returns the requested data.
  - AC-3: Given an Admin user is authenticated, When they access admin configuration endpoints, Then full system configuration is accessible.
  - AC-4: Given a JWT token contains a role claim, When the authorization middleware processes the request, Then it validates the role against the endpoint's `[Authorize(Roles = "...")]` attribute.
- Edge Cases:
  - Role changed during active session: existing JWT remains valid until expiry; new role applies on next token refresh.
  - Tampered role claims: JWT signature validation rejects the token with 401 Unauthorized.

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
| API Framework | ASP.NET Core MVC | 8.x |
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |
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

Implement server-side role-based access control (RBAC) using ASP.NET Core Identity roles and JWT role claims on .NET 8. This task configures three roles (Patient, Staff, Admin) with the principle of least privilege (NFR-011), embeds roles as claims in JWT tokens (AC-4), creates authorization policies for each role, applies `[Authorize(Roles = "...")]` attributes to controller endpoints, returns 403 Forbidden for unauthorized role access (AC-1), validates JWT signatures to reject tampered tokens with 401 (edge case), and logs all authorization events to the audit trail (NFR-012). The middleware ensures that token refresh propagates role changes.

## Dependent Tasks

- US_005 — Authentication scaffold (provides ASP.NET Core Identity setup, JWT token generation/validation, base controller patterns)
- task_003_db_role_seed_data — Role seed data must exist in the database for role assignment to function

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `JwtTokenService` | Server (Backend) | MODIFY — embed role claim in JWT access token |
| `RbacAuthorizationPolicies` | Server (Backend) | CREATE — define named authorization policies per role |
| `AuthorizationMiddleware` config | Server (Backend) | MODIFY — register policies and configure fallback behavior |
| Controller endpoints | Server (Backend) | MODIFY — apply `[Authorize(Roles)]` attributes to existing/future controllers |
| `RoleClaimsTransformer` | Server (Backend) | CREATE — claims transformation to normalize role claims |
| `ForbiddenResponseHandler` | Server (Backend) | CREATE — custom handler returning structured 403 JSON response |
| `UnauthorizedResponseHandler` | Server (Backend) | CREATE — custom handler returning structured 401 JSON response |
| `Program.cs` | Server (Backend) | MODIFY — register authorization policies, handlers, and middleware |

## Implementation Plan

1. **Configure ASP.NET Core Identity roles** — Ensure the three roles (Patient, Staff, Admin) are registered in ASP.NET Core Identity via `RoleManager<IdentityRole>`. Roles are seeded by task_003; this task configures the runtime role resolution. Assign roles to users during registration or admin user management using `UserManager.AddToRoleAsync()`.

2. **Embed role claim in JWT token** — Modify the JWT token generation service to include the user's role as a claim in the access token: `ClaimTypes.Role` with value "Patient", "Staff", or "Admin". When the token is refreshed, re-query the user's current role from the database to propagate role changes (edge case: role changed during active session). The refresh token endpoint must re-evaluate the role and issue a new JWT with the updated claim.

3. **Create authorization policies** — Define named policies in `Program.cs` using `builder.Services.AddAuthorization()`:
   - `"PatientOnly"` — requires role "Patient"
   - `"StaffOnly"` — requires role "Staff"
   - `"AdminOnly"` — requires role "Admin"
   - `"StaffOrAdmin"` — requires role "Staff" OR "Admin"
   - `"AnyAuthenticated"` — requires authentication (no role restriction)
   These policies use `policy.RequireRole(...)` for clean role-based checks.

4. **Implement custom response handlers** — Create `ForbiddenResponseHandler` implementing `IAuthorizationMiddlewareResultHandler` to return a structured JSON 403 response: `{ "error": "Forbidden", "message": "You do not have permission to access this resource." }` instead of the default empty response. Create `UnauthorizedResponseHandler` returning structured JSON 401: `{ "error": "Unauthorized", "message": "Authentication required. Please sign in." }`. Both handlers log the event to the audit trail with user ID, attempted resource, IP address, and timestamp (NFR-012).

5. **Apply authorization attributes to controllers** — Annotate controller endpoints with the appropriate policy attributes:
   - Patient endpoints (appointments, intake, health records): `[Authorize(Policy = "PatientOnly")]`
   - Staff endpoints (queue management, document upload, clinical tools): `[Authorize(Policy = "StaffOnly")]`
   - Admin endpoints (configuration, user management): `[Authorize(Policy = "AdminOnly")]`
   - Shared endpoints (dashboard router, profile): `[Authorize(Policy = "AnyAuthenticated")]`
   - Staff + Admin shared endpoints (patient search): `[Authorize(Policy = "StaffOrAdmin")]`

6. **Implement JWT signature validation** — Ensure the JWT validation parameters in `TokenValidationParameters` enforce signature validation (`ValidateIssuerSigningKey = true`), issuer validation, audience validation, and lifetime validation. Tampered tokens with invalid signatures must be rejected with 401 Unauthorized before role evaluation. Log failed signature validation attempts to the audit trail as security events.

7. **Add audit logging for authorization events** — Log all authorization decisions to the audit trail (NFR-012):
   - Successful access: action="data_access", resource_type, resource_id, user_id, role
   - Forbidden access (403): action="access_denied", resource_type, user_id, role, attempted_endpoint
   - Unauthorized access (401): action="auth_failure", ip_address, user_agent
   Use Serilog structured logging with correlation IDs (NFR-035).

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
Server/                                # Backend .NET 8 solution
├── Controllers/                       # API controllers
├── Services/                          # Business logic
│   └── JwtTokenService.cs             # JWT generation (to be modified)
├── Authorization/                     # Authorization components
│   ├── RbacAuthorizationPolicies.cs   # NEW - policy definitions
│   ├── ForbiddenResponseHandler.cs    # NEW - 403 response handler
│   └── UnauthorizedResponseHandler.cs # NEW - 401 response handler
├── Claims/                            # Claims components
│   └── RoleClaimsTransformer.cs       # NEW - role claims normalization
├── Models/                            # Entity models
└── Program.cs                         # Service registration
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Authorization/RbacAuthorizationPolicies.cs | Named policies: PatientOnly, StaffOnly, AdminOnly, StaffOrAdmin, AnyAuthenticated |
| CREATE | Server/Authorization/ForbiddenResponseHandler.cs | Custom 403 JSON response with audit logging |
| CREATE | Server/Authorization/UnauthorizedResponseHandler.cs | Custom 401 JSON response with audit logging |
| CREATE | Server/Claims/RoleClaimsTransformer.cs | Claims transformation normalizing role claim format |
| MODIFY | Server/Services/JwtTokenService.cs | Embed ClaimTypes.Role in JWT; re-query role on refresh |
| MODIFY | Server/Controllers/*.cs | Apply [Authorize(Policy = "...")] attributes to endpoints |
| MODIFY | Server/Program.cs | Register authorization policies, custom handlers, claims transformer |

## External References

- [ASP.NET Core Authorization — Policy-Based Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-8.0)
- [ASP.NET Core Authorization — Role-Based Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-8.0)
- [ASP.NET Core — Custom Authorization Handlers](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased?view=aspnetcore-8.0)
- [JWT Bearer Token — TokenValidationParameters](https://learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters)
- [OWASP — Access Control Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Access_Control_Cheat_Sheet.html)
- [ASP.NET Core — Claims Transformation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/claims?view=aspnetcore-8.0)

## Build Commands

- `dotnet build` — Build solution
- `dotnet run --project Server` — Run backend
- `dotnet test` — Run tests

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Patient user calling staff endpoint (e.g., GET /api/staff/queue) receives 403 Forbidden
- [ ] Patient user calling admin endpoint (e.g., GET /api/admin/config) receives 403 Forbidden
- [ ] Staff user calling staff endpoint receives 200 OK with data
- [ ] Staff user calling admin endpoint receives 403 Forbidden
- [ ] Admin user calling admin endpoint receives 200 OK with data
- [ ] Admin user calling staff endpoint receives 403 (unless StaffOrAdmin policy applied)
- [ ] JWT access token contains ClaimTypes.Role with correct value
- [ ] Token refresh re-queries role from database (role change propagation)
- [ ] Tampered JWT (modified role claim) returns 401 Unauthorized
- [ ] Expired JWT returns 401 Unauthorized
- [ ] 403 responses return structured JSON body
- [ ] 401 responses return structured JSON body
- [ ] Authorization events logged to audit trail (NFR-012) with correlation IDs
- [ ] Swagger documentation shows required roles per endpoint (NFR-038)

## Implementation Checklist

- [x] Configure ASP.NET Core Identity roles and verify RoleManager resolves Patient, Staff, Admin
- [x] Modify `JwtTokenService` to embed `ClaimTypes.Role` in JWT; re-query role on token refresh
- [x] Create named authorization policies (PatientOnly, StaffOnly, AdminOnly, StaffOrAdmin, AnyAuthenticated) in Program.cs
- [x] Implement `ForbiddenResponseHandler` returning structured 403 JSON with audit logging
- [x] Implement `UnauthorizedResponseHandler` returning structured 401 JSON with audit logging
- [x] Apply `[Authorize(Policy = "...")]` attributes to all controller endpoints per role mapping
- [x] Verify JWT signature validation rejects tampered tokens with 401
- [x] Add Serilog structured audit logging for all authorization decisions (NFR-012, NFR-035)
