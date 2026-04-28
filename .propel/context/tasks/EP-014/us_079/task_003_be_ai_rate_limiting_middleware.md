# Task - task_003_be_ai_rate_limiting_middleware

## Requirement Reference

- User Story: us_079
- Story Location: .propel/context/tasks/EP-014/us_079/us_079.md
- Acceptance Criteria:
  - AC-4: Given a user makes AI requests, When they exceed 100 requests per hour, Then the system returns HTTP 429 Too Many Requests with a Retry-After header.
- Edge Case:
  - How does the system handle rate limiting for staff performing bulk operations? Staff accounts have a configurable higher rate limit (500/hour); admins can temporarily increase limits.

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
| Library | System.Threading.RateLimiting | 8.x |
| Caching | Upstash Redis | 7.x |
| Library | StackExchange.Redis | 2.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements deterministic rate limiting middleware. No LLM inference or AI pipeline interaction — it guards the AI endpoint surface with request counting only.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement AI-specific rate limiting middleware using ASP.NET Core's built-in `System.Threading.RateLimiting` backed by Redis for distributed state per AIR-S08 and TR-027. The middleware applies a sliding window rate limit to all AI endpoints (routes matching `/api/ai/*` and `/api/intake/*`) with role-based limits: 100 requests/hour for patient users, 500 requests/hour for staff users, and a configurable limit for admin users (default 1000/hour, adjustable via an admin API endpoint). Rate limit state is stored in Redis using `ai:ratelimit:{userId}` keys with sorted set-based sliding window for accurate distributed counting across server instances. When the limit is exceeded, the middleware returns HTTP 429 Too Many Requests with `Retry-After` header indicating seconds until the next available request slot. All rate limit violations are logged to the audit trail per AIR-S04. An admin endpoint allows temporary rate limit increases for staff performing bulk operations (e.g., batch document parsing), with automatic reset after a configurable duration.

## Dependent Tasks

- US_005 — Requires JWT authentication with role claims (Patient, Staff, Admin) for role-based limit resolution.
- US_004 — Requires Redis infrastructure for distributed rate limit state.

## Impacted Components

- **NEW** `src/UPACIP.Service/AiSafety/IAiRateLimiter.cs` — Interface defining CheckRateLimitAsync, GetRemainingQuotaAsync methods
- **NEW** `src/UPACIP.Service/AiSafety/AiRateLimiter.cs` — Redis-backed sliding window rate limiter with role-based limits
- **NEW** `src/UPACIP.Service/AiSafety/Models/RateLimitOptions.cs` — Configuration: PatientLimitPerHour, StaffLimitPerHour, AdminLimitPerHour, WindowSizeMinutes
- **NEW** `src/UPACIP.Service/AiSafety/Models/RateLimitResult.cs` — Result DTO: IsAllowed, RemainingRequests, RetryAfterSeconds, CurrentCount
- **NEW** `src/UPACIP.Api/Middleware/AiRateLimitingMiddleware.cs` — ASP.NET Core middleware: extract user, check limit, return 429 or pass-through
- **NEW** `src/UPACIP.Api/Controllers/Admin/RateLimitAdminController.cs` — Admin endpoint for temporary limit overrides
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register IAiRateLimiter, bind RateLimitOptions, add AiRateLimitingMiddleware to pipeline
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add AiRateLimiting configuration section

## Implementation Plan

1. **Define rate limiting configuration model**: Create `RateLimitOptions` bound from `appsettings.json` section `AiRateLimiting` with: `int PatientLimitPerHour` (default: 100), `int StaffLimitPerHour` (default: 500), `int AdminLimitPerHour` (default: 1000), `int WindowSizeMinutes` (default: 60), `int TemporaryOverrideDurationMinutes` (default: 120). Add the configuration section to `appsettings.json`:
   ```json
   "AiRateLimiting": {
     "PatientLimitPerHour": 100,
     "StaffLimitPerHour": 500,
     "AdminLimitPerHour": 1000,
     "WindowSizeMinutes": 60,
     "TemporaryOverrideDurationMinutes": 120
   }
   ```

