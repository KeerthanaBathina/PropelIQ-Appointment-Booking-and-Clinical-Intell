# Task - TASK_002

## Requirement Reference

- User Story: US_016
- Story Location: .propel/context/tasks/EP-001/us_016/us_016.md
- Acceptance Criteria:
  - AC-1: Given MFA is enabled for a staff user, When they log in with valid credentials, Then the system prompts for a TOTP code before granting access — BE must validate credentials first, then require TOTP verification before issuing JWT.
  - AC-2: Given a user fails 5 consecutive login attempts, Then the account is locked for 30 minutes — BE must track failed attempts, enforce lockout, and return 423 with lockout expiry.
  - AC-3: Given the lockout period has passed, When the user attempts to log in with correct credentials, Then login succeeds — BE must auto-unlock after 30 minutes.
  - AC-4: Given a user successfully logs in, When the system processes the request, Then it records and returns "Last login: [timestamp] from [IP address]." — BE must update last_login_at and return previous login data.
  - AC-5: Given any authentication event occurs, Then the system creates an immutable audit log entry — BE must log login, logout, failed attempt, lockout, MFA events.
- Edge Cases:
  - Login during lockout: reject with 423 and same lockedUntil without resetting lockout timer.
  - MFA device lost: admin endpoint to reset MFA for a user, itself logged in audit trail.
  - Concurrent login attempts during lockout verification: use atomic increment for failed-attempt counter.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | ASP.NET Core Web API | .NET 8 |
| Authentication | ASP.NET Core Identity + JWT | .NET 8 |
| ORM | Entity Framework Core | 8.x |
| Cache | Upstash Redis | 7.x |
| Resilience | Polly | 8.x |
| Logging | Serilog + Seq | latest |
| Language | C# | 12 |
| Frontend | N/A | - |
| Database | N/A (consumed via EF Core) | - |
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

Implement backend services for three security features: **(1) MFA TOTP**: Generate and store encrypted TOTP secrets per user, provide setup/verify-setup/verify-login endpoints, issue JWT only after both credential AND TOTP validation succeed, support backup codes for recovery. **(2) Account Lockout**: Track consecutive failed login attempts per user (atomic counter in Redis + persisted to DB), lock account for 30 minutes after 5 failures, auto-unlock after expiry, reject login during lockout with 423 status and remaining time. **(3) Login Tracking & Audit Logging**: Update last_login_at and IP on every successful authentication, return previous login data in response, create immutable audit log entries for all authentication events (login, logout, failed attempt, lockout triggered, MFA enabled/disabled/verified, admin MFA reset).

## Dependent Tasks

- US_005 — Authentication scaffold (provides AuthController, JWT service, Identity configuration)
- US_013 task_002 — RBAC authorization (provides role-based policy checks for admin-only MFA reset endpoint)
- task_003_db_mfa_lockout_audit — Database schema for audit_logs table, MFA fields, lockout fields; BE can develop in parallel with in-memory testing

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `IMfaService` / `MfaService` | Server (Backend) | CREATE — TOTP secret generation, QR code URL, code verification, backup code management |
| `IAccountLockoutService` / `AccountLockoutService` | Server (Backend) | CREATE — failed attempt tracking, lockout enforcement, auto-unlock logic |
| `IAuditLogService` / `AuditLogService` | Server (Backend) | CREATE — immutable audit log creation for auth events |
| `AuthController` | Server (Backend) | MODIFY — add MFA verify, MFA setup, MFA verify-setup, admin MFA reset endpoints |
| `LoginHandler` (or existing login logic) | Server (Backend) | MODIFY — integrate lockout check → credential validation → MFA check → JWT issue pipeline |
| `MfaSetupDto` / `MfaVerifyDto` / `AuditLogDto` | Server (Backend) | CREATE — request/response DTOs |
| `AuditLog` entity | Server (Backend) | CREATE — EF Core entity mapping to audit_logs table |
| `User` / `ApplicationUser` entity | Server (Backend) | MODIFY — add TotpSecretEncrypted, MfaRecoveryCodes, MfaEnabled, FailedLoginAttempts, AccountLockedUntil, LastLoginAt, LastLoginIp |

## Implementation Plan

1. **Add MFA fields to ApplicationUser and create AuditLog entity** — Extend `ApplicationUser` with properties: `TotpSecretEncrypted` (string, nullable — AES-256 encrypted), `MfaRecoveryCodes` (string, nullable — comma-separated hashed codes), `MfaEnabled` (bool, default false), `FailedLoginAttempts` (int, default 0), `AccountLockedUntil` (DateTimeOffset?, nullable), `LastLoginAt` (DateTimeOffset?, nullable), `LastLoginIp` (string, nullable). Create `AuditLog` entity with: `LogId` (Guid, PK), `UserId` (Guid?, FK to User), `Action` (string/enum — Login, Logout, FailedLogin, AccountLocked, MfaEnabled, MfaDisabled, MfaVerified, AdminMfaReset, PasswordChanged), `ResourceType` (string), `ResourceId` (string), `Timestamp` (DateTimeOffset, set once, immutable), `IpAddress` (string), `UserAgent` (string). Configure EF mappings: AuditLog has no update — only insert (enforced at repository level). Index on UserId + Timestamp for audit queries. Use `HasConversion` for Action enum.

