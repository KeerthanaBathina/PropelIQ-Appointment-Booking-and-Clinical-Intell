# Task - task_002_be_redis_caching_optimization

## Requirement Reference

- User Story: us_084
- Story Location: .propel/context/tasks/EP-015/us_084/us_084.md
- Acceptance Criteria:
  - AC-2: Given appointment slot availability is queried, When the data is available in Redis cache, Then the cached version is returned (TTL 5 minutes) without hitting the database.
  - AC-3: Given a cache miss occurs, When the database query completes, Then the result is stored in Redis cache with a 5-minute TTL for subsequent requests.
  - AC-4: Given an appointment is booked or cancelled, When the booking state changes, Then the related cache entries (slot availability for that provider/date) are invalidated immediately.
- Edge Case:
  - What happens when Redis itself is unavailable? System bypasses cache and queries the database directly; performance is degraded but functionality is preserved.
  - How does the system handle stale cache during high-frequency booking periods? Cache invalidation on write ensures consistency; slot lockout uses database-level locking (not cache).

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
| Backend | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Library | Polly | 8.x |
| Library | System.Text.Json | 8.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements deterministic cache-aside patterns and cache invalidation logic. No LLM inference involved.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Harden and extend the existing Redis caching layer (established in US_004 and US_017) to deliver production-grade cache-aside performance for appointment slot availability and patient profiles per NFR-030 and FR-097. This task provides four capabilities: (1) **Slot cache-aside hardening** — enhance the existing `AppointmentSlotCacheService` (from US_017) with composite cache key generation, explicit 5-minute TTL enforcement, cache miss metrics, and Redis unavailability fallthrough that bypasses cache and queries PostgreSQL directly (edge case 1); (2) **Patient profile caching** — implement a new `PatientProfileCacheService` that caches frequently-accessed patient profile data (demographics, insurance summary, recent appointments) with 5-minute TTL using the `ICacheService` cache-aside pattern; (3) **Write-through cache invalidation** — consolidate and harden cache invalidation hooks so that booking, cancellation, and rescheduling operations immediately invalidate affected slot cache entries (AC-4), ensuring no stale data is served during high-frequency booking periods (edge case 2); (4) **Cache observability** — emit cache hit/miss/error metrics via `IPerformanceTracker` and log cache operations for operational monitoring, supporting the >80% cache hit ratio target from NFR-004.

## Dependent Tasks

- US_004 task_002_be_cache_service_layer — Requires `ICacheService` / `RedisCacheService` with Polly circuit breaker for Redis.
- US_017 task_002_be_appointment_slot_api — Requires `AppointmentSlotCacheService` with `InvalidateSlotsAsync` and `AppointmentSlotService`.
- US_081 task_001_be_performance_instrumentation_alerting — Requires `IPerformanceTracker` for cache metrics emission.

## Impacted Components

- **NEW** `src/UPACIP.Service/Caching/PatientProfileCacheService.cs` — Cache-aside for patient profiles with 5-min TTL and Redis fallthrough
- **NEW** `src/UPACIP.Service/Caching/Models/CachedPatientProfile.cs` — Lightweight DTO: PatientId, FullName, DateOfBirth, InsuranceProvider, InsuranceStatus, RecentAppointmentCount
- **NEW** `src/UPACIP.Service/Caching/ICacheInvalidationCoordinator.cs` — Interface: InvalidateOnBookingAsync, InvalidateOnCancellationAsync, InvalidateOnRescheduleAsync
- **NEW** `src/UPACIP.Service/Caching/CacheInvalidationCoordinator.cs` — Centralized cache invalidation orchestrating slot and patient profile cache eviction
- **MODIFY** `Server/Services/AppointmentSlotCacheService.cs` — Add cache miss metrics, Redis fallthrough on unavailability, composite key validation
- **MODIFY** `Server/Services/AppointmentBookingService.cs` — Call `ICacheInvalidationCoordinator.InvalidateOnBookingAsync` after successful booking
- **MODIFY** `Server/Services/AppointmentCancellationService.cs` — Call `ICacheInvalidationCoordinator.InvalidateOnCancellationAsync` after successful cancellation
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register PatientProfileCacheService, CacheInvalidationCoordinator