2. **Define rate limit result model**: Create `RateLimitResult` with: `bool IsAllowed` (true if under limit), `int RemainingRequests` (remaining quota in current window), `int RetryAfterSeconds` (seconds until next slot available, 0 if allowed), `int CurrentCount` (total requests in current window), `string UserId`, `string UserRole`, `int AppliedLimit` (the limit that was checked against — useful for debugging overrides).

3. **Define `IAiRateLimiter` interface**: Methods:
   - `Task<RateLimitResult> CheckRateLimitAsync(string userId, string userRole, CancellationToken cancellationToken = default)` — Increments counter and checks against role-based limit.
   - `Task<RateLimitResult> GetRemainingQuotaAsync(string userId, string userRole, CancellationToken cancellationToken = default)` — Read-only check of remaining quota (no increment).
   - `Task SetTemporaryOverrideAsync(string userId, int overrideLimit, int durationMinutes, CancellationToken cancellationToken = default)` — Admin-set temporary limit increase.

4. **Implement Redis-backed sliding window**: In `AiRateLimiter`, use a Redis sorted set per user with key `ai:ratelimit:{userId}`. Each request adds a member with score = Unix timestamp in milliseconds and value = a unique request ID (GUID). On `CheckRateLimitAsync`: (a) Remove all members with score < (now - windowSizeMinutes * 60 * 1000) using `ZREMRANGEBYSCORE`; (b) Count remaining members with `ZCARD`; (c) If count < applicable limit, add new member with `ZADD` and return `IsAllowed = true` with `RemainingRequests = limit - count - 1`; (d) If count >= limit, calculate `RetryAfterSeconds` from the oldest member's timestamp (`ZRANGE ... 0 0 WITHSCORES`) as `oldestTimestamp + windowMs - now` converted to seconds; (e) Return `IsAllowed = false`. Set key TTL to `WindowSizeMinutes + 5` minutes for automatic cleanup. Use a Lua script to execute steps (a)-(c) atomically to prevent race conditions in concurrent requests.

5. **Implement role-based limit resolution**: Resolve the applicable limit for a user: (a) Check for temporary override first — stored in Redis at `ai:ratelimit:override:{userId}` as a JSON object `{ "limit": 1000, "expiresAt": "ISO8601" }`; if present and not expired, use override limit. (b) If no override, resolve from user role claim: "Patient" → `PatientLimitPerHour`, "Staff" → `StaffLimitPerHour`, "Admin" → `AdminLimitPerHour`. (c) Unknown roles default to `PatientLimitPerHour` (principle of least privilege).

6. **Implement `AiRateLimitingMiddleware`**: Create ASP.NET Core middleware that intercepts requests to AI endpoints. Route matching: apply to paths starting with `/api/ai/` or `/api/intake/` (the AI-powered endpoints). On each request: (a) Extract `userId` and `role` from the JWT claims (`ClaimTypes.NameIdentifier` and `ClaimTypes.Role`); (b) If user is not authenticated, reject with 401 (not 429); (c) Call `IAiRateLimiter.CheckRateLimitAsync(userId, role)`; (d) Add response headers: `X-RateLimit-Limit: {appliedLimit}`, `X-RateLimit-Remaining: {remaining}`, `X-RateLimit-Reset: {resetTimestamp}`; (e) If `IsAllowed`, call `next(context)` to continue the pipeline; (f) If not allowed, return HTTP 429 with `Retry-After: {retryAfterSeconds}` header and JSON body `{ "error": "Rate limit exceeded", "retryAfterSeconds": N, "limit": L }`. Log rate limit violations at `Warning` level per AIR-S04.

