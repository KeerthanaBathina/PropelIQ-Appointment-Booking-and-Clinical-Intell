# Task - task_002_be_walkin_booking_api

## Requirement Reference

- User Story: US_022
- Story Location: .propel/context/tasks/EP-002/us_022/us_022.md
- Acceptance Criteria:
    - AC-1: Given I am on the staff dashboard, When I click "Walk-in Registration", Then a form opens for patient search or new patient creation with same-day slot selection.
    - AC-2: Given a walk-in patient has an existing record, When I search by name, DOB, or phone, Then matching patient records are displayed for selection.
    - AC-3: Given I select a same-day slot for the walk-in, When I confirm the booking, Then the appointment is created with "walk-in" designation and the patient is added to the arrival queue automatically.
    - AC-4: Given no same-day slots are available, When the walk-in form loads, Then the system displays "No same-day slots available" with the next available date/time.
- Edge Case:
    - EC-1: Patient-role callers must be blocked with the message "Walk-in bookings are available through staff only."
    - EC-2: Urgent walk-ins with no same-day slots must support an escalation path for emergency slot creation or supervisor review.

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

Implement the staff-only walk-in booking backend flow for US_022. This task delivers FR-021 and FR-022 by exposing staff-authorized same-day slot lookup, patient search, optional minimal patient creation, and walk-in appointment booking that persists `is_walk_in = true` and inserts a queue entry automatically. The flow must enforce staff-only access, return next available slot guidance when same-day capacity is exhausted, and integrate urgent-priority escalation with the arrival queue rules from FR-074 while preserving structured auditability and request tracing through NFR-012 and NFR-035.

## Dependent Tasks

- US_017 task_002_be_appointment_slot_api (Same-day slot lookup and availability reuse)
- US_018 task_002_be_appointment_booking_api (Appointment creation and optimistic booking mutation infrastructure)
- US_008 - Foundational - Requires Appointment and QueueEntry entities

## Impacted Components

- **NEW** `WalkInRegistrationController` - Staff-only endpoints for patient search, same-day slot lookup, and walk-in booking (Server/Controllers/)
- **NEW** `IWalkInRegistrationService` / `WalkInRegistrationService` - Service layer for same-day walk-in booking and queue insertion (Server/Services/)
- **NEW** `WalkInBookingRequest` / `WalkInBookingResponse` - DTOs for walk-in booking inputs and created appointment/queue results (Server/Models/DTOs/)
- **NEW** `WalkInPatientSearchRequest` / `WalkInPatientSearchResponse` - DTOs for patient lookup results by name, DOB, or phone (Server/Models/DTOs/)
- **MODIFY** `AppointmentBookingService` or shared appointment mutation service - Add walk-in designation handling for staff-created same-day appointments (Server/Services/)
- **MODIFY** `QueueService` or queue integration path - Create queue entry automatically with normal or urgent priority (Server/Services/)

## Implementation Plan

1. **Add staff-only API contracts** for patient search, same-day slot lookup, and walk-in booking, protected by staff/admin authorization rather than patient authorization.
2. **Implement patient search** by name, DOB, or phone with exact/partial matching appropriate for staff lookup, returning lightweight record summaries for selection.
3. **Support minimal new-patient creation inside the booking flow** so staff can register an unscheduled patient when no existing record is selected, reusing existing patient-validation rules rather than creating a separate onboarding flow.
4. **Implement same-day availability lookup** that returns only same-day slots for walk-in booking and includes the next available date/time when none exist.
5. **Create the walk-in booking transaction** that sets the appointment as `is_walk_in = true`, persists same-day appointment details, and automatically inserts a `QueueEntry` with normal priority or urgent priority when flagged.
6. **Enforce staff-only access and urgency handling** by rejecting patient-role callers with the required message and returning an escalation outcome when urgent walk-ins cannot be placed into existing same-day capacity.
7. **Add structured logging, audit events, and OpenAPI metadata** for patient search, walk-in booking, queue insertion, unauthorized attempts, and urgent escalation outcomes.

## Current Project State

```text
Server/
  Controllers/
    AppointmentSlotsController.cs
    AppointmentBookingController.cs
  Services/
    IAppointmentSlotService.cs
    AppointmentSlotService.cs
    IAppointmentBookingService.cs
    AppointmentBookingService.cs
    AppointmentSlotCacheService.cs
  Models/
    DTOs/
      SlotQueryParameters.cs
      SlotAvailabilityResponse.cs
      BookingRequest.cs
      BookingResponse.cs
    Entities/
      Appointment.cs
      QueueEntry.cs
      Patient.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/WalkInRegistrationController.cs | Staff-only endpoints for patient search, same-day slots, and walk-in booking |
| CREATE | Server/Services/IWalkInRegistrationService.cs | Interface for staff walk-in search and booking operations |
| CREATE | Server/Services/WalkInRegistrationService.cs | Same-day walk-in booking logic with queue insertion and urgency handling |
| CREATE | Server/Models/DTOs/WalkInPatientSearchRequest.cs | Search payload for patient lookup by name, DOB, or phone |
| CREATE | Server/Models/DTOs/WalkInPatientSearchResponse.cs | Patient search result payloads for staff selection |
| CREATE | Server/Models/DTOs/WalkInBookingRequest.cs | Booking payload containing patient selection/new patient data, slot, and urgency |
| CREATE | Server/Models/DTOs/WalkInBookingResponse.cs | Created walk-in appointment and queue-position response |
| MODIFY | Server/Services/AppointmentBookingService.cs | Support staff-created walk-in appointment designation |
| MODIFY | Server/Services/QueueService.cs | Add automatic queue-entry creation and urgent-priority handling for walk-in bookings |
| MODIFY | Server/Program.cs | Register walk-in registration services and related dependencies |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- ASP.NET Core authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-8.0
- EF Core querying: https://learn.microsoft.com/en-us/ef/core/querying/
- EF Core transactions: https://learn.microsoft.com/en-us/ef/core/saving/transactions
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Add staff-authorized APIs for patient lookup, same-day slot lookup, and walk-in booking
- [x] Search existing patient records by name, DOB, or phone and return selectable summaries for staff
- [x] Support minimal new-patient creation inside the walk-in booking flow when no existing match is chosen
- [x] Return same-day slot options only, plus next available date/time when same-day capacity is empty
- [x] Create walk-in appointments with `is_walk_in = true` and add patients to the arrival queue automatically
- [x] Support urgent-priority queue insertion or an escalation result when urgent walk-ins cannot fit existing same-day capacity
- [x] Reject patient-role access with the exact required staff-only message and log unauthorized attempts appropriately
- [x] Add structured logging, audit events, and OpenAPI docs for search, booking, queue insertion, and escalation outcomes