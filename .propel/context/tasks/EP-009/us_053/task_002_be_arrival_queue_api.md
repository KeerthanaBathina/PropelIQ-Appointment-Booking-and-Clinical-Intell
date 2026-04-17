# Task - TASK_002

## Requirement Reference

- User Story: US_053
- Story Location: .propel/context/tasks/EP-009/us_053/us_053.md
- Acceptance Criteria:
    - AC-1: **Given** the staff member opens the arrival queue, **When** the dashboard loads, **Then** it displays all arrived patients sorted by appointment time and priority with sub-second response time.
    - AC-2: **Given** the queue is displayed, **When** a patient's status changes (new arrival, no-show, cancelled), **Then** the dashboard refreshes within 5 seconds without requiring manual reload.
    - AC-3: **Given** the queue dashboard is open, **When** the staff member views it, **Then** each entry shows patient name, appointment time, provider, arrival time, current wait time, and status badge (color-coded).
    - AC-4: **Given** the queue contains entries, **When** the staff member calculates average wait time, **Then** the system displays the average wait time for all currently waiting patients.
- Edge Case:
    - EC-1: When the dashboard has 100+ queue entries, system paginates with 25 entries per page and maintains sort order across pages.
    - EC-2: System uses optimistic UI updates with a "Last updated" timestamp; manual refresh button available for network latency.

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
| ORM | Entity Framework Core | 8.x |
| Caching | Upstash Redis | 7.x |
| Database | PostgreSQL | 16.x |

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

Implement the backend API endpoints for the real-time Arrival Queue Dashboard. This includes the `GET /api/queue/today` endpoint that returns today's queue entries sorted by appointment time and priority, with computed wait times, average wait time for waiting patients, server-side pagination (25/page), and filtering by provider, status, and appointment type. The endpoint integrates with Redis caching (5-min TTL) to achieve sub-second response time per NFR-004. The Queue Service layer handles sort logic, wait time computation, and cache invalidation.

## Dependent Tasks

- US_052 tasks (arrival status data — provides QueueEntry creation/update on arrival marking)
- US_004 tasks (Redis caching foundation — provides the Redis infrastructure and connection)
- task_003_db_queue_queries_caching (provides optimized database queries and indexes for queue retrieval)

## Impacted Components

| Component | Type | Project |
|-----------|------|---------|
| QueueController | New | Server (Backend) |
| IQueueService / QueueService | New | Server (Backend) |
| QueueEntryDto | New | Server (Backend) |
| QueuePagedResponseDto | New | Server (Backend) |
| QueueFilterParams | New | Server (Backend) |
| IQueueCacheService / QueueCacheService | New | Server (Backend) |

## Implementation Plan

1. **Create DTOs** for the queue API response:
   - `QueueEntryDto`: queue_id, appointment_id, patient_name, appointment_time, provider_name, appointment_type, arrival_time, wait_time_minutes (computed), wait_time_display (MM:SS string), priority (normal/urgent), status (waiting/in_visit/completed/no_show/scheduled), exceeds_threshold (bool).
   - `QueuePagedResponseDto`: items (list of QueueEntryDto), total_count, page, page_size, average_wait_time_minutes (for waiting patients only), last_updated (UTC timestamp), threshold_alert_count (count of patients waiting > 30 min).
   - `QueueFilterParams`: provider_id (optional), status (optional), page (default 1), page_size (default 25).

2. **Create `IQueueService` interface and `QueueService` implementation**:
   - `GetTodayQueueAsync(QueueFilterParams filters)`: Fetch today's queue entries with appointment joins, apply filters, sort by priority (urgent first) then appointment time ascending, compute wait_time from arrival_timestamp to now, compute average wait time for status=waiting entries, apply pagination, return `QueuePagedResponseDto`.
   - Sort algorithm: urgent priority patients appear at the top, then normal priority patients sorted by appointment_time ascending.

3. **Create `IQueueCacheService` interface and `QueueCacheService` implementation**:
   - `GetCachedQueueAsync(string cacheKey)`: Attempt to retrieve queue response from Redis.
   - `SetCachedQueueAsync(string cacheKey, QueuePagedResponseDto data, TimeSpan ttl)`: Store response in Redis with 5-min TTL.
   - `InvalidateQueueCacheAsync()`: Clear all queue cache keys (called when status changes via US_052).
   - Cache key pattern: `queue:today:{provider}:{status}:{page}` for granular caching.

4. **Create `QueueController`** with the following endpoint:
   - `GET /api/queue/today?providerId={id}&status={status}&page={n}&pageSize={n}`: Returns paginated, sorted, filtered queue for today. First checks Redis cache; on miss, calls QueueService, caches result with 5-min TTL, returns response. Requires `[Authorize(Roles = "Staff,Admin")]` attribute for RBAC per NFR-011.

