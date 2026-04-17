# Task - task_002_be_appointment_cancellation_api

## Requirement Reference

- User Story: US_019
- Story Location: .propel/context/tasks/EP-002/us_019/us_019.md
- Acceptance Criteria:
    - AC-1: Given I have a scheduled appointment, When I cancel more than 24 hours before the appointment, Then the appointment status changes to "cancelled" and the slot is released within 1 minute.
    - AC-2: Given I attempt to cancel within 24 hours of the appointment, When I submit the cancellation, Then the system displays "Cancellations within 24 hours are not permitted. Please contact the clinic."
    - AC-3: Given a slot is released after cancellation, When another patient views availability, Then the released slot appears as available within 1 minute of cancellation.
    - AC-4: Given cancellation is confirmed, When the process completes, Then an audit log entry records the cancellation with user attribution and timestamp.
- Edge Case:
    - EC-1: Already-cancelled appointment requests must return "This appointment has already been cancelled."
    - EC-2: All cancellation cutoff comparisons must be performed in UTC, with display conversion handled separately by the frontend.

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
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Authentication | ASP.NET Core Identity + JWT | 8.x |
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

Implement the appointment cancellation backend flow that enforces the 24-hour policy, marks appointments as cancelled, releases the corresponding slot back into searchable inventory within 1 minute, and records an immutable audit event. This backend task implements FR-015 and FR-016 using the existing Appointment model constraints from DR-002 and DR-012, extends the appointment API surface with an authenticated patient-owned cancellation endpoint, performs the cutoff comparison in UTC, and returns explicit idempotent and policy-blocked messages. The implementation must preserve NFR-012 auditability, NFR-030 cache freshness for slot availability, and NFR-035 structured request tracing so the cancelled slot is visible again to other patients through the existing slot-viewing flow.

## Dependent Tasks

- US_017 task_002_be_appointment_slot_api (Slot availability service and cache invalidation path must exist)
- US_018 task_002_be_appointment_booking_api (Booked appointment creation flow and appointment API surface must exist)
- US_008 - Foundational - Requires Appointment entity

## Impacted Components

- **MODIFY** `AppointmentBookingController` - Add authenticated cancellation endpoint and response contracts (Server/Controllers/)
- **NEW** `AppointmentCancellationService` - Encapsulate cancellation policy enforcement, status mutation, cache invalidation, and audit logging (Server/Services/)
- **NEW** `CancelAppointmentRequest` / `CancelAppointmentResponse` - DTOs for cancellation command and result messages (Server/Models/DTOs/)
- **MODIFY** `AppointmentSlotCacheService` - Expose targeted invalidation for released appointment dates and provider scope (Server/Services/)
- **MODIFY** audit logging integration - Record cancellation actor, resource, and UTC timestamp (Server/Services/ or Server/Infrastructure/)

## Implementation Plan

1. **Add cancellation request and response contracts** that carry appointment identity, normalized outcome status, patient-visible message text, the updated appointment status, and UTC timestamps needed by downstream consumers.
2. **Implement `AppointmentCancellationService`** to load the appointment by identifier, verify the caller owns the appointment or has the proper role, reject non-scheduled appointments idempotently, and evaluate the 24-hour cancellation cutoff in UTC against `appointment_time`.
3. **Add an authenticated cancellation endpoint** on the appointment controller using a command-style route such as `POST /api/appointments/{appointmentId}/cancel`. Return success for valid cancellations, a policy-blocked result for within-24-hours requests, and the already-cancelled message for repeat requests.
4. **Persist the cancellation atomically** by updating appointment status to `cancelled`, setting `updated_at`, and preserving optimistic concurrency behavior so concurrent mutation attempts do not re-open the slot or lose audit accuracy.
5. **Release the slot back to inventory within 1 minute** by invalidating or refreshing the existing slot-availability cache for the affected provider and appointment date immediately after cancellation so subsequent availability reads include the newly open slot.
6. **Write immutable audit logging** that captures cancellation action, user attribution, appointment identifier, and UTC event timestamp to satisfy the traceability requirement and existing audit conventions.
7. **Add structured logging and API documentation** for cancellation outcomes, including correlation IDs, sanitized request context, response semantics, and the explicit policy message required by AC-2.

## Current Project State

```text
Server/
  Controllers/
    AppointmentSlotsController.cs
    AppointmentBookingController.cs
  Services/
    IAppointmentSlotService.cs
    AppointmentSlotService.cs
    AppointmentSlotCacheService.cs
    IAppointmentBookingService.cs
    AppointmentBookingService.cs
    SlotHoldService.cs
  Models/
    DTOs/
      SlotQueryParameters.cs
      SlotAvailabilityResponse.cs
      BookingRequest.cs
      BookingResponse.cs
    Entities/
      Appointment.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IAppointmentCancellationService.cs | Interface for cancellation policy and execution flow |
| CREATE | Server/Services/AppointmentCancellationService.cs | UTC cutoff validation, status mutation, slot-release cache invalidation, and audit logging |
| CREATE | Server/Models/DTOs/CancelAppointmentRequest.cs | Request DTO for cancellation command metadata |
| CREATE | Server/Models/DTOs/CancelAppointmentResponse.cs | Response DTO carrying status, message, and updated appointment details |
| MODIFY | Server/Controllers/AppointmentBookingController.cs | Add authenticated cancellation endpoint and response mapping |
| MODIFY | Server/Services/AppointmentSlotCacheService.cs | Add targeted invalidation for released slots after cancellation |
| MODIFY | Server/Program.cs | Register appointment cancellation service and related dependencies |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- ASP.NET Core authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-8.0
- EF Core 8 optimistic concurrency: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- ASP.NET Core distributed caching: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Create cancellation DTOs that return normalized success, policy-blocked, and already-cancelled outcomes with patient-visible message text
- [ ] Implement `AppointmentCancellationService` to enforce patient ownership, scheduled-only cancellation, and UTC-based 24-hour cutoff validation
- [ ] Add authenticated cancellation endpoint on `AppointmentBookingController` and return the exact required policy-blocked and already-cancelled messages
- [ ] Persist appointment status changes to `cancelled` with concurrency-safe update behavior and UTC timestamps
- [ ] Invalidate released-slot cache entries immediately so slot availability queries show the cancelled slot again within 1 minute
- [ ] Record immutable audit logs with user attribution, appointment identifier, action type, and UTC timestamp for every successful cancellation
- [ ] Add structured logging and OpenAPI metadata for the cancellation flow without exposing sensitive data in logs