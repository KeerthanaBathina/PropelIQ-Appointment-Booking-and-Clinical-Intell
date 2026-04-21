# Task - TASK_002

## Requirement Reference

- User Story: US_012
- Story Location: .propel/context/tasks/EP-001/us_012/us_012.md
- Acceptance Criteria:
  - AC-1: Given valid name, email, phone, DOB, and password submitted, Then account created with status "pending verification" and verification email sent within 2 minutes.
  - AC-2: Given verification link clicked, Then account status changes to "active" and user redirected to login page with success message.
  - AC-3: Given expired verification link (1-hour expiry) clicked, Then system displays "Link expired" and offers resend.
  - AC-4: Given registration with already-registered email, Then system displays "An account with this email already exists" without revealing verification status.
  - AC-5: Given password does not meet complexity (8+ chars, 1 uppercase, 1 number, 1 special char), Then return 400 with specific missing criteria.
- Edge Cases:
  - Rate limiting: max 3 resend requests per 5 minutes per email address.
  - Disposable email domains: allow in Phase 1.

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
| Email Service | SMTP (SendGrid Free Tier / Gmail SMTP) | - |
| Caching | Upstash Redis | 7.x |
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

Implement the backend API endpoints for patient registration with email verification using ASP.NET Core Identity on .NET 8. This includes: account creation with bcrypt password hashing (10+ rounds per FR-004/NFR-013), email verification token generation and validation (1-hour expiry), verification email dispatch via SMTP, email uniqueness check endpoint (without leaking verification status per AC-4), resend verification with rate limiting (max 3 per 5 minutes per email), and server-side password complexity validation. All authentication events must be logged to the audit trail per FR-006/NFR-012.

## Dependent Tasks

- US_005 — Authentication scaffold (provides ASP.NET Core Identity setup, JWT configuration, base controller patterns)
- task_003_db_registration_schema — Database schema and migrations for Patient/User and EmailVerificationToken tables

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `AuthController` | Server (Backend) | CREATE — registration and verification endpoints |
| `RegistrationRequest` DTO | Server (Backend) | CREATE — request model with validation attributes |
| `RegistrationResponse` DTO | Server (Backend) | CREATE — response model |
| `IRegistrationService` | Server (Backend) | CREATE — service interface |
| `RegistrationService` | Server (Backend) | CREATE — business logic for registration, verification, resend |
| `IEmailService` | Server (Backend) | CREATE — email sending interface |
| `SmtpEmailService` | Server (Backend) | CREATE — SMTP implementation for verification emails |
| `EmailVerificationTokenGenerator` | Server (Backend) | CREATE — secure token generation utility |
| `PasswordComplexityValidator` | Server (Backend) | CREATE — custom ASP.NET Core Identity password validator |
| `RateLimitingMiddleware` or policy | Server (Backend) | CREATE — per-email rate limiting for resend endpoint |

## Implementation Plan

1. **Create RegistrationRequest DTO** — Define model with properties: FirstName, LastName, Email, Phone, DateOfBirth, Password, AcceptedTerms. Add DataAnnotation attributes for server-side validation. Add custom FluentValidation or DataAnnotation for password complexity: 8+ chars, 1 uppercase, 1 number, 1 special char. Return 400 with specific missing criteria list on failure.

2. **Implement PasswordComplexityValidator** — Create custom `IPasswordValidator<ApplicationUser>` enforcing: minimum 8 characters, at least 1 uppercase letter, at least 1 digit, at least 1 special character. Register in ASP.NET Core Identity configuration. Return structured error response listing each failed criterion.

3. **Implement RegistrationService** — Core business logic:
   - Validate email uniqueness via `UserManager.FindByEmailAsync()`. On duplicate, return generic error "An account with this email already exists" regardless of verification status (AC-4).
   - Create user with `UserManager.CreateAsync()` — ASP.NET Core Identity uses bcrypt by default with 10+ rounds (FR-004, NFR-013).
   - Set account status to "pending verification" (custom claim or property).
   - Generate email verification token via `UserManager.GenerateEmailConfirmationTokenAsync()`.
   - Store token with 1-hour expiration timestamp.
   - Dispatch verification email via `IEmailService`.
   - Log registration event to audit trail (NFR-012).

4. **Implement email verification endpoint** — `POST /api/auth/verify-email` accepting token and email/userId.
   - Validate token via `UserManager.ConfirmEmailAsync()`.
   - If token valid: set account status to "active", return 200 with success message and login redirect URL (AC-2).
   - If token expired: return 410 Gone with "Link expired" message and resend URL (AC-3).
   - If token invalid: return 400 with "Invalid verification link".
   - Log verification event to audit trail.

5. **Implement resend verification endpoint** — `POST /api/auth/resend-verification` accepting email.
   - Rate limit: max 3 requests per 5 minutes per email address. Use Redis counter with 5-minute TTL or in-memory rate limiter.
   - If rate limit exceeded: return 429 Too Many Requests with retry-after header.
   - If account already verified: return 200 (no-op, do not reveal status).
   - Generate new token, invalidate old token, send new verification email.
   - Log resend event to audit trail.