## Implementation Plan

1. **Harden `AppointmentSlotCacheService` (AC-2, AC-3, edge case 1)**: Modify the existing `AppointmentSlotCacheService` (from US_017) to enforce production-grade caching behavior. Enhance as follows:
   - **Composite cache key validation**: Ensure cache keys follow the pattern `slots:{providerId}:{date:yyyy-MM-dd}` with input validation — reject null/empty provider IDs and dates outside the 90-day booking window (FR-013). Normalize date format to prevent key fragmentation.
   - **Explicit 5-minute TTL**: Replace any relative/sliding expiration with absolute expiration of exactly 5 minutes (`DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }`). Absolute expiration prevents indefinite TTL extension from frequent reads.
   - **Redis unavailability fallthrough (edge case 1)**: The underlying `ICacheService` (from US_004) already has a Polly circuit breaker that catches Redis failures. Ensure `GetSlotsAsync` returns `null` on any cache exception (triggering the database path in `AppointmentSlotService`), and `SetSlotsAsync` silently skips caching when Redis is unavailable. Log a warning on cache failure: `"Redis unavailable — slot query will hit database directly for provider={ProviderId} date={Date}"`.
   - **Cache metrics**: After each operation, emit metrics via `IPerformanceTracker`: `RecordLatency("cache.slots.hit", 1)` on cache hit, `RecordLatency("cache.slots.miss", 1)` on cache miss, `RecordLatency("cache.slots.error", 1)` on cache error. These metrics feed the >80% hit ratio target from NFR-004.

2. **Implement `PatientProfileCacheService` (AC-2, AC-3)**: Create a new caching service for frequently-accessed patient profile data. The cache-aside pattern: (a) `GetProfileAsync(Guid patientId)` — check Redis key `patient:profile:{patientId}`; on hit, return deserialized `CachedPatientProfile`; on miss, return null; (b) `SetProfileAsync(Guid patientId, CachedPatientProfile profile)` — serialize and store with 5-minute absolute TTL; (c) `InvalidateProfileAsync(Guid patientId)` — remove the cached entry. The `CachedPatientProfile` DTO contains only display-friendly fields: `PatientId`, `FullName`, `DateOfBirth`, `InsuranceProvider`, `InsuranceStatus`, `RecentAppointmentCount`, `LastVisitDate`. This avoids caching sensitive clinical data (PHI minimization) — full patient records are always fetched from PostgreSQL. Use `ICacheService.GetOrSetAsync<CachedPatientProfile>()` for the cache-aside pattern, inheriting the existing Redis circuit breaker from `RedisCacheService`. Emit cache metrics: `cache.patient_profile.hit`, `cache.patient_profile.miss`.

3. **Implement `CacheInvalidationCoordinator` (AC-4, edge case 2)**: Create `ICacheInvalidationCoordinator` / `CacheInvalidationCoordinator` as the single point of cache invalidation for appointment state changes. Methods:
   - `InvalidateOnBookingAsync(Guid providerId, DateTime appointmentDate, Guid patientId)` — invalidate the slot cache entry for the specific provider/date combination AND the patient profile cache (appointment count changed). Call `AppointmentSlotCacheService.InvalidateSlotsAsync(providerId, appointmentDate)` and `PatientProfileCacheService.InvalidateProfileAsync(patientId)`.
   - `InvalidateOnCancellationAsync(Guid providerId, DateTime appointmentDate, Guid patientId)` — same invalidation as booking (slot becomes available, patient profile changes).
   - `InvalidateOnRescheduleAsync(Guid providerId, DateTime oldDate, DateTime newDate, Guid patientId)` — invalidate TWO slot cache entries (old date and new date) plus the patient profile. Handles the rescheduling edge case where both the released slot and the newly booked slot must be fresh.
   All invalidation operations are fire-and-forget with error swallowing — cache invalidation failures must never block the booking/cancellation response. Log invalidation actions: `"Cache invalidated: slots for provider={ProviderId} date={Date}, patient profile={PatientId}"`.

