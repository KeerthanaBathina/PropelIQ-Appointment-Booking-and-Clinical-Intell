# Task - TASK_001

## Requirement Reference

- User Story: US_065
- Story Location: .propel/context/tasks/EP-011/us_065/us_065.md
- Acceptance Criteria:
  - AC-1: Given a user is authenticated, When 15 minutes of inactivity pass, Then the session is automatically invalidated and the user is redirected to the login page.
  - AC-2: Given a user is logged in on Device A, When they attempt to log in on Device B, Then the session on Device A is terminated and the user is notified of concurrent session prevention.
  - AC-3: Given a user account exists, When 5 consecutive failed login attempts occur, Then the account is locked for 30 minutes and an audit log entry records the lockout event.
  - AC-4: Given a session timeout is approaching, When 2 minutes remain, Then a modal countdown with "Extend Session" button is displayed to the user — backend must support session time-remaining query and extension endpoint.
- Edge Cases:
  - Session extension request fails due to network issues: System invalidates the session on timeout; next page load redirects to login with "Session expired" message.
  - Admin account lockout: Same 5-attempt lockout applies; a separate admin recovery process (database-level unlock) is documented for emergencies.

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

Harden the session security layer by modifying the concurrent session behavior from "reject new login" (US_014 AC-3) to "terminate old session and allow new login" (US_065 AC-2), enforcing account lockout with HIPAA-compliant audit logging (AC-3), verifying session timeout invalidation (AC-1), and ensuring the session extension endpoint validates remaining time (AC-4). This task directly implements FR-094 (automatic session timeout after 15 minutes of inactivity), NFR-014 (session timeout enforcement), NFR-015 (concurrent session prevention), and NFR-016 (account lockout after 5 failed attempts for 30 minutes). It modifies the existing `ConcurrentSessionGuard`, `RedisSessionService`, and `AuthController` from US_014/task_002, and adds lockout audit logging to the authentication pipeline. The behavioral change for concurrent sessions aligns with NFR-015's intent to prevent session hijacking by ensuring the latest authenticated session is authoritative.

## Dependent Tasks

- US_005 / task_001_be_identity_configuration — ASP.NET Core Identity with lockout configuration (5 attempts / 30 minutes)
- US_005 / task_002_be_jwt_token_service — JWT token generation, validation, and refresh token flow
- US_014 / task_002_be_session_management — RedisSessionService, ConcurrentSessionGuard, SessionManagementMiddleware, SessionController
- US_014 / task_003_db_session_schema — Redis key structure, UserSession entity, JWT blacklist keys
- US_065 / task_003_db_lockout_recovery_schema — Lockout audit event type and admin recovery procedure

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `ConcurrentSessionGuard` | Server (Backend) | MODIFY — change from reject-new to terminate-old-allow-new behavior |
| `RedisSessionService` | Server (Backend) | MODIFY — add `TerminateAndReplaceSessionAsync` method for atomic old-session eviction |
| `AuthController` (login) | Server (Backend) | MODIFY — integrate updated concurrent session flow, add lockout audit logging |
| `AccountLockoutAuditHandler` | Server (Backend) | CREATE — handler logging lockout events to AuditLog table |
| `SessionController` | Server (Backend) | MODIFY — add `GET /api/session/time-remaining` endpoint for frontend countdown |
| `ISessionService` | Server (Backend) | MODIFY — add `TerminateAndReplaceSessionAsync` and `GetTimeRemainingAsync` method signatures |
| `Program.cs` | Server (Backend) | MODIFY — register `AccountLockoutAuditHandler` in DI |

## Implementation Plan

1. **Modify ConcurrentSessionGuard — Terminate-Old Behavior**: Change the concurrent session logic in `ConcurrentSessionGuard` from returning 409 Conflict to the following flow:
   - On login attempt, check if an active session exists for the user in Redis via `IsSessionActiveAsync(userId)`.
   - If an active session exists AND is not expired:
     - Call `RedisSessionService.TerminateAndReplaceSessionAsync(userId, newSessionId, ip, userAgent)` which atomically: (a) reads the old session details (device info, IP), (b) deletes the old Redis session key, (c) adds the old JWT `jti` to the Redis blacklist, (d) creates the new session key with 15-minute TTL, (e) records the old session termination in the `UserSession` PostgreSQL table with `ExpirationReason = ConcurrentSessionReplacement`.
     - Store a notification flag in Redis: `session_terminated:{oldSessionId}` with TTL of 5 minutes, containing a JSON payload `{ "reason": "concurrent_login", "newDeviceIp": "{ip}", "terminatedAt": "{utc_timestamp}" }`. This allows the frontend on Device A to detect termination on its next API call.
     - Log the concurrent session replacement event to audit trail: action = `session_replaced`, including both old and new session details.
   - If no active session exists: proceed with normal session creation (no change from US_014).