6. **Implement email check endpoint** — `POST /api/auth/check-email` accepting email string.
   - Check if email exists in database (regardless of verification status).
   - Return `{ available: true/false }`. When false, display generic message (AC-4).
   - Rate limit to prevent enumeration attacks.

7. **Implement SmtpEmailService** — Send verification email using configured SMTP (SendGrid free tier or Gmail SMTP).
   - Email body contains verification link: `{baseUrl}/verify-email?token={token}&email={email}`.
   - Use HTML email template with UPACIP branding.
   - Log email dispatch status to NotificationLog (sent/failed).
   - Implement circuit breaker for SMTP failures (NFR-023) using Polly retry with exponential backoff (max 3 retries per NFR-032).

8. **Wire up AuthController** — Create controller with endpoints:
   - `POST /api/auth/register` — calls RegistrationService.RegisterAsync()
   - `POST /api/auth/verify-email` — calls RegistrationService.VerifyEmailAsync()
   - `POST /api/auth/resend-verification` — calls RegistrationService.ResendVerificationAsync()
   - `POST /api/auth/check-email` — calls RegistrationService.CheckEmailAvailabilityAsync()
   - All endpoints return structured JSON responses. Document via Swagger/Swashbuckle (NFR-038).

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
Server/                               # Backend .NET 8 solution
├── Controllers/                      # API controllers
│   └── AuthController.cs
├── DTOs/                             # Request/Response models
│   ├── RegistrationRequest.cs
│   └── RegistrationResponse.cs
├── Services/                         # Business logic
│   ├── IRegistrationService.cs
│   ├── RegistrationService.cs
│   ├── IEmailService.cs
│   └── SmtpEmailService.cs
├── Validators/                       # Custom validators
│   └── PasswordComplexityValidator.cs
├── Models/                           # Entity models
└── Program.cs                        # Service registration
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/AuthController.cs | Registration, verify-email, resend, check-email endpoints |
| CREATE | Server/DTOs/RegistrationRequest.cs | Request model with validation annotations |
| CREATE | Server/DTOs/RegistrationResponse.cs | Response model with status and message |
| CREATE | Server/Services/IRegistrationService.cs | Service interface for registration operations |
| CREATE | Server/Services/RegistrationService.cs | Business logic: register, verify, resend, check-email |
| CREATE | Server/Services/IEmailService.cs | Email sending interface |
| CREATE | Server/Services/SmtpEmailService.cs | SMTP implementation with Polly retry |
| CREATE | Server/Validators/PasswordComplexityValidator.cs | Custom ASP.NET Core Identity password validator |
| MODIFY | Server/Program.cs | Register RegistrationService, EmailService, PasswordValidator, rate limiting |

## External References

- [ASP.NET Core Identity — Account Confirmation & Password Recovery](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/accconfirm?view=aspnetcore-8.0)
- [ASP.NET Core Identity — Custom Password Validators](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-8.0#password)
- [Polly — Resilience Policies for .NET](https://github.com/App-vNext/Polly)
- [ASP.NET Core Rate Limiting Middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0)
- [SendGrid — SMTP Integration](https://docs.sendgrid.com/for-developers/sending-email/getting-started-smtp)
- [OWASP — Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)

## Build Commands

- `dotnet build` — Build solution
- `dotnet run --project Server` — Run backend
- `dotnet test` — Run tests

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] POST /api/auth/register creates user with bcrypt-hashed password (10+ rounds)
- [ ] Account created with "pending verification" status
- [ ] Verification email sent within 2 minutes of registration
- [ ] POST /api/auth/verify-email activates account with valid token
- [ ] POST /api/auth/verify-email returns 410 for expired token (1-hour expiry)
- [ ] POST /api/auth/verify-email returns 400 for invalid token
- [ ] POST /api/auth/resend-verification rate limited to 3 per 5 minutes per email
- [ ] POST /api/auth/resend-verification returns 429 when rate limit exceeded
- [ ] POST /api/auth/check-email returns availability without revealing verification status
- [ ] Duplicate email registration returns "An account with this email already exists"
- [ ] Password complexity validation returns specific missing criteria
- [ ] All authentication events logged to audit trail (NFR-012)
- [ ] SMTP failures handled with Polly retry + circuit breaker (NFR-023, NFR-032)
- [ ] Swagger documentation generated for all endpoints (NFR-038)
- [ ] Input sanitization prevents injection attacks (NFR-018)

## Implementation Checklist

- [x] Create `RegistrationRequest` DTO with DataAnnotation validation (required fields, email format, password complexity)
- [x] Implement `PasswordComplexityValidator` as custom `IPasswordValidator<ApplicationUser>` (8+ chars, 1 upper, 1 number, 1 special)
- [x] Implement `IRegistrationService` and `RegistrationService` (register, verify, resend, check-email)
- [x] Implement `IEmailService` and `SmtpEmailService` with Polly retry/circuit breaker for SMTP
- [x] Create `AuthController` with POST endpoints: /register, /verify-email, /resend-verification, /check-email
- [x] Configure rate limiting for resend endpoint (max 3 per 5 min per email) and check-email (anti-enumeration)
- [x] Register all services in Program.cs (DI container, Identity password validator, rate limiting policies)
- [x] Add audit logging for all registration and verification events (NFR-012)
