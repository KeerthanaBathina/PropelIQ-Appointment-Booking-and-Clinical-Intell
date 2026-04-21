# Task - TASK_002

## Requirement Reference

- User Story: US_015
- Story Location: .propel/context/tasks/EP-001/us_015/us_015.md
- Acceptance Criteria:
  - AC-1: Given the user enters their registered email, When the form is submitted, Then the system sends a password reset link within 2 minutes.
  - AC-2: Given the user clicks the reset link within 1 hour, When the token is valid, Then the system allows setting a new compliant password.
  - AC-3: Given the reset link has expired (1-hour window), When the user clicks it, Then the system returns "Link expired" and supports resend.
  - AC-4: Given the password is successfully reset, When the user tries to log in with the old password, Then the system rejects it and only accepts the new password.
- Edge Cases:
  - Non-registered email submitted: System returns 200 OK with same response as registered email — prevent email enumeration (OWASP guideline).
  - Multiple reset requests: Only the latest token is valid; previous tokens are invalidated on new request.

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
| Authentication | ASP.NET Core Identity | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Email Service | SMTP (SendGrid Free Tier / Gmail SMTP) | - |
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

Implement the backend API endpoints for the password reset flow using ASP.NET Core Identity on .NET 8. This includes: requesting a password reset (generates a secure token, invalidates prior tokens, sends an email with the reset link), validating the reset token (checks expiry and validity), executing the password reset (sets new hashed password, invalidates all refresh tokens/sessions), and audit logging of all password change events (FR-006). The API must prevent email enumeration by returning identical responses for registered and non-registered emails (OWASP Forgot Password guideline). Tokens have a 1-hour expiry window (FR-005).

## Dependent Tasks

- US_005 — Authentication scaffold (provides ASP.NET Core Identity, JWT/refresh token infrastructure, base controller patterns)
- task_003_db_password_reset_schema — Password reset token table must exist in the database
- US_012 task_002 — Reuse IEmailService (SmtpEmailService) for sending reset emails

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `PasswordResetController` | Server (Backend) | CREATE — forgot-password and reset-password endpoints |
| `ForgotPasswordRequest` DTO | Server (Backend) | CREATE — request model with email |
| `ResetPasswordRequest` DTO | Server (Backend) | CREATE — request model with token, email, newPassword |
| `IPasswordResetService` | Server (Backend) | CREATE — service interface |
| `PasswordResetService` | Server (Backend) | CREATE — business logic for token generation, validation, password update |
| `IEmailService` | Server (Backend) | REUSE — from US_012 (shared email sending interface) |
| `AuthController` (login) | Server (Backend) | VERIFY — old password rejected after reset (AC-4 inherently handled by Identity) |
| `Program.cs` | Server (Backend) | MODIFY — register PasswordResetService |

## Implementation Plan

1. **Create ForgotPasswordRequest DTO** — Model with single property: `Email` (string, required, email format validation via DataAnnotation `[EmailAddress]`). No other fields needed.

2. **Implement forgot-password endpoint** — `POST /api/auth/forgot-password` accepting `ForgotPasswordRequest`:
   - Lookup user by email via `UserManager.FindByEmailAsync()`.
   - **If user not found**: Return 200 OK with message "If an account exists with that email, a password reset link has been sent." (anti-enumeration — same response as found case). Do NOT send an email. Log the attempt without revealing the result.
   - **If user found**: Generate reset token via `UserManager.GeneratePasswordResetTokenAsync()`. This invalidates prior tokens automatically (ASP.NET Core Identity behavior handles the "only latest token valid" edge case). Store token hash with 1-hour expiry in `password_reset_tokens` table (task_003). Send email with reset link: `{baseUrl}/reset-password?token={urlEncodedToken}&email={urlEncodedEmail}`. Return 200 OK with same generic message.
   - Rate limit: max 5 requests per 15 minutes per IP (prevent abuse). Return 429 on exceeded.
   - Log forgot-password request to audit trail (FR-006: authentication event).

3. **Implement reset-password endpoint** — `POST /api/auth/reset-password` accepting `ResetPasswordRequest` (token, email, newPassword):
   - Validate request: email format, token not empty, new password meets complexity (8+ chars, 1 uppercase, 1 number, 1 special char via custom validator from US_012).
   - Lookup user by email. If not found, return 400 "Invalid reset request."
   - Validate token via `UserManager.ResetPasswordAsync(user, token, newPassword)`. This ASP.NET Core Identity method:
     - Verifies token validity and expiry.
     - Hashes the new password with bcrypt (10+ rounds per FR-004/NFR-013).
     - Updates the security stamp (invalidates ALL existing tokens, including refresh tokens — ensures AC-4: old password rejected).
   - **If token valid**: Return 200 with success message. Mark token as used in `password_reset_tokens` table. Invalidate all active sessions for this user in Redis (from US_014). Log password change to audit trail (FR-006).
   - **If token expired**: Return 410 Gone with "Reset link has expired. Please request a new one." (AC-3).
   - **If token invalid**: Return 400 with "Invalid reset link."
   - **If password complexity fails**: Return 422 with specific missing criteria list.

4. **Implement token invalidation on new request** — When generating a new reset token (step 2), mark all previous unused tokens in `password_reset_tokens` table as invalidated (`is_used = true` or `invalidated_at` timestamp). This ensures only the latest token is valid (edge case: multiple reset requests).