2. **Implement TerminateAndReplaceSessionAsync in RedisSessionService**: Add a new method to `ISessionService` and `RedisSessionService`:
   - Signature: `Task<SessionTerminationResult> TerminateAndReplaceSessionAsync(Guid userId, Guid newSessionId, string ipAddress, string userAgent)`
   - Use a Redis Lua script (or `MULTI/EXEC` transaction) to atomically: `DEL session:{userId}` → `HSET session:{userId} ...newFields` → `EXPIRE session:{userId} 900` → `SET blacklist:{oldJti} "1" EX {remainingTtl}` → `SET session_terminated:{oldSessionId} "{json}" EX 300`.
   - Return `SessionTerminationResult` with: `OldSessionId`, `OldIpAddress`, `OldUserAgent`, `WasTerminated` (bool).
   - Handle edge case: if old session expired between check and terminate, proceed with normal creation (idempotent).

3. **Implement GetTimeRemainingAsync in RedisSessionService**: Add method to query Redis TTL for the user's session key:
   - Signature: `Task<int?> GetTimeRemainingAsync(Guid userId)`
   - Use Redis `TTL session:{userId}` command. Return remaining seconds or null if no active session.
   - This supports the frontend countdown modal (AC-4).

4. **Add GET /api/session/time-remaining endpoint**: Add to `SessionController`:
   - Route: `GET /api/session/time-remaining`
   - Requires authentication (valid JWT).
   - Calls `GetTimeRemainingAsync(userId)` and returns `{ "remainingSeconds": N, "warningThresholdSeconds": 120 }`.
   - Returns 401 if no active session (session already expired).
   - Rate limit: max 1 request per 10 seconds per user (prevent polling abuse).

5. **Implement AccountLockoutAuditHandler**: Create a service that hooks into ASP.NET Core Identity's lockout events:
   - After each failed login attempt, check `UserManager.GetAccessFailedCountAsync(user)`.
   - When the failed count reaches 5 (and Identity locks the account), create an `AuditLog` entry:
     - `action = "account_lockout"`, `resource_type = "User"`, `resource_id = user.Id`, `ip_address`, `user_agent`.
     - Include in metadata: `failedAttemptCount = 5`, `lockoutEndUtc = account_locked_until`.
   - Also log each individual failed login attempt: `action = "login_failed"`, with `failedAttemptCount` incremented.
   - Use `IAuditService` (from US_014 audit infrastructure) to persist entries.
   - Ensure admin accounts receive identical lockout treatment (edge case — no special admin bypass).

6. **Modify AuthController Login Flow**: Update the `Login` action to integrate:
   - Check account lockout status FIRST via `UserManager.IsLockedOutAsync(user)`. If locked, return 423 Locked with `{ "message": "Account locked due to too many failed attempts. Try again after {minutes} minutes.", "lockoutEnd": "{utc_timestamp}" }`.
   - On failed password validation, call `UserManager.AccessFailedAsync(user)` to increment failed count (Identity handles lockout threshold internally).
   - On lockout trigger, call `AccountLockoutAuditHandler.LogLockoutAsync(user, ip, userAgent)`.
   - On successful login, call `UserManager.ResetAccessFailedCountAsync(user)` to clear failed attempts.
   - Integrate updated `ConcurrentSessionGuard` (step 1) for concurrent session handling.

7. **Modify SessionManagementMiddleware — Termination Detection**: Add logic to the existing middleware to check for the `session_terminated:{sessionId}` Redis key on each authenticated request:
   - Extract `sessionId` from JWT claims.
   - Check `EXISTS session_terminated:{sessionId}` in Redis.
   - If the termination key exists: return 440 (Login Timeout / custom status) with `{ "code": "SESSION_TERMINATED", "reason": "concurrent_login", "message": "Your session was terminated because your account logged in from another device." }`. Delete the termination key after reading (one-time notification).
   - If the termination key does not exist: proceed with normal activity tracking (no change from US_014).

8. **Register AccountLockoutAuditHandler in Program.cs**: Add to DI container:
   - `services.AddScoped<IAccountLockoutAuditHandler, AccountLockoutAuditHandler>()`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Security/
