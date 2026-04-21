# Task - task_002_be_appointment_booking_api

## Requirement Reference

- User Story: US_018
- Story Location: .propel/context/tasks/EP-002/us_018/us_018.md
- Acceptance Criteria:
    - AC-1: Given I select an available slot, When I confirm the booking, Then the system creates the appointment using optimistic locking (version field compare-and-swap) and returns confirmation within 2 seconds.
    - AC-2: Given another patient selects the same slot simultaneously, When both confirm, Then only the first transaction succeeds; the second receives "Slot no longer available" with refreshed availability.
    - AC-3: Given I select a slot but do not confirm, When 1 minute passes, Then the hold is released and the slot returns to available inventory.
    - AC-4: Given booking succeeds, When the confirmation is displayed, Then it shows appointment date, time, provider, and appointment type with a booking reference number.
- Edge Case:
    - EC-1: Database temporarily unavailable during booking -> System retries once, then displays "Service temporarily unavailable. Please try again."
    - EC-2: 90-day boundary -> Slot at exactly 90 days is allowed; 91 days is rejected with date range error.

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

Implement the appointment booking REST API with optimistic locking and temporary slot hold mechanism for the Appointment Booking feature (US_018). This backend task creates the `POST /api/appointments` endpoint that books an appointment using EF Core concurrency tokens (version field compare-and-swap) to prevent double-booking (FR-012, TR-015). It implements a Redis-based slot hold mechanism with 60-second TTL for temporary reservation before confirmation (AC-3), validates the 90-day advance booking boundary (FR-013), generates a unique booking reference number (AC-4), and returns 409 Conflict with 3 alternative available slots on concurrent booking failures (AC-2). The endpoint meets the 2-second response time target at P95 (NFR-001) and includes Polly retry logic for transient database failures (EC-1).

## Dependent Tasks

- US_017 task_002_be_appointment_slot_api (Slot availability service used for alternative slot suggestions on 409)
- US_017 task_003_db_appointment_slot_indexes (Database indexes for efficient slot queries)
- US_008 - Foundational - Requires Appointment entity with version concurrency token
- US_004 - Foundational - Requires Redis caching infrastructure and IDistributedCache registration

## Impacted Components

- **NEW** `AppointmentBookingController` - API controller for POST /api/appointments and slot hold endpoints (Server/Controllers/)
- **NEW** `IAppointmentBookingService` / `AppointmentBookingService` - Service layer for booking logic with optimistic locking (Server/Services/)
- **NEW** `BookingRequest` - Request model for slot booking with validation (Server/Models/DTOs/)
- **NEW** `BookingResponse` - Response DTO with appointment details and reference number (Server/Models/DTOs/)
- **NEW** `SlotHoldService` - Redis-based temporary slot hold with 60-second TTL (Server/Services/)
- **MODIFY** `AppointmentSlotCacheService` (from US_017) - Call InvalidateSlotsAsync on successful booking
- **MODIFY** `Program.cs` - Register new services in DI container

## Implementation Plan

1. **Create `BookingRequest` model** with properties: `SlotId` (required, Guid), `PatientId` (required, Guid — extracted from JWT claims), `ProviderId` (required, Guid), `AppointmentTime` (required, DateTime), `AppointmentType` (required, string). Add DataAnnotations validation: AppointmentTime must be >= today and <= today + 90 days (FR-013). Validate SlotId references an existing, non-cancelled slot.
2. **Create `BookingResponse` DTO** containing: `AppointmentId` (Guid), `BookingReference` (string, format: `BK-{YYYYMMDD}-{6-char-alphanumeric}`), `AppointmentDate` (Date), `AppointmentTime` (Time), `ProviderName` (string), `AppointmentType` (string), `Status` (string, "scheduled"). Include `CreatedAt` timestamp.
3. **Implement `SlotHoldService`** using `IDistributedCache` (Redis). Provide `AcquireHoldAsync(slotId, patientId)` — sets Redis key `hold:{slotId}` with value `patientId` and 60-second TTL. Return false if key already exists (held by another patient). Provide `ReleaseHoldAsync(slotId, patientId)` — deletes key only if current holder matches. Provide `IsHeldAsync(slotId)` — checks if hold exists. Redis TTL auto-expires holds after 60 seconds (AC-3).
4. **Implement `AppointmentBookingService.BookAppointmentAsync`** method. Verify slot hold belongs to requesting patient via `SlotHoldService`. Load Appointment entity from DB with version tracking (`ConcurrencyCheck` attribute on Version property). Attempt to update slot status to "scheduled" and increment version. On `DbUpdateConcurrencyException` (optimistic lock failure), catch and return conflict result with 3 next available slots from `IAppointmentSlotService` (AC-2). On success, generate booking reference number, invalidate Redis slot cache via `AppointmentSlotCacheService.InvalidateSlotsAsync`, release the hold, log audit event, and return booking confirmation (AC-1, AC-4).
5. **Create `AppointmentBookingController`** with two endpoints: `[HttpPost("api/appointments")]` for booking confirmation (requires `[Authorize]` with Patient role), and `[HttpPost("api/appointments/hold")]` / `[HttpDelete("api/appointments/hold/{slotId}")]` for acquiring and releasing slot holds. Return `201 Created` with `BookingResponse` on success, `409 Conflict` with alternative slots on concurrency failure, `400 Bad Request` for validation errors, `422 Unprocessable Entity` if hold not owned by requesting patient.
6. **Implement 90-day boundary validation** as a reusable validation attribute. `AppointmentTime` at exactly 90 days from today is accepted. `AppointmentTime` at 91+ days returns `400 Bad Request` with specific error: "Appointment date exceeds maximum 90-day advance booking window" (FR-013, EC-2).
7. **Add Polly retry policy** for transient PostgreSQL failures. Configure single retry with 500ms delay using `AddResiliencePipeline` in DI registration. On retry exhaustion, return `503 Service Unavailable` with message "Service temporarily unavailable. Please try again." (EC-1, NFR-032). Log each retry attempt with Serilog including correlation ID.
8. **Register services in DI container** (`IAppointmentBookingService`, `SlotHoldService`), add Polly resilience pipeline, and add OpenAPI documentation attributes with request/response examples for Swagger (NFR-038). Add structured logging for booking flow: hold acquired/released, booking attempt, concurrency conflict, booking success with reference number (NFR-035).