5. **Implement post-reset session cleanup** — After successful password reset:
   - Update the user's `SecurityStamp` (done automatically by `ResetPasswordAsync`).
   - Invalidate all active Redis sessions for this user via `ISessionService.InvalidateSessionAsync(userId)` (from US_014). This forces re-login on all devices.
   - Add all active JWT `jti` values for this user to the Redis blacklist.
   - Log the session invalidation as part of the password-change audit event.

6. **Send reset email via IEmailService** — Reuse `IEmailService` (SmtpEmailService from US_012). Email contents:
   - Subject: "Reset your UPACIP password"
   - Body: HTML template with UPACIP branding, reset link button, 1-hour expiry notice, "If you didn't request this, you can safely ignore this email."
   - Implement Polly retry with exponential backoff (max 3 retries) for SMTP failures (NFR-023, NFR-032).
   - Log email dispatch status to NotificationLog.

7. **Wire controller and register services** — Create `PasswordResetController` with:
   - `POST /api/auth/forgot-password` — calls `PasswordResetService.RequestResetAsync()`
   - `POST /api/auth/reset-password` — calls `PasswordResetService.ResetPasswordAsync()`
   - Register `IPasswordResetService` → `PasswordResetService` in Program.cs DI container.
   - Apply rate limiting policy to forgot-password endpoint.
   - Document via Swagger/Swashbuckle (NFR-038).

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
Server/                                # Backend .NET 8 solution
├── Controllers/                       # API controllers
│   └── PasswordResetController.cs     # NEW
├── DTOs/                              # Request/Response models
│   ├── ForgotPasswordRequest.cs       # NEW
│   └── ResetPasswordRequest.cs        # NEW
├── Services/                          # Business logic
│   ├── IPasswordResetService.cs       # NEW
│   ├── PasswordResetService.cs        # NEW
│   ├── IEmailService.cs               # REUSE from US_012
│   └── SmtpEmailService.cs            # REUSE from US_012
├── Models/                            # Entity models
└── Program.cs                         # Service registration
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/PasswordResetController.cs | POST /api/auth/forgot-password and POST /api/auth/reset-password |
| CREATE | Server/DTOs/ForgotPasswordRequest.cs | Email field with DataAnnotation validation |
| CREATE | Server/DTOs/ResetPasswordRequest.cs | Token, email, newPassword with validation |
| CREATE | Server/Services/IPasswordResetService.cs | Interface: RequestResetAsync, ResetPasswordAsync |
| CREATE | Server/Services/PasswordResetService.cs | Token generation, validation, password update, session cleanup |
| MODIFY | Server/Program.cs | Register PasswordResetService, rate limiting policy for forgot-password |

## External References

- [ASP.NET Core Identity — Password Reset](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/accconfirm?view=aspnetcore-8.0#password-recovery)
- [ASP.NET Core Identity — GeneratePasswordResetTokenAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.usermanager-1.generatepasswordresettokenasync)
- [ASP.NET Core Identity — ResetPasswordAsync](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.usermanager-1.resetpasswordasync)
- [OWASP — Forgot Password Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html)
- [ASP.NET Core Rate Limiting Middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-8.0)
- [Polly — Resilience Policies for .NET](https://github.com/App-vNext/Polly)

## Build Commands

- `dotnet build` — Build solution
- `dotnet run --project Server` — Run backend
- `dotnet test` — Run tests

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] POST /api/auth/forgot-password returns 200 for both registered and non-registered emails (anti-enumeration)
- [ ] POST /api/auth/forgot-password sends reset email for registered email within 2 minutes
- [ ] POST /api/auth/forgot-password does NOT send email for non-registered email
- [ ] Reset token has 1-hour expiry (FR-005)
- [ ] POST /api/auth/reset-password with valid token sets new bcrypt-hashed password (10+ rounds)
- [ ] POST /api/auth/reset-password with expired token returns 410 "Link expired" (AC-3)
- [ ] POST /api/auth/reset-password with invalid token returns 400
- [ ] POST /api/auth/reset-password with weak password returns 422 with specific criteria
- [ ] Old password rejected after successful reset (AC-4 — SecurityStamp change)
- [ ] All active sessions invalidated after password reset (Redis cleanup)
- [ ] Prior unused tokens invalidated when new token is generated
- [ ] Password change logged to immutable audit trail (FR-006)
- [ ] Rate limiting enforced on forgot-password (max 5 per 15 min per IP)
- [ ] SMTP failures handled with Polly retry + circuit breaker (NFR-023, NFR-032)
- [ ] Swagger documentation generated for all endpoints (NFR-038)
- [ ] Input sanitization on all endpoints (NFR-018)

## Implementation Checklist

- [X] Create `ForgotPasswordRequest` and `ResetPasswordRequest` DTOs with DataAnnotation validation
- [X] Implement `IPasswordResetService` and `PasswordResetService` (request reset, validate token, reset password, invalidate prior tokens)
- [X] Implement anti-enumeration: return identical 200 responses for registered and non-registered emails
- [X] Implement post-reset session cleanup: invalidate Redis sessions, blacklist JWTs, update SecurityStamp
- [X] Send reset email via `IEmailService` (reuse from US_012) with Polly retry for SMTP failures
- [X] Create `PasswordResetController` with POST endpoints: /forgot-password, /reset-password
- [X] Configure rate limiting for forgot-password endpoint (max 5 per 15 min per IP)
- [X] Add audit logging for all password reset events: request, success, failure, session cleanup (FR-006)