7. **Implement admin rate limit override endpoint (edge case)**: Create `RateLimitAdminController` with `[Authorize(Roles = "Admin")]` at route `/api/admin/rate-limits`:
   - `POST /api/admin/rate-limits/override` — Body: `{ "userId": "guid", "overrideLimit": 1000, "durationMinutes": 120 }`. Validates: overrideLimit > 0 and <= 5000 (hard cap), durationMinutes > 0 and <= 480 (8 hours max). Stores override in Redis with TTL = durationMinutes. Returns 200 with override details.
   - `GET /api/admin/rate-limits/{userId}` — Returns current rate limit status for a user including remaining quota, applied limit, and any active override.
   - `DELETE /api/admin/rate-limits/override/{userId}` — Removes a temporary override, reverting to role-based default.
   Log all override operations in audit trail with admin user ID, target user ID, override limit, and duration.

8. **Register services and configure middleware**: Add `services.AddScoped<IAiRateLimiter, AiRateLimiter>()` in `Program.cs`. Bind `RateLimitOptions` from `AiRateLimiting` section. Add `app.UseMiddleware<AiRateLimitingMiddleware>()` after authentication middleware but before routing to AI controllers. The middleware must execute after `UseAuthentication()` and `UseAuthorization()` to have access to JWT claims.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   └── Admin/
│   │   │       └── KnowledgeBaseRefreshController.cs ← from US_078 task_002
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── AiSafety/
│   │   │   ├── IPiiRedactionService.cs            ← from US_074 task_001
│   │   │   ├── PiiRedactionService.cs             ← from US_074 task_001
│   │   │   ├── MedicalTermAllowlist.cs            ← from US_074 task_001
│   │   │   ├── PiiRedactionMiddleware.cs          ← from US_074 task_001
│   │   │   ├── IPromptInjectionDetector.cs        ← from task_001
│   │   │   ├── PromptInjectionDetector.cs         ← from task_001
│   │   │   ├── PromptSanitizationMiddleware.cs    ← from task_001
│   │   │   ├── IRagAccessControlFilter.cs         ← from task_002
│   │   │   ├── RagAccessControlFilter.cs          ← from task_002
│   │   │   ├── IContentFilterService.cs           ← from task_002
│   │   │   ├── ContentFilterService.cs            ← from task_002
│   │   │   ├── ContentFilterMiddleware.cs         ← from task_002
│   │   │   └── Models/
│   │   ├── Caching/
│   │   │   ├── ICacheService.cs
│   │   │   └── RedisCacheService.cs
│   │   ├── VectorSearch/
│   │   └── Rag/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       └── Entities/
├── Server/
│   └── AI/
│       └── AiGatewayService.cs                    ← from US_067
├── app/
├── config/
│   ├── medical-term-allowlist.json                ← from US_074 task_001
│   ├── prompt-injection-patterns.json             ← from task_001
│   └── content-filter-rules.json                  ← from task_002
└── scripts/
```

> Assumes US_005 (JWT auth), US_004 (Redis), US_067 (AI Gateway), task_001 (prompt injection), and task_002 (access control + content filtering) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/AiSafety/IAiRateLimiter.cs | Interface: CheckRateLimitAsync, GetRemainingQuotaAsync, SetTemporaryOverrideAsync |
| CREATE | src/UPACIP.Service/AiSafety/AiRateLimiter.cs | Redis sorted-set sliding window with atomic Lua script, role-based limits, override support |
| CREATE | src/UPACIP.Service/AiSafety/Models/RateLimitOptions.cs | Configuration DTO: PatientLimitPerHour, StaffLimitPerHour, AdminLimitPerHour, WindowSizeMinutes |
| CREATE | src/UPACIP.Service/AiSafety/Models/RateLimitResult.cs | Result DTO: IsAllowed, RemainingRequests, RetryAfterSeconds, CurrentCount |
| CREATE | src/UPACIP.Api/Middleware/AiRateLimitingMiddleware.cs | ASP.NET Core middleware: JWT claim extraction, rate check, 429 response with Retry-After |
| CREATE | src/UPACIP.Api/Controllers/Admin/RateLimitAdminController.cs | Admin endpoints: POST override, GET status, DELETE override |
| MODIFY | src/UPACIP.Api/Program.cs | Register IAiRateLimiter, bind RateLimitOptions, add AiRateLimitingMiddleware |
| MODIFY | src/UPACIP.Api/appsettings.json | Add AiRateLimiting configuration section with default limits |

## External References

- [ASP.NET Core Rate Limiting Middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- [System.Threading.RateLimiting — SlidingWindowRateLimiter](https://learn.microsoft.com/en-us/dotnet/api/system.threading.ratelimiting.slidingwindowratelimiter)
- [Redis Sorted Sets — ZADD, ZREMRANGEBYSCORE, ZCARD](https://redis.io/docs/data-types/sorted-sets/)
- [Redis Lua Scripting — EVAL](https://redis.io/docs/interact/programmability/eval-intro/)
- [RFC 6585 — HTTP 429 Too Many Requests](https://www.rfc-editor.org/rfc/rfc6585#section-4)
- [Retry-After Header — RFC 9110](https://www.rfc-editor.org/rfc/rfc9110#field.retry-after)
- [StackExchange.Redis — Scripting](https://stackexchange.github.io/StackExchange.Redis/Scripting.html)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln

# Run API project
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj
```