│   │   │   └── BcryptPasswordHasher.cs
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlerMiddleware.cs
│   │   │   ├── CorrelationIdMiddleware.cs
│   │   │   └── SessionManagementMiddleware.cs
│   │   └── Controllers/
│   │       ├── AuthController.cs
│   │       └── SessionController.cs
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Auth/
│   │   │   ├── ISessionService.cs
│   │   │   ├── RedisSessionService.cs
│   │   │   ├── ConcurrentSessionGuard.cs
│   │   │   └── JwtTokenService.cs
│   │   ├── Audit/
│   │   │   └── IAuditService.cs
│   │   └── Caching/
│   │       ├── ICacheService.cs
│   │       └── RedisCacheService.cs
│   └── UPACIP.DataAccess/
│       ├── UPACIP.DataAccess.csproj
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── ApplicationUser.cs
│       │   ├── ApplicationRole.cs
│       │   ├── UserSession.cs
│       │   └── AuditLog.cs
│       └── Migrations/
└── scripts/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.Service/Auth/ISessionService.cs | Add `TerminateAndReplaceSessionAsync` and `GetTimeRemainingAsync` method signatures |
| MODIFY | src/UPACIP.Service/Auth/RedisSessionService.cs | Implement `TerminateAndReplaceSessionAsync` (atomic Lua script for old session eviction + new session creation + blacklist + termination flag) and `GetTimeRemainingAsync` (Redis TTL query) |
| MODIFY | src/UPACIP.Service/Auth/ConcurrentSessionGuard.cs | Change from reject-new (409) to terminate-old-allow-new behavior calling `TerminateAndReplaceSessionAsync` |
| CREATE | src/UPACIP.Service/Auth/IAccountLockoutAuditHandler.cs | Interface for lockout audit logging |
| CREATE | src/UPACIP.Service/Auth/AccountLockoutAuditHandler.cs | Implementation logging lockout and failed login events to AuditLog via IAuditService |
| CREATE | src/UPACIP.Service/Auth/SessionTerminationResult.cs | DTO containing old session details (OldSessionId, OldIpAddress, WasTerminated) |
| MODIFY | src/UPACIP.Api/Controllers/AuthController.cs | Add lockout check (423 response), integrate lockout audit handler, use updated ConcurrentSessionGuard, reset failed count on success |
| MODIFY | src/UPACIP.Api/Controllers/SessionController.cs | Add `GET /api/session/time-remaining` endpoint with rate limiting |
| MODIFY | src/UPACIP.Api/Middleware/SessionManagementMiddleware.cs | Add termination detection via `session_terminated:{sessionId}` Redis key check, return 440 with termination reason |
| MODIFY | src/UPACIP.Api/Program.cs | Register `IAccountLockoutAuditHandler` in DI container |

## External References

- [ASP.NET Core Identity Account Lockout](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-8.0#lockout)
- [Redis Lua Scripting — Atomic Multi-Key Operations](https://redis.io/docs/latest/develop/interact/programmability/eval-intro/)
- [StackExchange.Redis — Lua Script Execution](https://stackexchange.github.io/StackExchange.Redis/Scripting.html)
- [OWASP — Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)
- [OWASP — Authentication Cheat Sheet (Account Lockout)](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html#account-lockout)
- [ASP.NET Core — Custom HTTP Status Codes in Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-8.0)
- [HIPAA Security Rule — Access Control (§164.312(a))](https://www.hhs.gov/hipaa/for-professionals/security/laws-regulations/index.html)

## Build Commands

```powershell
# Restore and build
dotnet restore UPACIP.sln
dotnet build UPACIP.sln --configuration Debug

# Run backend
dotnet run --project src/UPACIP.Api

# Run tests
dotnet test UPACIP.sln
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Login on Device B terminates session on Device A (Redis key deleted, JWT blacklisted) (AC-2)
- [ ] Device A receives 440 response with `SESSION_TERMINATED` code on next API call after concurrent login (AC-2)
- [ ] `session_terminated:{sessionId}` Redis key created with 5-minute TTL on concurrent login
- [ ] Audit log records `session_replaced` action with both old and new session details
- [ ] 5 consecutive failed logins lock account for 30 minutes (AC-3)
- [ ] Locked account returns 423 with lockout end timestamp
- [ ] Audit log records `account_lockout` action with failed attempt count and lockout end time (AC-3)
- [ ] Each failed login attempt creates `login_failed` audit entry with incrementing count
- [ ] Successful login resets failed attempt counter to 0
- [ ] Admin accounts receive identical lockout treatment (no bypass)
- [ ] Session auto-invalidates after 15 minutes of inactivity (AC-1, verified via Redis TTL expiry)
- [ ] `GET /api/session/time-remaining` returns remaining seconds (AC-4)
- [ ] `GET /api/session/time-remaining` returns 401 if session expired
- [ ] Rate limiting prevents more than 1 time-remaining request per 10 seconds per user
- [ ] Lua script executes atomically (no partial state on Redis failure)
- [ ] Input sanitization on all endpoints (NFR-018)
- [ ] No PII in application logs (NFR-017)

## Implementation Checklist

- [ ] Modify `ISessionService` to add `TerminateAndReplaceSessionAsync` and `GetTimeRemainingAsync` signatures
- [ ] Implement `TerminateAndReplaceSessionAsync` in `RedisSessionService` with atomic Lua script (DEL old → HSET new → EXPIRE → blacklist old jti → SET termination flag)
- [ ] Implement `GetTimeRemainingAsync` in `RedisSessionService` using Redis TTL command
- [ ] Modify `ConcurrentSessionGuard` to terminate old session and allow new login instead of rejecting with 409
- [ ] Create `AccountLockoutAuditHandler` logging lockout and failed login events to AuditLog table via IAuditService
- [ ] Modify `AuthController.Login` to check lockout (423), integrate lockout audit, use updated concurrent session guard, and reset failed count on success
- [ ] Add `GET /api/session/time-remaining` to `SessionController` with rate limiting
- [ ] Modify `SessionManagementMiddleware` to detect `session_terminated:{sessionId}` key and return 440 with termination reason