4. **Integrate cache invalidation into booking flow (AC-4)**: Modify `AppointmentBookingService.BookAppointmentAsync()` to call `ICacheInvalidationCoordinator.InvalidateOnBookingAsync()` immediately after the database transaction commits successfully. The invalidation must happen AFTER the commit (not inside the transaction) to avoid invalidating cache for a booking that might roll back. This ensures that the next slot availability query for the affected provider/date fetches fresh data from PostgreSQL, preventing stale cache during high-frequency booking periods (edge case 2). The booking flow already uses database-level optimistic locking (`ConcurrencyCheck` from US_018) for slot contention — cache invalidation complements but does not replace this mechanism.

5. **Integrate cache invalidation into cancellation flow (AC-4)**: Modify `AppointmentCancellationService` to call `ICacheInvalidationCoordinator.InvalidateOnCancellationAsync()` after the appointment status is updated to "cancelled" and the database transaction commits. This ensures the released slot appears as available in subsequent queries within seconds rather than waiting for the 5-minute TTL to expire. The cancellation flow from US_019 already calls `AppointmentSlotCacheService.InvalidateSlotsAsync` — the coordinator consolidates this call and adds patient profile invalidation.

6. **Handle stale cache during high-frequency bookings (edge case 2)**: The cache-aside pattern combined with write-through invalidation handles most staleness. For the specific scenario where two users view the same slot simultaneously (both see it as available from cache), the system relies on the database-level optimistic locking (from US_018) as the source of truth: (a) first booking succeeds and invalidates cache; (b) second booking attempt fails with `DbUpdateConcurrencyException`; (c) the booking API returns HTTP 409 with 3 next available slots (fetched fresh from the database, not cache). Document this approach in code comments to clarify that cache is an optimization layer, not a consistency guarantor — database locking is the authoritative mechanism for slot contention.

7. **Add cache observability dashboard metrics**: Create a consolidated cache metrics summary accessible via the existing monitoring infrastructure. In the `PerformanceMonitoringService` (from US_081), add a periodic cache hit ratio computation: `hitRatio = totalHits / (totalHits + totalMisses) * 100`. Log the ratio every monitoring cycle: `Log.Information("CACHE_METRICS: HitRatio={HitRatio}%, SlotHits={SlotHits}, SlotMisses={SlotMisses}, ProfileHits={ProfileHits}, ProfileMisses={ProfileMisses}")`. Alert (via `ISlaMonitorService`) when the hit ratio drops below 80% (NFR-004 target). Track per-operation cache timings: `RecordLatency("cache.operation_ms", durationMs)` to identify slow serialization or Redis latency.