## Implementation Validation Strategy

- [x] `dotnet build` completes with zero errors for UPACIP.Service and UPACIP.Api projects
- [x] Patient user receives HTTP 429 after 100 AI requests within a 1-hour window
- [x] Staff user receives HTTP 429 after 500 AI requests within a 1-hour window
- [x] HTTP 429 response includes `Retry-After` header with correct seconds value
- [x] Response headers include `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`
- [x] Admin can set temporary rate limit override for a staff user via POST endpoint
- [x] Temporary override automatically expires after configured duration
- [x] Rate limit state is distributed across server instances via Redis
- [x] Non-AI endpoints (e.g., `/api/appointments`) are not affected by AI rate limiter
- [x] Audit log captures all rate limit violations with user ID and role

## Implementation Checklist

- [x] Create `RateLimitOptions` and `RateLimitResult` models in `src/UPACIP.Service/AiSafety/Models/`
- [x] Define `IAiRateLimiter` interface with `CheckRateLimitAsync`, `GetRemainingQuotaAsync`, and `SetTemporaryOverrideAsync` methods
- [x] Implement `AiRateLimiter` with Redis sorted-set sliding window and atomic Lua script
- [x] Implement role-based limit resolution with temporary override check
- [x] Implement `AiRateLimitingMiddleware` with JWT claim extraction, route matching, and 429 response
- [x] Implement `RateLimitAdminController` with override CRUD endpoints (admin-only)
- [x] Add `AiRateLimiting` configuration section to `appsettings.json`
- [x] Register services in DI and add middleware after authentication/authorization

## Evaluation Report

| Criterion | Result | Notes |
|-----------|--------|-------|
| T1: Build Zero Errors | PASS | Both UPACIP.Service and UPACIP.Api build with zero errors (warnings only, pre-existing) |
| T2: Rate Limit Enforcement | PASS | Atomic Lua script (ZREMRANGEBYSCORE+ZCARD+ZADD) prevents race conditions; Patient=100/hr, Staff=500/hr, Admin=1000/hr |
| T3: HTTP 429 + Headers | PASS | `Retry-After` computed from oldest window member; `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` set on every response |
| T4: Admin Override | PASS | POST/GET/DELETE at `/api/admin/rate-limits`; Redis TTL auto-expires override; AdminOnly policy enforced |
| T5: Audit Compliance (AIR-S04) | PASS | Violations logged at Warning with UserId/Role/Limit/Count/Path; override operations at Information with AdminId/TargetUserId |
