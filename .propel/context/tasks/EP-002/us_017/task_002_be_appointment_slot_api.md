# Task - task_002_be_appointment_slot_api

## Requirement Reference

- User Story: US_017
- Story Location: .propel/context/tasks/EP-002/us_017/us_017.md
- Acceptance Criteria:
    - AC-1: Given I am on the booking page, When I select a date range and optionally a provider, Then the system displays all available slots matching my criteria within 2 seconds.
    - AC-3: Given I select a specific date, When the time slot grid loads, Then slots are shown in 30-minute increments with provider name and appointment type.
    - AC-4: Given slots are cached, When I request slots for a date within the cache TTL (5 minutes), Then the response returns in sub-second time from Redis cache.
- Edge Case:
    - EC-2: Booking windows beyond 90 days -> API rejects requests with date ranges exceeding 90 days from today per FR-013.

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
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| Resilience | Polly | 8.x |
| API Documentation | Swagger (Swashbuckle) | 6.x |
| Logging | Serilog | 8.x |
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
| Mobile | N/A | N/A |

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

Implement the appointment slot availability REST API endpoint with Redis caching for the Appointment Booking feature (US_017). This backend task creates the `GET /api/appointments/slots` endpoint that accepts date range, time, and provider filter parameters, queries available appointment slots from PostgreSQL, returns them structured in 30-minute increments with provider name and appointment type, and caches results in Redis with 5-minute TTL. The endpoint enforces 90-day advance booking validation (FR-013) and meets the 2-second response time target at P95 (NFR-001). Cache invalidation ensures stale slot data is cleared when appointments are booked or cancelled.

## Dependent Tasks

- task_003_db_appointment_slot_indexes (Database indexes must exist for performant slot queries)
- US_008 - Foundational - Requires Appointment entity and DbContext configuration
- US_004 - Foundational - Requires Redis caching infrastructure and IDistributedCache registration

## Impacted Components

- **NEW** `AppointmentSlotsController` - API controller for GET /api/appointments/slots (Server/Controllers/)
- **NEW** `IAppointmentSlotService` / `AppointmentSlotService` - Service layer for slot query and filtering logic (Server/Services/)
- **NEW** `SlotAvailabilityResponse` - DTO for slot availability API response (Server/Models/DTOs/)
- **NEW** `SlotQueryParameters` - Request model with date range, time, provider filters and validation (Server/Models/DTOs/)
- **NEW** `AppointmentSlotCacheService` - Redis cache wrapper for slot availability with 5-min TTL (Server/Services/)
- **MODIFY** `Program.cs` or DI registration - Register new services in dependency injection container

## Implementation Plan

1. **Create `SlotQueryParameters` request model** with properties: `DateFrom` (required, Date), `DateTo` (optional, Date, defaults to DateFrom), `ProviderId` (optional, Guid), `AppointmentType` (optional, string). Add FluentValidation or DataAnnotations: DateFrom >= today, DateTo <= today + 90 days (FR-013), DateTo >= DateFrom.
2. **Create `SlotAvailabilityResponse` DTO** containing a list of `SlotItem` objects. Each `SlotItem` includes: `SlotId` (Guid), `StartTime` (DateTime), `EndTime` (DateTime), `ProviderName` (string), `ProviderId` (Guid), `AppointmentType` (string), `IsAvailable` (bool). Include a `DateSummary` list for calendar dot indicators (date + available slot count).
3. **Implement `AppointmentSlotCacheService`** wrapping `IDistributedCache` (Redis). Build cache key from query parameters (`slots:{dateFrom}:{dateTo}:{providerId}`). Set TTL to 5 minutes (NFR-030). Implement `GetSlotsAsync` (cache hit path) and `SetSlotsAsync` (cache miss path). Implement `InvalidateSlotsAsync` to remove cached entries when appointments change.
4. **Implement `AppointmentSlotService`** with `GetAvailableSlotsAsync` method. Check cache first via `AppointmentSlotCacheService`. On cache miss, query PostgreSQL via EF Core for appointments in date range with status != cancelled, joined with provider data. Compute available 30-minute slots by diffing scheduled appointments against provider availability templates. Cache result before returning. Log cache hit/miss metrics.
5. **Create `AppointmentSlotsController`** with `[HttpGet("api/appointments/slots")]` endpoint. Accept `[FromQuery] SlotQueryParameters` with model validation. Require `[Authorize]` with Patient or Staff role. Call `IAppointmentSlotService.GetAvailableSlotsAsync`. Return `200 OK` with `SlotAvailabilityResponse` on success, `400 Bad Request` for invalid parameters (date >90 days), `401 Unauthorized` for unauthenticated requests.
6. **Implement cache invalidation hooks** by exposing `InvalidateSlotsAsync` on the cache service, to be called by the booking/cancellation flows (existing or future appointment mutation endpoints). Invalidate cache entries for the affected date range when an appointment is created, cancelled, or rescheduled.
7. **Add structured logging with Serilog** including correlation IDs (NFR-035) for the slot query flow. Log request parameters (sanitized), cache hit/miss status, query duration, and result count. Use `ILogger<AppointmentSlotsController>`.
8. **Register services in DI container** (`IAppointmentSlotService`, `AppointmentSlotCacheService`) and add OpenAPI documentation attributes to the controller for Swagger generation (NFR-038).