## Current Project State

```text
[Placeholder - to be updated based on dependent task completion]
Server/
  Controllers/
    AppointmentSlotsController.cs (from US_017)
    (new controller file to be created here)
  Services/
    IAppointmentSlotService.cs (from US_017)
    AppointmentSlotService.cs (from US_017)
    AppointmentSlotCacheService.cs (from US_017 - to modify)
    (new service files to be created here)
  Models/
    DTOs/
      SlotQueryParameters.cs (from US_017)
      SlotAvailabilityResponse.cs (from US_017)
      (new DTO files to be created here)
    Entities/
      Appointment.cs (from US_008 - has Version field)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/AppointmentBookingController.cs | POST /api/appointments, POST/DELETE hold endpoints with auth and validation |
| CREATE | Server/Services/IAppointmentBookingService.cs | Interface for booking service with BookAppointmentAsync |
| CREATE | Server/Services/AppointmentBookingService.cs | Booking logic with optimistic locking, reference generation, cache invalidation |
| CREATE | Server/Services/SlotHoldService.cs | Redis-based slot hold with 60-second TTL, acquire/release/check operations |
| CREATE | Server/Models/DTOs/BookingRequest.cs | Request model with slot ID, appointment time, 90-day validation |
| CREATE | Server/Models/DTOs/BookingResponse.cs | Response DTO with appointment details and booking reference number |
| MODIFY | Server/Services/AppointmentSlotCacheService.cs | Invoke InvalidateSlotsAsync from booking flow on successful booking |
| MODIFY | Server/Program.cs | Register IAppointmentBookingService, SlotHoldService, Polly resilience pipeline |

## External References

- EF Core concurrency tokens: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- EF Core DbUpdateConcurrencyException: https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbupdateconcurrencyexception
- ASP.NET Core 8 Web API: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- Polly resilience pipelines (.NET 8): https://learn.microsoft.com/en-us/dotnet/core/resilience/
- IDistributedCache with Redis: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Create `BookingRequest` model with SlotId, PatientId (from JWT), ProviderId, AppointmentTime, AppointmentType and 90-day advance booking validation (FR-013, EC-2)
- [x] Create `BookingResponse` DTO with AppointmentId, BookingReference (format `BK-{YYYYMMDD}-{6-char}`), appointment details, and status (AC-4)
- [x] Implement `SlotHoldService` with Redis-based `AcquireHoldAsync`/`ReleaseHoldAsync`/`IsHeldAsync` using 60-second TTL for temporary slot reservation (AC-3)
- [x] Implement `AppointmentBookingService.BookAppointmentAsync` with EF Core optimistic locking via version concurrency token, catch `DbUpdateConcurrencyException` for conflict handling (FR-012, TR-015, AC-1)
- [x] Return 409 Conflict response with next 3 alternative available slots from `IAppointmentSlotService` on concurrency failure (AC-2)
- [x] Create `AppointmentBookingController` with POST /api/appointments (booking), POST /api/appointments/hold (acquire), DELETE /api/appointments/hold/{slotId} (release) endpoints with `[Authorize]` (AC-1, AC-3)
- [x] Add Polly single-retry resilience pipeline with 500ms delay for transient DB failures, returning 503 on exhaustion (EC-1, NFR-032)
- [x] Register services in DI, add OpenAPI docs, and implement structured Serilog logging with correlation IDs for hold/booking/conflict events (NFR-035, NFR-038)
