# Task - TASK_002

## Requirement Reference

- User Story: US_014
- Story Location: .propel/context/tasks/EP-001/us_014/us_014.md
- Acceptance Criteria:
  - AC-1: Given I am logged in, When I am inactive for 15 minutes, Then the system invalidates my session and redirects to login with message "Session expired due to inactivity."
  - AC-2: Given I am actively using the system, When I make a request within the 15-minute window, Then the session timer resets and I remain authenticated.
  - AC-3: Given I am logged in on one device, When I attempt to log in on a second device, Then the system rejects the second login with "Another active session exists. Please logout first."
  - AC-4: Given a session expiry warning is configured, When 2 minutes remain before expiry, Then a modal countdown appears — backend must support session extend endpoint.
- Edge Cases:
  - Server restarts mid-session: Redis-backed session storage preserves session state across server restarts.
  - Simultaneous requests from the same session: last-activity timestamp updates atomically using Redis atomic SET operations; no race condition on session extension.

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

Implement server-side session management using Redis-backed session storage with JWT + refresh tokens on .NET 8. This task covers: tracking active sessions per user in Redis with a 15-minute sliding TTL (NFR-014), updating the last-activity timestamp atomically on each authenticated request via middleware (AC-2), rejecting concurrent login attempts when an active session exists (AC-3/NFR-015/FR-007), providing a session extend endpoint for the frontend warning modal (AC-4), invalidating expired sessions and adding JWT tokens to a blacklist in Redis (AC-1), and logging all session lifecycle events to the audit trail (NFR-012). Redis ensures session state survives server restarts (edge case).

## Dependent Tasks

- US_005 — Authentication scaffold (provides JWT token generation/validation, refresh token flow, ASP.NET Core Identity setup)
- task_003_db_session_schema — Redis key structure and UserSession DB table must be defined

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `SessionManagementMiddleware` | Server (Backend) | CREATE — middleware tracking activity and enforcing session timeout |
| `ISessionService` | Server (Backend) | CREATE — session service interface |
| `RedisSessionService` | Server (Backend) | CREATE — Redis-backed implementation of session tracking |
| `SessionController` | Server (Backend) | CREATE — extend-session and session-status endpoints |
| `ConcurrentSessionGuard` | Server (Backend) | CREATE — login-time check rejecting concurrent sessions |
| `JwtTokenService` | Server (Backend) | MODIFY — integrate token blacklist on session invalidation |
| `AuthController` (login) | Server (Backend) | MODIFY — register session on login, check for concurrent sessions |
| `Program.cs` | Server (Backend) | MODIFY — register session middleware, Redis connection, services |

## Implementation Plan

1. **Implement RedisSessionService** — Create a service managing active sessions in Redis. Key structure:
   - `session:{userId}` → Hash with fields: `sessionId` (UUID), `lastActivity` (UTC timestamp), `loginAt` (UTC timestamp), `ipAddress`, `userAgent`.
   - TTL: 15 minutes (900 seconds), sliding window (reset on each activity update).
   - Use Redis `HSET` + `EXPIRE` for atomic set-with-TTL operations (edge case: no race condition).
   - Methods: `CreateSessionAsync(userId, sessionId, ip, userAgent)`, `UpdateActivityAsync(userId)`, `GetSessionAsync(userId)`, `InvalidateSessionAsync(userId)`, `IsSessionActiveAsync(userId)`.
   - Redis connection uses `StackExchange.Redis` configured with Upstash Redis connection string from `appsettings.json`.

2. **Implement ConcurrentSessionGuard** — On login attempt, check if an active session exists for the user in Redis via `IsSessionActiveAsync(userId)`. If an active session exists AND the session is not expired:
   - Return 409 Conflict with message: "Another active session exists. Please logout first." (AC-3, FR-007, NFR-015).
   - Log the rejected login attempt to the audit trail with both the existing session details and the new attempt details.
   - Do NOT invalidate the existing session — the user must explicitly logout first.
   If no active session exists OR the existing session TTL has expired, allow login to proceed. Integrate this check into the `AuthController.Login()` method before issuing new tokens.

3. **Implement SessionManagementMiddleware** — ASP.NET Core middleware that runs on every authenticated request:
   - Extract `userId` from the JWT claims.
   - Call `RedisSessionService.UpdateActivityAsync(userId)` to reset the Redis key TTL to 15 minutes (AC-2: timer resets on activity).
   - This update is atomic via Redis `EXPIRE` command — handles simultaneous requests without race conditions (edge case).
   - Skip middleware for unauthenticated endpoints (login, register, public routes).
   - If Redis is unavailable, log error and allow request to proceed (circuit breaker — graceful degradation per NFR-023).

4. **Implement session extend endpoint** — `POST /api/session/extend`:
   - Requires authentication (valid JWT).
   - Calls `RedisSessionService.UpdateActivityAsync(userId)` to reset the 15-minute TTL.
   - Issues a new JWT access token with refreshed expiry (15 minutes) via `JwtTokenService`.
   - Returns 200 with new token and `expiresAt` timestamp.
   - Returns 401 if session already expired in Redis (user must re-login).
   - Log session extension event to audit trail.

5. **Implement session invalidation on timeout** — When the Redis key expires (TTL reached 0), the session is implicitly invalidated:
   - Redis key auto-deletes on TTL expiry — no background job needed.
   - On the next client request with expired session, the middleware checks Redis, finds no active session, and returns 401 Unauthorized.
   - Add the expired JWT `jti` (JWT ID) to a Redis blacklist set `blacklist:{jti}` with TTL matching the JWT's remaining lifetime. This prevents reuse of the expired JWT before its signature-based expiry.
   - JWT validation middleware checks the blacklist before allowing request: if `jti` is in blacklist, return 401.