2. **Implement IMfaService** — Methods: `GenerateSecretAsync(userId)` → generates TOTP secret (using OtpNet/TwoFactorAuth library), encrypts with AES-256 (key from IConfiguration), returns `otpAuthUrl` for QR code and `manualEntryKey`. `VerifyCodeAsync(userId, code)` → decrypts stored secret, validates TOTP code with ±1 time-step tolerance (RFC 6238, 30-second window). `GenerateBackupCodesAsync(userId)` → generates 8 random 8-char alphanumeric codes, stores bcrypt-hashed versions. `VerifyBackupCodeAsync(userId, code)` → checks against stored hashes, marks used code as consumed (one-time use). `DisableMfaAsync(userId)` → clears secret, backup codes, sets MfaEnabled=false. `IsEnabledAsync(userId)` → returns MfaEnabled flag. Store TOTP secret encrypted at rest (OWASP requirement — never plaintext).

3. **Implement IAccountLockoutService** — Methods: `RecordFailedAttemptAsync(userId)` → atomic increment of FailedLoginAttempts (use Redis `INCR session:lockout:{userId}` with 30-min TTL for speed, persist to DB). If count >= 5: set `AccountLockedUntil = DateTimeOffset.UtcNow.AddMinutes(30)`, log audit event (AccountLocked). Return `{ isLocked: true, lockedUntil, remainingAttempts: 0 }`. `CheckLockoutAsync(userId)` → if `AccountLockedUntil > DateTimeOffset.UtcNow`, return locked status + remaining time. If expired, auto-clear lockout fields + Redis counter. `ResetOnSuccessAsync(userId)` → set FailedLoginAttempts = 0, clear AccountLockedUntil, clear Redis counter. Edge case: login attempt during lockout → return 423 with same lockedUntil (no timer reset, no counter increment).

4. **Implement IAuditLogService** — `LogAsync(AuditLogEntry)` → insert-only to audit_logs table via EF Core. No update or delete methods exposed. Fields auto-populated: `LogId = Guid.NewGuid()`, `Timestamp = DateTimeOffset.UtcNow` (immutable). `IpAddress` extracted from `HttpContext.Connection.RemoteIpAddress` (handle X-Forwarded-For behind reverse proxy). `UserAgent` from request headers. Log all auth events: Login (success), FailedLogin (with remaining attempts), AccountLocked, Logout, MfaEnabled, MfaDisabled, MfaVerified, AdminMfaReset, PasswordChanged, PasswordReset. Correlation ID from Serilog scope for cross-event tracing.

5. **Modify login endpoint pipeline** — Refactor the login flow into a sequential pipeline: **(a)** Check lockout status → 423 if locked. **(b)** Validate credentials (email + password) → 401 if invalid, increment failed attempts. **(c)** Check MFA enabled → if yes, return 200 with `{ mfaRequired: true, mfaToken: <short-lived-token> }` (mfaToken is JWT with 5-min expiry, no role claims, purpose="mfa"). **(d)** If MFA not enabled, issue full JWT + refresh token → 200 with tokens + `{ lastLogin: { timestamp, ipAddress } }`. **(e)** On success: reset lockout counters, update last_login_at/last_login_ip (capture PREVIOUS values for response), log Login audit event. Response codes: 200 (success or MFA required), 401 (invalid credentials), 423 (locked).

6. **Add MFA endpoints to AuthController** — `POST /api/auth/mfa/verify` — accepts `{ mfaToken, code }`. Validate mfaToken (5-min expiry, purpose=mfa). Verify TOTP code. Issue full JWT + refresh token on success. Log MfaVerified audit. `POST /api/auth/mfa/setup` (authenticated, staff/admin only) — generate secret, return otpAuthUrl + manualEntryKey. `POST /api/auth/mfa/verify-setup` — verify code against newly generated secret, enable MFA, return backup codes. Log MfaEnabled audit. `POST /api/auth/mfa/disable` (authenticated) — require password confirmation, disable MFA. Log MfaDisabled audit. `POST /api/admin/mfa/reset/{userId}` (admin only, policy="AdminOnly") — disable MFA for specified user. Log AdminMfaReset audit with target userId.