8. **Register services and update configuration**: In `Program.cs`: register `services.AddSingleton<PatientProfileCacheService>()`, `services.AddSingleton<ICacheInvalidationCoordinator, CacheInvalidationCoordinator>()`. Inject `ICacheInvalidationCoordinator` into `AppointmentBookingService` and `AppointmentCancellationService`. No new configuration sections needed — the 5-minute TTL is the default from `ICacheService` (US_004), and Redis connection is already configured. Ensure `PatientProfileCacheService` is injected into patient-facing controllers (`PatientController`, dashboard endpoints) for cache-aside integration at the service layer.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   ├── AppointmentController.cs
│   │   │   ├── PatientController.cs
│   │   │   └── Admin/
│   │   ├── Middleware/
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Caching/
│   │   │   ├── ICacheService.cs                     ← from US_004
│   │   │   └── RedisCacheService.cs                 ← from US_004
│   │   ├── Performance/
│   │   │   ├── IPerformanceTracker.cs               ← from US_081
│   │   │   └── PerformanceMonitoringService.cs      ← from US_081
│   │   ├── Monitoring/
│   │   └── Resilience/
│   │       ├── IExternalServiceResilienceProvider.cs ← from task_001
│   │       └── ExternalServiceResilienceProvider.cs  ← from task_001
│   └── UPACIP.DataAccess/
│       └── ApplicationDbContext.cs
├── Server/
│   ├── Services/
│   │   ├── IAppointmentBookingService.cs
│   │   ├── AppointmentBookingService.cs             ← from US_018
│   │   ├── AppointmentCancellationService.cs        ← from US_019
│   │   ├── AppointmentSlotService.cs                ← from US_017
│   │   └── AppointmentSlotCacheService.cs           ← from US_017
│   └── AI/
├── app/
├── config/
└── scripts/
```

> Assumes US_004 (ICacheService), US_017 (AppointmentSlotCacheService), US_018 (booking with invalidation), US_019 (cancellation with invalidation), US_081 (performance tracker), and task_001 (circuit breakers) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Caching/PatientProfileCacheService.cs | Cache-aside for patient profiles with 5-min TTL |
| CREATE | src/UPACIP.Service/Caching/Models/CachedPatientProfile.cs | Lightweight DTO: PatientId, FullName, InsuranceProvider, RecentAppointmentCount |
| CREATE | src/UPACIP.Service/Caching/ICacheInvalidationCoordinator.cs | Interface: InvalidateOnBookingAsync, InvalidateOnCancellationAsync, InvalidateOnRescheduleAsync |
| CREATE | src/UPACIP.Service/Caching/CacheInvalidationCoordinator.cs | Centralized slot + profile cache invalidation for booking state changes |
| MODIFY | Server/Services/AppointmentSlotCacheService.cs | Add cache metrics, Redis fallthrough logging, composite key validation |
| MODIFY | Server/Services/AppointmentBookingService.cs | Call ICacheInvalidationCoordinator.InvalidateOnBookingAsync post-commit |
| MODIFY | Server/Services/AppointmentCancellationService.cs | Call ICacheInvalidationCoordinator.InvalidateOnCancellationAsync post-commit |
| MODIFY | src/UPACIP.Api/Program.cs | Register PatientProfileCacheService, CacheInvalidationCoordinator |

## External References

- [Cache-Aside Pattern — Microsoft](https://learn.microsoft.com/en-us/azure/architecture/patterns/cache-aside)
- [ASP.NET Core Distributed Caching](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0)
- [DistributedCacheEntryOptions — AbsoluteExpirationRelativeToNow](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.distributed.distributedcacheentryoptions)
- [Optimistic Concurrency — EF Core](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [Redis Key Naming Conventions](https://redis.io/docs/latest/develop/use/keyspace/)

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build Server project
dotnet build Server/Server.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Slot availability query returns cached data on second request within 5-minute window (cache hit)
- [ ] After 5 minutes, slot availability query fetches fresh data from PostgreSQL (cache expiry)
- [ ] Patient profile query returns cached data on second request within 5-minute window
- [ ] After booking, the affected provider/date slot cache entry is invalidated immediately
- [ ] After cancellation, the released slot appears as available on the next query (not stale)
- [ ] After rescheduling, both old-date and new-date slot cache entries are invalidated
- [ ] When Redis is unavailable, slot and profile queries fall through to database without error
- [ ] Cache hit/miss metrics are emitted via IPerformanceTracker for operational monitoring
- [ ] Cache hit ratio alert triggers when ratio drops below 80%
- [ ] Two concurrent bookings for the same slot resolve via database locking (not cache)
- [ ] Cache invalidation failures are swallowed and logged — never block the booking response

## Implementation Checklist

- [ ] Harden `AppointmentSlotCacheService` with composite key validation, absolute 5-min TTL, Redis fallthrough, and cache metrics
- [ ] Create `CachedPatientProfile` DTO and implement `PatientProfileCacheService` with cache-aside pattern
- [ ] Implement `ICacheInvalidationCoordinator` / `CacheInvalidationCoordinator` with booking, cancellation, and reschedule invalidation
- [ ] Integrate `ICacheInvalidationCoordinator` into `AppointmentBookingService` post-commit invalidation
- [ ] Integrate `ICacheInvalidationCoordinator` into `AppointmentCancellationService` post-commit invalidation
- [ ] Document stale-cache resolution via database-level optimistic locking for concurrent bookings
- [ ] Add cache hit ratio monitoring and <80% alert in PerformanceMonitoringService
- [ ] Register `PatientProfileCacheService` and `CacheInvalidationCoordinator` in DI
