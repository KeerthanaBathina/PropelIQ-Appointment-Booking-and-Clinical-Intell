# Task - task_001_be_dynamic_slot_swap_engine

## Requirement Reference

- User Story: US_021
- Story Location: .propel/context/tasks/EP-002/us_021/us_021.md
- Acceptance Criteria:
    - AC-1: Given I have an appointment and registered preferred slot criteria, When a matching preferred slot opens (cancellation or new availability), Then the system automatically swaps my appointment to the preferred slot.
    - AC-2: Given the swap occurs, When it completes, Then my original slot is released within 1 minute and I receive a notification with old and new appointment times.
    - AC-3: Given staff has disabled auto-swap for my account, When a preferred slot opens, Then the system skips the swap and logs the reason.
    - AC-4: Given multiple patients prefer the same slot, When the slot opens, Then the system prioritizes by longest wait time and lowest no-show risk score.
    - AC-5: Given a preferred slot opens less than 24 hours before the appointment, When the system evaluates the swap, Then it skips automatic swap and notifies me for manual confirmation instead.
- Edge Case:
    - EC-1: Concurrent swap conflicts must be handled with optimistic locking and retry against the next eligible candidate or preferred slot.
    - EC-2: Appointments already marked with arrival status `arrived` or `in-visit` must be skipped from auto-swap processing.

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

Implement the dynamic preferred-slot swap backend engine for US_021. This task delivers FR-019 and FR-020, consumes the optimistic-locking guarantees from DR-002 and TR-015, and triggers FR-043 notifications when a swap succeeds or when a <24-hour opening requires manual confirmation. The service must detect newly available slots from cancellations or new availability, filter out ineligible appointments such as checked-in patients and accounts with auto-swap disabled, prioritize candidates by longest wait time and lowest no-show risk score, execute swaps atomically, release the original slot within 1 minute, and log all outcomes with correlation IDs per NFR-035.

## Dependent Tasks

- task_002_db_preferred_slot_swap_controls.md (Auto-swap control fields and preferred-slot query/index support must exist)
- US_018 task_002_be_appointment_booking_api (Optimistic locking and booking mutation infrastructure must exist)
- US_019 task_002_be_appointment_cancellation_api (Cancellation-triggered slot release must feed swap evaluation)
- US_020 task_002_be_waitlist_registration_orchestration.md (Slot-availability monitor pattern and release triggers should be reused)
- US_026 - Cross-Epic - Requires no-show risk score source for candidate prioritization
- US_036 - Cross-Epic - Requires swap notification delivery integration

## Impacted Components

- **NEW** `IPreferredSlotSwapService` / `PreferredSlotSwapService` - Core business logic for eligibility checks, prioritization, and swap execution (Server/Services/)
- **NEW** `PreferredSlotSwapProcessor` - Background worker or orchestrator that reacts to newly opened slots (Server/BackgroundServices/)
- **NEW** `PreferredSlotSwapResult` - Internal or DTO model for swap outcomes, skip reasons, and manual-confirmation offers (Server/Models/DTOs/ or Server/Services/)
- **MODIFY** `AppointmentBookingService` or shared appointment mutation service - Expose atomic update path for moving an existing appointment to a new slot (Server/Services/)
- **MODIFY** notification integration - Send swap-complete and manual-confirmation notifications with old/new appointment times (Server/Services/)
- **MODIFY** audit logging integration - Record swap success, skip reasons, conflicts, and manual-confirmation offers (Server/Services/ or Server/Infrastructure/)

## Implementation Plan

1. **Create `PreferredSlotSwapService`** to evaluate a newly available slot against appointments containing matching `preferred_slot_criteria`, while excluding cancelled appointments, walk-ins, and appointments with arrival states `arrived` or `in-visit`.
2. **Implement candidate prioritization** using longest wait time as the primary sort and lowest no-show risk score as the secondary sort, with deterministic tie-breaking so the selection remains stable across retries.
3. **Respect patient-level auto-swap controls** by skipping appointments or accounts flagged as auto-swap disabled, logging the reason, and avoiding any automatic mutation when staff has overridden the behavior.
4. **Apply the 24-hour decision rule** by automatically swapping only when the newly opened preferred slot is at least 24 hours away; otherwise generate a manual-confirmation offer and notification instead of mutating the appointment.
5. **Execute automatic swaps atomically** using optimistic locking on the appointment record, moving the patient to the preferred slot, releasing the original slot, invalidating slot caches, and retrying the next eligible candidate if a concurrency conflict occurs.
6. **Integrate notifications** so successful swaps send old/new appointment details and manual-confirmation offers send the available preferred slot details without auto-swapping.
7. **Add structured logging and audit events** for slot detection, candidate ranking, skipped candidates, conflicts, successful swaps, original-slot release, and manual-confirmation fallbacks.

## Current Project State

```text
Server/
  Controllers/
    AppointmentBookingController.cs
    AppointmentSlotsController.cs
    WaitlistController.cs
  Services/
    IAppointmentBookingService.cs
    AppointmentBookingService.cs
    IAppointmentSlotService.cs
    AppointmentSlotService.cs
    AppointmentSlotCacheService.cs
    SlotHoldService.cs
    IWaitlistService.cs
    WaitlistService.cs
  BackgroundServices/
    WaitlistOfferProcessor.cs
  Models/
    DTOs/
      BookingRequest.cs
      BookingResponse.cs
      JoinWaitlistRequest.cs
      JoinWaitlistResponse.cs
    Entities/
      Appointment.cs
      NotificationLog.cs
      QueueEntry.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IPreferredSlotSwapService.cs | Interface for evaluating and executing preferred-slot swaps |
| CREATE | Server/Services/PreferredSlotSwapService.cs | Eligibility filtering, candidate ranking, atomic swap execution, and skip/manual-confirmation logic |
| CREATE | Server/BackgroundServices/PreferredSlotSwapProcessor.cs | Triggered processor for newly opened slots from cancellations or new availability |
| CREATE | Server/Models/DTOs/PreferredSlotSwapResult.cs | Outcome model for swapped, skipped, conflicted, or manual-confirmation states |
| MODIFY | Server/Services/AppointmentBookingService.cs | Expose reusable appointment-reschedule mutation path with optimistic locking |
| MODIFY | Server/Services/AppointmentSlotCacheService.cs | Invalidate both original and new slot availability after automatic swap |
| MODIFY | Server/Program.cs | Register preferred-slot swap services and processor |

## External References

- ASP.NET Core hosted services: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0
- EF Core optimistic concurrency: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- EF Core querying: https://learn.microsoft.com/en-us/ef/core/querying/
- ASP.NET Core distributed caching: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Build preferred-slot swap service logic that filters eligible appointments by preferred-slot match, auto-swap setting, and arrival state
- [x] Rank multiple eligible patients by longest wait time and lowest no-show risk score with deterministic tie-breaking
- [x] Skip automatic swap for slots opening within 24 hours and emit manual-confirmation notifications instead
- [x] Execute automatic swaps with optimistic locking, release the original slot, and retry the next eligible candidate on conflict
- [x] Invalidate slot availability cache entries for both the released and claimed slots within the swap flow
- [x] Send swap-complete notifications containing old and new appointment times and log all delivery outcomes
- [x] Record structured logs and audit entries for swaps, skips, conflicts, and manual-confirmation fallbacks