5. **Implement wait time computation** in QueueService:
   - For each queue entry with status=waiting and a non-null arrival_timestamp: `wait_time_minutes = (DateTime.UtcNow - arrival_timestamp).TotalMinutes`.
   - Set `exceeds_threshold = wait_time_minutes > 30` (configurable via appsettings).
   - For entries without arrival_timestamp (status=scheduled): wait_time = null.

6. **Implement average wait time calculation**:
   - Filter queue entries where status=waiting and arrival_timestamp is not null.
   - Compute `average_wait_time_minutes = entries.Average(e => e.wait_time_minutes)`.
   - Include in `QueuePagedResponseDto.average_wait_time_minutes`.

7. **Implement pagination with sort preservation**:
   - Apply sorting (priority desc, appointment_time asc) before pagination.
   - Use EF Core `.Skip((page - 1) * pageSize).Take(pageSize)` after sorting.
   - Return total_count for frontend pagination controls.

8. **Register services in DI container** and add controller route mapping in `Program.cs`. Add configuration for wait time threshold (default 30 minutes) in `appsettings.json`.

## Current Project State

- Project structure is placeholder — to be updated based on completion of dependent tasks.

```
Server/
├── Controllers/
├── Services/
├── DTOs/
├── Data/
│   ├── Entities/
│   └── Repositories/
├── Program.cs
└── appsettings.json
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/DTOs/Queue/QueueEntryDto.cs | DTO for individual queue entry with computed fields |
| CREATE | Server/DTOs/Queue/QueuePagedResponseDto.cs | Paged response DTO with average wait time and alert count |
| CREATE | Server/DTOs/Queue/QueueFilterParams.cs | Filter/pagination parameters DTO |
| CREATE | Server/Services/Queue/IQueueService.cs | Queue service interface |
| CREATE | Server/Services/Queue/QueueService.cs | Queue service with sort, filter, wait time, and pagination logic |
| CREATE | Server/Services/Cache/IQueueCacheService.cs | Queue cache service interface |
| CREATE | Server/Services/Cache/QueueCacheService.cs | Redis caching layer with 5-min TTL and cache invalidation |
| CREATE | Server/Controllers/QueueController.cs | GET /api/queue/today endpoint with auth, caching, filtering |
| MODIFY | Server/Program.cs | Register IQueueService, IQueueCacheService in DI container |
| MODIFY | Server/appsettings.json | Add QueueSettings section with WaitTimeThresholdMinutes (default 30) |

## External References

- [ASP.NET Core 8 Web API Controllers](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [Entity Framework Core 8 — Querying Data](https://learn.microsoft.com/en-us/ef/core/querying/)
- [StackExchange.Redis for .NET](https://stackexchange.github.io/StackExchange.Redis/)
- [ASP.NET Core Authorization — Role-based](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-8.0)
- [EF Core 8 Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] GET /api/queue/today returns sorted queue (urgent first, then by appointment time ascending)
- [ ] Response includes computed wait_time_minutes for each waiting patient
- [ ] average_wait_time_minutes correctly calculated for waiting patients only
- [ ] Pagination returns correct page with 25 items and total_count
- [ ] Filters by provider, status work correctly
- [ ] Redis cache hit returns sub-second response (< 1 second per NFR-004)
- [ ] Cache miss fetches from DB, caches result with 5-min TTL
- [ ] Endpoint requires Staff or Admin role authorization
- [ ] threshold_alert_count correctly counts patients waiting > 30 minutes

## Implementation Checklist

- [ ] Create `QueueEntryDto`, `QueuePagedResponseDto`, and `QueueFilterParams` DTOs with computed wait time fields, average wait time, and threshold alert count
- [ ] Create `IQueueService` interface and `QueueService` with today's queue fetch, sort logic (urgent priority first, then appointment time ascending), and filter application
- [ ] Implement wait time computation from `arrival_timestamp` to `DateTime.UtcNow` with configurable 30-minute threshold detection
- [ ] Implement average wait time calculation for waiting patients (status=waiting, arrival_timestamp not null)
- [ ] Implement server-side pagination with `.Skip().Take()` preserving sort order and returning total_count
- [ ] Create `IQueueCacheService` and `QueueCacheService` with Redis `GET`/`SET` (5-min TTL), granular cache keys, and cache invalidation method
- [ ] Create `QueueController` with authorized `GET /api/queue/today` endpoint integrating cache-first strategy
- [ ] Register services in DI, add `QueueSettings` configuration for wait time threshold in `appsettings.json`