6. **Implement explicit logout with session cleanup** — Modify `POST /api/auth/logout`:
   - Call `RedisSessionService.InvalidateSessionAsync(userId)` to delete the Redis session key.
   - Add the current JWT `jti` to the Redis blacklist.
   - Clear the refresh token (mark as revoked in DB or delete from Redis).
   - Log logout event to audit trail (NFR-012, FR-006).
   - Return 200 with Set-Cookie to clear HttpOnly cookie.

7. **Register session on login** — Modify `AuthController.Login()`:
   - After successful authentication, call `ConcurrentSessionGuard` to check for existing session (step 2).
   - If allowed, call `RedisSessionService.CreateSessionAsync(userId, newSessionId, ip, userAgent)` to create the Redis session with 15-minute TTL.
   - Include `sessionId` as a claim in the JWT for session correlation.
   - Log login event with session details to audit trail.

8. **Wire middleware and services in Program.cs** — Register in DI container:
   - `IConnectionMultiplexer` (StackExchange.Redis) from Upstash Redis connection string.
   - `ISessionService` → `RedisSessionService` (scoped).
   - `SessionManagementMiddleware` in the pipeline after authentication but before authorization.
   - Configure Redis circuit breaker with Polly (open after 5 failures, retry after 30 seconds per NFR-023).

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
Server/                                # Backend .NET 8 solution
├── Controllers/                       # API controllers
│   ├── AuthController.cs              # Login/logout (to be modified)
│   └── SessionController.cs           # NEW - session extend endpoint
├── Middleware/                         # Custom middleware
│   └── SessionManagementMiddleware.cs  # NEW
├── Services/                          # Business logic
│   ├── ISessionService.cs             # NEW - session service interface
│   ├── RedisSessionService.cs         # NEW - Redis-backed implementation
│   ├── ConcurrentSessionGuard.cs      # NEW - concurrent login prevention
│   └── JwtTokenService.cs             # To be modified
├── Models/                            # Entity models
└── Program.cs                         # Service registration
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/ISessionService.cs | Interface: CreateSession, UpdateActivity, GetSession, Invalidate, IsActive |
| CREATE | Server/Services/RedisSessionService.cs | Redis-backed session tracking with 15-min sliding TTL |
| CREATE | Server/Services/ConcurrentSessionGuard.cs | Login-time check rejecting concurrent sessions (409 Conflict) |
| CREATE | Server/Middleware/SessionManagementMiddleware.cs | Per-request activity tracking resetting Redis TTL |
| CREATE | Server/Controllers/SessionController.cs | POST /api/session/extend — reset TTL, issue new JWT |
| MODIFY | Server/Controllers/AuthController.cs | Integrate ConcurrentSessionGuard on login, session creation, session cleanup on logout |
| MODIFY | Server/Services/JwtTokenService.cs | Add jti claim, check blacklist on validation |
| MODIFY | Server/Program.cs | Register Redis connection, session services, middleware, Polly circuit breaker |

## External References

- [StackExchange.Redis — .NET Redis Client](https://stackexchange.github.io/StackExchange.Redis/)
- [ASP.NET Core Middleware — Custom Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-8.0)
- [Redis — HSET + EXPIRE Atomic Operations](https://redis.io/commands/hset/)
- [Polly — Circuit Breaker Policy](https://github.com/App-vNext/Polly#circuit-breaker)
- [JWT — jti Claim for Token Revocation](https://datatracker.ietf.org/doc/html/rfc7519#section-4.1.7)
- [OWASP — Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)

## Build Commands

- `dotnet build` — Build solution
- `dotnet run --project Server` — Run backend
- `dotnet test` — Run tests

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] Authenticated request resets Redis session TTL to 15 minutes (AC-2)
- [ ] Session auto-expires after 15 minutes of inactivity (Redis TTL expiry)
- [ ] Expired session returns 401 on next request (AC-1)
- [ ] POST /api/session/extend resets TTL and returns new JWT (AC-4)
- [ ] POST /api/session/extend returns 401 if session already expired
- [ ] Second login attempt with active session returns 409 "Another active session exists" (AC-3)
- [ ] Explicit logout deletes Redis session and blacklists JWT jti
- [ ] JWT with blacklisted jti returns 401 on subsequent requests
- [ ] Redis unavailability handled gracefully (circuit breaker, requests still processed)
- [ ] Session state survives server restarts (Redis persistence)
- [ ] Simultaneous requests update last-activity atomically (no race condition)
- [ ] All session events logged to audit trail (NFR-012): login, extend, timeout, logout, concurrent rejection
- [ ] Swagger documentation generated for session endpoints (NFR-038)
- [ ] Input sanitization on all endpoints (NFR-018)

## Implementation Checklist

- [X] Implement `ISessionService` and `RedisSessionService` with Redis HSET/EXPIRE for 15-min sliding TTL
- [X] Implement `ConcurrentSessionGuard` rejecting second login with 409 when active session exists (FR-007, NFR-015)
- [X] Implement `SessionManagementMiddleware` resetting Redis TTL on every authenticated request
- [X] Create `POST /api/session/extend` endpoint resetting TTL and issuing new JWT
- [X] Implement JWT jti blacklist in Redis for immediate token revocation on logout/expiry
- [X] Modify login flow to check concurrent sessions and create Redis session on success
- [X] Modify logout flow to delete Redis session, blacklist JWT, and clear refresh token
- [X] Register Redis connection, services, middleware, and Polly circuit breaker in Program.cs