## Current Project State

```
src/
  UPACIP.DataAccess/
    Entities/
      Appointment.cs                       ✅ Extended — ProviderId, ProviderName, AppointmentType added
    Configurations/
      AppointmentConfiguration.cs          ✅ Updated — column configs for 3 new fields
    Migrations/
      20260420..._AddAppointmentProviderAndTypeFields.cs  ✅ Created
  UPACIP.Service/
    Appointments/
      IAppointmentSlotService.cs           ✅ Created
      AppointmentSlotService.cs            ✅ Created (cache-aside, 30-min slot gen, Mon–Fri)
      SlotQueryParameters.cs               ✅ Created (in Service project)
      SlotAvailabilityResponse.cs          ✅ Created (in Service project)
    Validation/
      SlotQueryParametersValidator.cs      ✅ Created (FR-013 90-day validation)
  UPACIP.Api/
    Controllers/
      AppointmentSlotsController.cs        ✅ Created (GET /api/appointments/slots)
    Program.cs                             ✅ Updated — DI registration + using added
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/AppointmentSlotsController.cs | GET /api/appointments/slots endpoint with auth, validation, Swagger docs |
| CREATE | Server/Services/IAppointmentSlotService.cs | Interface for slot availability query service |
| CREATE | Server/Services/AppointmentSlotService.cs | Slot query logic with EF Core, cache integration, 30-min increment computation |
| CREATE | Server/Services/AppointmentSlotCacheService.cs | Redis cache wrapper with 5-min TTL, cache key generation, invalidation |
| CREATE | Server/Models/DTOs/SlotQueryParameters.cs | Request model with date range, provider filter, 90-day validation |
| CREATE | Server/Models/DTOs/SlotAvailabilityResponse.cs | Response DTO with slot items and date summary for calendar indicators |
| MODIFY | Server/Program.cs | Register IAppointmentSlotService and AppointmentSlotCacheService in DI |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- Entity Framework Core 8 querying: https://learn.microsoft.com/en-us/ef/core/querying/
- IDistributedCache with Redis: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/
- Swashbuckle OpenAPI: https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-8.0

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Create `SlotQueryParameters` request model with DateFrom/StartDate, DateTo/EndDate, ProviderId, AppointmentType and 90-day range validation (FR-013) — placed in `UPACIP.Service.Appointments`
- [x] Create `SlotAvailabilityResponse` DTO with `SlotItem` list (slot ID, start/end time, provider name, appointment type, available flag), `ProviderSummary` list, and `DateAvailabilitySummary` list for calendar indicators — placed in `UPACIP.Service.Appointments`
- [x] Implement `AppointmentSlotService.GetAvailableSlotsAsync` with `ICacheService` cache-aside strategy, EF Core query fallback, 30-minute slot generation Mon–Fri 08:00–17:00 per provider (AC-1, AC-3, AC-4)
- [x] Extend `Appointment` entity with `ProviderId` (Guid?), `ProviderName` (string?, max 100), `AppointmentType` (string?, max 50); updated `AppointmentConfiguration`; generated EF migration
- [x] Create `AppointmentSlotsController` with `GET /api/appointments/slots` endpoint, `[Authorize(Policy = AnyAuthenticated)]`, model validation via FluentValidation, OpenAPI attributes (NFR-038)
- [x] Implement cache invalidation via `IAppointmentSlotService.InvalidateCacheAsync` (single-date, optional provider scope) for booking/cancellation flow integration
- [x] Add Serilog structured logging: request params, cache hit/miss, result count (NFR-035) — correlation ID injected by `CorrelationIdMiddleware`
- [x] Register `IAppointmentSlotService → AppointmentSlotService` as scoped in Program.cs
- [x] `dotnet build UPACIP.sln` → `Build succeeded. 0 Warning(s) 0 Error(s)`
