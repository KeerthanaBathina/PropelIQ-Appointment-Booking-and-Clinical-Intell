# Task - TASK_003

## Requirement Reference

- User Story: US_014
- Story Location: .propel/context/tasks/EP-001/us_014/us_014.md
- Acceptance Criteria:
  - AC-1: Session invalidation requires Redis key expiry and UserSession DB record for audit history.
  - AC-2: Activity tracking requires Redis key TTL reset — key structure must support sliding expiry.
  - AC-3: Concurrent session prevention requires Redis lookup of active session per user — requires single-key-per-user design.
  - AC-4: Session extend requires Redis key refresh — same key structure as AC-2.
- Edge Cases:
  - Server restarts mid-session: Redis-backed storage preserves session state (Upstash Redis with persistence).
  - Simultaneous requests: Redis atomic operations prevent race conditions on session updates.

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
| Caching | Upstash Redis | 7.x |
| Backend | .NET 8 (ASP.NET Core) | 8.x |
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

Define the Redis key structure for active session tracking and JWT blacklisting, create the PostgreSQL `user_sessions` table for session audit history, and configure the EF Core migration. Redis serves as the real-time session store with sliding 15-minute TTL while PostgreSQL stores immutable session history for HIPAA compliance audit trail (DR-016: 7-year retention). The design supports single active session per user (NFR-015), atomic TTL updates (no race conditions), and survives server restarts via Redis persistence.

## Dependent Tasks

- US_005 — Authentication scaffold (provides ApplicationDbContext, User entity, Redis connection configuration)

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `UserSession` entity | Server (Backend) | CREATE — PostgreSQL entity for session audit history |
| `ApplicationDbContext` | Server (Backend) | MODIFY — add DbSet<UserSession> and Fluent API configuration |
| EF Core migration | Server (Backend) | CREATE — migration for user_sessions table |
| Redis key documentation | Server (Backend) | CREATE — key structure documentation for session and blacklist keys |

## Implementation Plan

1. **Define Redis key structure** — Document and implement the Redis key conventions used by `RedisSessionService` (task_002):
   - Active session: `session:{userId}` → Redis Hash with fields:
     - `sessionId` (string, UUID) — unique session identifier
     - `lastActivity` (string, ISO 8601 UTC) — last activity timestamp
     - `loginAt` (string, ISO 8601 UTC) — session start time
     - `ipAddress` (string) — client IP at login
     - `userAgent` (string) — client user-agent at login
     - TTL: 900 seconds (15 minutes), reset on each activity update
   - JWT blacklist: `blacklist:{jti}` → Redis String with value "1"
     - TTL: matches JWT remaining lifetime (max 15 minutes for access tokens)
   - Key naming convention: lowercase, colon-separated, no PII in key names (NFR-017).

2. **Create UserSession entity** — PostgreSQL table `user_sessions` for immutable session audit history (not real-time tracking). Fields:
   - `Id` (UUID, primary key)
   - `UserId` (UUID, foreign key to AspNetUsers)
   - `SessionId` (UUID, correlates to Redis session)
   - `LoginAt` (DateTime, UTC)
   - `LogoutAt` (DateTime?, UTC, null if session expired without explicit logout)
   - `ExpirationReason` (enum: ExplicitLogout, InactivityTimeout, ConcurrentSessionReplacement, AdminForceLogout)
   - `IpAddress` (string, 45 chars max for IPv6)
   - `UserAgent` (string, 512 chars max)
   - `CreatedAt` (DateTime, UTC)

3. **Configure Fluent API mappings** — In `ApplicationDbContext.OnModelCreating()`:
   - `UserSession`: index on `UserId` for per-user session history queries, index on `SessionId` for correlation lookups, index on `LoginAt` for time-range audit queries.
   - Foreign key from `UserId` to `ApplicationUser` with restrict delete (sessions are audit records, must not be cascade deleted per DR-016).
   - No soft delete — session records are immutable audit logs retained for 7 years.

