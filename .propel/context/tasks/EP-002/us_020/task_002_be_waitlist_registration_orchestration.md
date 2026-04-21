# Task - task_002_be_waitlist_registration_orchestration

## Requirement Reference

- User Story: US_020
- Story Location: .propel/context/tasks/EP-002/us_020/us_020.md
- Acceptance Criteria:
    - AC-1: Given all slots for my preferred time are booked, When I select "Join Waitlist", Then I am registered on the waitlist with my preferred criteria (date, time, provider).
    - AC-2: Given I am on the waitlist, When a matching slot becomes available, Then I receive a notification (email + SMS) within 5 minutes of slot availability.
    - AC-3: Given I receive a waitlist notification, When I click the booking link, Then the slot is held for me for 1 minute to complete booking.
    - AC-4: Given multiple patients are waitlisted for the same slot, When the slot opens, Then the system notifies all waitlisted patients and the first to confirm books the slot.
- Edge Case:
    - EC-1: Waitlist entries remain active when the patient cancels a separate appointment unless explicitly removed.
    - EC-2: Slots that open within 24 hours still generate notifications, but the offer should note the upcoming time.

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

Implement the waitlist registration and offer orchestration backend flow for US_020. This backend task delivers FR-017, FR-018, and the 5-minute notification expectation from FR-042 by exposing patient waitlist registration APIs, detecting newly opened matching slots after availability changes, notifying all matching waitlisted patients through the existing email and SMS services, and converting the notification link into a 1-minute claim hold using the booking infrastructure from US_018. The flow must preserve first-confirm-wins behavior through existing optimistic booking controls, maintain structured audit/logging aligned to NFR-012 and NFR-035, and avoid leaking PII in logs per NFR-017.

## Dependent Tasks

- task_003_db_waitlist_schema.md (Waitlist persistence and indexes must exist)
- US_017 task_002_be_appointment_slot_api (Availability query and cache invalidation path must exist)
- US_018 task_002_be_appointment_booking_api (SlotHoldService and booking confirmation flow must exist)
- US_019 task_002_be_appointment_cancellation_api (Cancellation-triggered slot release should feed the waitlist matcher)
- US_032 - Same-Epic - Requires email service integration
- US_033 - Same-Epic - Requires SMS service integration

## Impacted Components

- **NEW** `WaitlistController` - API controller for waitlist registration and claim-link resolution (Server/Controllers/)
- **NEW** `IWaitlistService` / `WaitlistService` - Service layer for registration, matching, and offer issuance (Server/Services/)
- **NEW** `WaitlistOfferProcessor` - Background worker that evaluates newly opened slots and dispatches offers within 5 minutes (Server/Services/ or Server/BackgroundServices/)
- **NEW** `JoinWaitlistRequest` / `JoinWaitlistResponse` - DTOs for patient waitlist registration (Server/Models/DTOs/)
- **NEW** `ClaimWaitlistOfferRequest` / `ClaimWaitlistOfferResponse` - DTOs for notification-link validation and held-slot response (Server/Models/DTOs/)
- **MODIFY** `SlotHoldService` - Support waitlist claim holds keyed by offer token and slot identity (Server/Services/)
- **MODIFY** notification integration - Send waitlist offer email/SMS and log delivery outcomes (Server/Services/)

## Implementation Plan

1. **Create waitlist registration DTOs and endpoint contracts** that accept preferred date, time window, provider, and appointment type criteria, validate patient ownership from JWT context, and reject duplicate active entries for the same criteria.
2. **Implement `WaitlistService.RegisterAsync`** to persist active waitlist entries, normalize criteria comparisons, and return the registered preference summary for UI confirmation.
3. **Add a slot-availability trigger path** so waitlist evaluation runs when cancellations, releases, or newly published availability make a slot bookable again. Reuse existing cache invalidation or appointment mutation integration points rather than polling blindly.
4. **Implement `WaitlistOfferProcessor`** to query active entries matching the newly available slot, notify all eligible patients within 5 minutes, and record per-channel delivery results using the existing email and SMS services.
5. **Generate secure waitlist offer links** containing a short-lived claim token that resolves to the offered slot without embedding sensitive patient data in the URL.
6. **Implement claim-link handling** through a `POST` or `GET` offer-claim endpoint that validates the token, acquires a 60-second hold through `SlotHoldService`, and returns the held slot details for the booking UI.
7. **Preserve first-confirm-wins behavior** by routing the final booking confirmation through the existing optimistic booking flow so multiple notified patients can compete safely without overbooking.
8. **Add structured logging, audit events, and OpenAPI metadata** for registration, offer dispatch, and claim operations, ensuring sanitized logs and explicit outcomes for expired, already-claimed, or invalid tokens.

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
      NotificationLog.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/WaitlistController.cs | Endpoints for joining the waitlist and resolving notification claim links |
| CREATE | Server/Services/IWaitlistService.cs | Interface for waitlist registration, offer generation, and claim handling |
| CREATE | Server/Services/WaitlistService.cs | Business logic for matching criteria to available slots and issuing offers |
| CREATE | Server/BackgroundServices/WaitlistOfferProcessor.cs | Background processor that dispatches waitlist offers within 5 minutes of slot availability |
| CREATE | Server/Models/DTOs/JoinWaitlistRequest.cs | Registration payload for preferred criteria |
| CREATE | Server/Models/DTOs/JoinWaitlistResponse.cs | Confirmation payload for active waitlist registration |
| CREATE | Server/Models/DTOs/ClaimWaitlistOfferRequest.cs | Claim-token validation payload when the notification link is redeemed |
| CREATE | Server/Models/DTOs/ClaimWaitlistOfferResponse.cs | Held-slot payload returned after claim-link validation |
| MODIFY | Server/Services/SlotHoldService.cs | Add waitlist-offer hold acquisition path with 60-second TTL |
| MODIFY | Server/Program.cs | Register waitlist services, background processor, and endpoint dependencies |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- ASP.NET Core hosted services: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0
- ASP.NET Core authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-8.0
- EF Core querying: https://learn.microsoft.com/en-us/ef/core/querying/
- ASP.NET Core distributed caching: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Add waitlist registration request/response contracts and an authenticated API for joining the waitlist with preferred criteria
- [x] Implement waitlist registration logic that prevents duplicate active entries and preserves active status until explicit removal
- [x] Trigger waitlist matching when a slot becomes available and dispatch offers to all matching patients within 5 minutes
- [x] Use existing email and SMS integrations to send waitlist offers and record delivery outcomes per channel
- [x] Generate secure claim links and convert them into 60-second slot holds through the existing hold infrastructure
- [x] Route final booking confirmation through the existing optimistic booking flow so the first patient to confirm wins safely
- [x] Return explicit outcomes for expired, invalid, and already-claimed waitlist offer links
- [x] Add structured logging, audit events, and OpenAPI docs for registration, offer dispatch, and claim flows