7. **Register services in DI and add middleware** — Register `IMfaService`, `IAccountLockoutService`, `IAuditLogService` as scoped services. Add IP extraction utility (handles X-Forwarded-For). Ensure Serilog enrichment includes UserId correlation for audit log queries. Add rate limiting on MFA verify endpoint (max 5 attempts per mfaToken to prevent brute force).

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
Server/                       # Backend ASP.NET Core Web API
├── Controllers/
│   └── AuthController.cs     # (existing from US_005) — MODIFY
├── Services/
│   ├── IMfaService.cs        # CREATE
│   ├── MfaService.cs         # CREATE
│   ├── IAccountLockoutService.cs   # CREATE
│   ├── AccountLockoutService.cs    # CREATE
│   ├── IAuditLogService.cs   # CREATE
│   └── AuditLogService.cs    # CREATE
├── Models/
│   ├── ApplicationUser.cs    # (existing from US_005) — MODIFY
│   ├── AuditLog.cs           # CREATE
│   └── DTOs/
│       ├── MfaSetupDto.cs    # CREATE
│       └── MfaVerifyDto.cs   # CREATE
├── Data/
│   └── ApplicationDbContext.cs  # MODIFY — add AuditLog DbSet
└── Program.cs                # MODIFY — register new services
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IMfaService.cs | Interface: GenerateSecret, VerifyCode, GenerateBackupCodes, VerifyBackupCode, Disable, IsEnabled |
| CREATE | Server/Services/MfaService.cs | TOTP generation (OtpNet), AES-256 encryption, backup code hashing |
| CREATE | Server/Services/IAccountLockoutService.cs | Interface: RecordFailedAttempt, CheckLockout, ResetOnSuccess |
| CREATE | Server/Services/AccountLockoutService.cs | Redis atomic counter, 5-attempt/30-min lock, auto-unlock |
| CREATE | Server/Services/IAuditLogService.cs | Interface: LogAsync (insert-only) |
| CREATE | Server/Services/AuditLogService.cs | EF Core insert-only audit log with IP/UserAgent enrichment |
| CREATE | Server/Models/AuditLog.cs | Entity: LogId, UserId, Action, ResourceType, ResourceId, Timestamp, IpAddress, UserAgent |
| CREATE | Server/Models/DTOs/MfaSetupDto.cs | Response: OtpAuthUrl, ManualEntryKey |
| CREATE | Server/Models/DTOs/MfaVerifyDto.cs | Request: MfaToken, Code |
| MODIFY | Server/Models/ApplicationUser.cs | Add TotpSecretEncrypted, MfaRecoveryCodes, MfaEnabled, FailedLoginAttempts, AccountLockedUntil, LastLoginAt, LastLoginIp |
| MODIFY | Server/Controllers/AuthController.cs | Add mfa/verify, mfa/setup, mfa/verify-setup, mfa/disable, admin/mfa/reset endpoints |
| MODIFY | Server/Data/ApplicationDbContext.cs | Add DbSet<AuditLog>, configure immutable Timestamp, indexes |
| MODIFY | Server/Program.cs | Register IMfaService, IAccountLockoutService, IAuditLogService |

## External References

- [OtpNet — .NET TOTP Library](https://github.com/kspearrin/Otp.NET)
- [RFC 6238 — TOTP Algorithm](https://datatracker.ietf.org/doc/html/rfc6238)
- [OWASP — Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)
- [OWASP — MFA Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Multifactor_Authentication_Cheat_Sheet.html)
- [ASP.NET Core Identity — Account Lockout](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration#lockout)
- [AES-256 Encryption in .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes)

## Build Commands

- `dotnet build` — Compile solution
- `dotnet test` — Run unit and integration tests
- `dotnet ef migrations add MfaLockoutAudit` — Generate EF Core migration
- `dotnet ef database update` — Apply migration

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass
- [ ] TOTP secret generated and encrypted at rest (not stored in plaintext)
- [ ] TOTP verification succeeds with valid code within ±1 time-step tolerance
- [ ] TOTP verification fails with invalid/expired code
- [ ] Backup codes: 8 generated, each usable exactly once
- [ ] Account locks after 5 failed attempts with 423 response + lockedUntil (AC-2)
- [ ] Login during lockout returns 423 with same lockedUntil — timer NOT reset
- [ ] Account auto-unlocks after 30 minutes (AC-3)
- [ ] Successful login resets failed attempt counter
- [ ] Last login timestamp and IP recorded and returned in login response (AC-4)
- [ ] Audit log entries created for: Login, FailedLogin, AccountLocked, MfaEnabled, MfaDisabled, MfaVerified, AdminMfaReset
- [ ] Audit logs are immutable — no update/delete operations
- [ ] MFA verify endpoint rate-limited (5 attempts per mfaToken)
- [ ] Admin MFA reset requires AdminOnly policy
- [ ] X-Forwarded-For handled for IP extraction behind reverse proxy

## Implementation Checklist

- [X] Extend ApplicationUser with MFA fields (TotpSecretEncrypted, MfaRecoveryCodes, MfaEnabled) and lockout/login fields (FailedLoginAttempts, AccountLockedUntil, LastLoginAt, LastLoginIp)
- [X] Create AuditLog entity with insert-only EF Core configuration and indexes on UserId + Timestamp
- [X] Implement MfaService with AES-256 encrypted TOTP secret storage, code verification (±1 step), and bcrypt-hashed backup codes
- [X] Implement AccountLockoutService with Redis atomic counter, 5-attempt threshold, 30-minute lock, and auto-unlock on expiry
- [X] Implement AuditLogService with insert-only logging, IP/UserAgent extraction, and Serilog correlation
- [X] Refactor login endpoint into lockout-check → credential-validation → MFA-check → JWT-issue pipeline
- [X] Add MFA endpoints: verify (login), setup, verify-setup, disable, and admin reset with appropriate authorization policies
- [X] Register all services in DI and add rate limiting on MFA verify endpoint