4. **Generate EF Core migration** — Create migration via `dotnet ef migrations add AddUserSessionsTable`. Verify generated SQL includes:
   - CREATE TABLE `user_sessions` with all columns and correct types.
   - CREATE INDEX on `user_id` for session history lookups.
   - CREATE INDEX on `session_id` for correlation.
   - CREATE INDEX on `login_at` for time-range queries.
   - Foreign key constraint to AspNetUsers with ON DELETE RESTRICT.
   Verify `Down()` method cleanly drops the table.

5. **Document Redis configuration requirements** — Document the Upstash Redis connection configuration needed in `appsettings.json`:
   - `Redis:ConnectionString` — Upstash Redis endpoint with TLS
   - `Redis:SessionTtlSeconds` — 900 (15 minutes, configurable)
   - `Redis:BlacklistEnabled` — true
   - Connection must use TLS (NFR-010: data in transit encryption).
   - Upstash free tier: 10K requests/day — estimate session overhead: ~100 requests/user/day (login + ~6 activity updates/hour × 8 hours + logout). Supports ~100 concurrent users within free tier.

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
Server/                                # Backend .NET 8 solution
├── Models/                            # Entity models
│   ├── UserSession.cs                 # NEW
│   └── ExpirationReason.cs            # NEW - enum
├── Data/                              # Data access
│   └── ApplicationDbContext.cs         # To be modified
├── Migrations/                        # EF Core migrations
│   └── YYYYMMDD_AddUserSessionsTable.cs  # NEW
└── docs/
    └── redis-key-structure.md         # NEW - key convention documentation
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/UserSession.cs | Entity: Id, UserId, SessionId, LoginAt, LogoutAt, ExpirationReason, IpAddress, UserAgent |
| CREATE | Server/Models/ExpirationReason.cs | Enum: ExplicitLogout, InactivityTimeout, ConcurrentSessionReplacement, AdminForceLogout |
| MODIFY | Server/Data/ApplicationDbContext.cs | Add DbSet<UserSession>, indexes on UserId/SessionId/LoginAt, FK with RESTRICT |
| CREATE | Server/Migrations/*_AddUserSessionsTable.cs | EF Core migration for user_sessions table |

## External References

- [Redis — Hash Data Type (HSET, HGETALL)](https://redis.io/docs/data-types/hashes/)
- [Redis — Key Expiry (EXPIRE, TTL)](https://redis.io/commands/expire/)
- [Upstash Redis — .NET Integration](https://upstash.com/docs/redis/sdks/dotnet)
- [EF Core — Migrations (.NET 8)](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=dotnet-core-cli)
- [HIPAA — Audit Log Retention Requirements](https://www.hhs.gov/hipaa/for-professionals/privacy/laws-regulations/index.html)

## Build Commands

- `dotnet ef migrations add AddUserSessionsTable --project Server` — Generate migration
- `dotnet ef database update --project Server` — Apply migration
- `dotnet ef database update <previous-migration> --project Server` — Rollback migration
- `dotnet build` — Build solution

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Migration applies cleanly on empty database
- [ ] Migration rollback (`Down()`) drops user_sessions table without errors
- [ ] `user_sessions` table created with correct columns and types
- [ ] Index on `user_id` exists for session history lookups
- [ ] Index on `session_id` exists for correlation
- [ ] Index on `login_at` exists for time-range audit queries
- [ ] Foreign key from `user_id` to AspNetUsers with ON DELETE RESTRICT (not CASCADE)
- [ ] ExpirationReason enum stored correctly (integer mapping)
- [ ] All DateTime columns store UTC values
- [ ] UUID primary keys generated correctly
- [ ] Redis key convention documented and matches RedisSessionService implementation

## Implementation Checklist

- [ ] Define Redis key structure documentation: `session:{userId}` hash and `blacklist:{jti}` string with TTLs
- [ ] Create `UserSession` entity with Id, UserId, SessionId, LoginAt, LogoutAt, ExpirationReason, IpAddress, UserAgent
- [ ] Create `ExpirationReason` enum (ExplicitLogout, InactivityTimeout, ConcurrentSessionReplacement, AdminForceLogout)
- [ ] Configure Fluent API: indexes on UserId/SessionId/LoginAt, FK with RESTRICT delete
- [ ] Generate and verify EF Core migration with `dotnet ef migrations add AddUserSessionsTable`
- [ ] Test migration apply and rollback on clean database
