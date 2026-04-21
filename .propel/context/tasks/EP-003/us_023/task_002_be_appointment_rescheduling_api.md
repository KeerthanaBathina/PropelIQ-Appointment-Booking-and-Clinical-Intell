# Task - task_002_be_appointment_rescheduling_api

## Requirement Reference

- User Story: US_023
- Story Location: .propel/context/tasks/EP-003/us_023/us_023.md
- Acceptance Criteria:
    - AC-1: Given I have a scheduled appointment, When I select "Reschedule" and choose a new slot, Then the old slot is released and the new appointment is created atomically within 2 seconds.
    - AC-2: Given the new appointment time is within 24 hours of the original, When I attempt to reschedule, Then the system rejects with "Cannot reschedule within 24 hours of appointment."
    - AC-3: Given rescheduling succeeds, When the confirmation displays, Then it shows both the original and new appointment times with a success message.
    - AC-4: Given I reschedule, When the transaction completes, Then updated confirmation is sent via email and SMS, and my calendar sync updates.
- Edge Case:
    - EC-1: If the new slot becomes unavailable during rescheduling, optimistic locking must return a conflict result with refreshed availability guidance.
    - EC-2: Patient-initiated rescheduling must reject walk-in appointments; staff-only modification remains out of scope for this patient API.

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
| API Documentation | Swagger (Swashbuckle) | 6.x |
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

Implement the patient-initiated appointment rescheduling backend flow for US_023. This task delivers FR-023 and the 2-second transactional target from NFR-001 by exposing an authenticated reschedule endpoint that atomically moves a scheduled appointment from its old slot to a new one using optimistic locking from DR-002 and TR-015. The service must enforce the 24-hour reschedule rule, reject patient rescheduling of walk-in appointments, release the old slot immediately after success, trigger updated email/SMS confirmations, and refresh calendar-sync data so downstream iCal generation updates the original event rather than duplicating it.

## Dependent Tasks

- US_017 task_002_be_appointment_slot_api (Replacement slot lookup and refreshed availability responses)
- US_018 task_002_be_appointment_booking_api (Optimistic booking logic and hold management)
- US_019 task_002_be_appointment_cancellation_api (24-hour cutoff enforcement pattern and slot-release invalidation)
- US_025 - Same-Epic - Calendar sync via iCal update contract
- US_034 - Cross-Epic - Updated booking confirmation email/SMS delivery integration

## Impacted Components

- **NEW** `IAppointmentReschedulingService` / `AppointmentReschedulingService` - Atomic reschedule orchestration, rule enforcement, and downstream side effects (Server/Services/)
- **NEW** `RescheduleAppointmentRequest` / `RescheduleAppointmentResponse` - DTOs for old appointment/new slot input and outcome messaging (Server/Models/DTOs/)
- **MODIFY** `AppointmentBookingController` or dedicated appointment controller - Add authenticated reschedule endpoint for patients (Server/Controllers/)
- **MODIFY** `AppointmentBookingService` - Expose reusable atomic slot-move or reschedule mutation path with optimistic locking (Server/Services/)
- **MODIFY** notification integration - Trigger updated booking confirmation payloads containing original and new appointment times (Server/Services/)
- **MODIFY** calendar-sync integration - Preserve event UID/update semantics for regenerated iCal output after reschedule (Server/Services/)

## Implementation Plan

1. **Add reschedule request and response contracts** carrying the current appointment identifier, selected new slot, patient-visible success or rejection message, and both original and new appointment details.
2. **Implement `AppointmentReschedulingService`** to verify patient ownership, reject walk-in appointments, and enforce the 24-hour reschedule rule against the original appointment time in UTC.
3. **Create a patient-authenticated reschedule endpoint** that accepts the current appointment and replacement slot, returning success, 24-hour rejection, walk-in restriction, or concurrency-conflict outcomes explicitly.
4. **Execute the reschedule transaction atomically** by acquiring the new slot through the existing booking/hold pathway, updating the appointment to the new time with optimistic locking, releasing the old slot, and invalidating affected slot-availability caches in one unit of work.
5. **Handle slot conflicts cleanly** by catching optimistic-locking or availability failures when the replacement slot is no longer open and returning a conflict result that prompts the UI to refresh options.
6. **Trigger downstream confirmation updates** so the updated email and SMS confirmation includes original and new appointment times and the calendar-sync layer can regenerate an iCal event using the original UID.
7. **Add structured logging, audit events, and OpenAPI metadata** for reschedule attempts, rejections, conflicts, successful slot moves, and downstream notification/calendar update outcomes.

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
    IAppointmentCancellationService.cs
    AppointmentCancellationService.cs
  Models/
    DTOs/
      SlotQueryParameters.cs
      SlotAvailabilityResponse.cs
      BookingRequest.cs
      BookingResponse.cs
      CancelAppointmentRequest.cs
      CancelAppointmentResponse.cs
    Entities/
      Appointment.cs
      NotificationLog.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IAppointmentReschedulingService.cs | Interface for atomic appointment reschedule operations |
| CREATE | Server/Services/AppointmentReschedulingService.cs | Rule enforcement, optimistic slot move, notification trigger, and calendar update orchestration |
| CREATE | Server/Models/DTOs/RescheduleAppointmentRequest.cs | Request payload containing current appointment and replacement slot |
| CREATE | Server/Models/DTOs/RescheduleAppointmentResponse.cs | Response payload with original/new appointment details and outcome state |
| MODIFY | Server/Controllers/AppointmentBookingController.cs | Add patient-authenticated reschedule endpoint |
| MODIFY | Server/Services/AppointmentBookingService.cs | Expose reusable atomic slot-move pathway for reschedule flow |
| MODIFY | Server/Services/AppointmentSlotCacheService.cs | Invalidate both released and newly booked slot availability after reschedule |
| MODIFY | Server/Program.cs | Register rescheduling services and dependencies |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- EF Core optimistic concurrency: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- EF Core transactions: https://learn.microsoft.com/en-us/ef/core/saving/transactions
- ASP.NET Core distributed caching: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Add request/response contracts for patient-initiated rescheduling with both original and new appointment details
- [x] Enforce patient ownership, 24-hour reschedule cutoff, and walk-in restriction before any slot mutation occurs
- [x] Perform the replacement-slot booking and original-slot release atomically with optimistic-locking protection
- [x] Return a conflict outcome when the selected replacement slot is no longer available and support refreshed availability handling
- [x] Trigger updated email/SMS confirmation workflows containing original and new appointment times
- [x] Preserve calendar event update semantics so regenerated iCal output updates the existing event rather than duplicating it
- [x] Add structured logs, audit events, and OpenAPI docs for reschedule attempts, rejections, conflicts, and successful updates