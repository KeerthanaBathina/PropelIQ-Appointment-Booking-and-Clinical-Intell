# Task - task_001_be_waitlist_offer_notification_fifo_and_expiry

## Requirement Reference

- User Story: US_036
- Story Location: .propel/context/tasks/EP-005/us_036/us_036.md
- Acceptance Criteria:
    - AC-1: Given a slot becomes available that matches a waitlisted patient's criteria, When the system detects the opening, Then the patient is notified via email and SMS within 5 minutes.
    - AC-3: Given multiple waitlisted patients match a newly available slot, When the notification is triggered, Then the system notifies patients in waitlist registration order (FIFO).
    - AC-4: Given a waitlist notification is sent, When the patient does not book within 24 hours, Then the system moves to the next waitlisted patient.
- Edge Case:
    - EC-1: When a waitlisted patient's contact info is invalid, the system skips the patient, logs the error, and notifies the next person on the waitlist.
    - EC-2: Waitlist offer progression must preserve first-confirm-wins behavior while ensuring only one active 24-hour offer is advanced per candidate order.

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
| Logging | Serilog | 8.x |
| Email Service | SMTP (SendGrid Free Tier or Gmail SMTP) | N/A |
| SMS Gateway | Twilio API | 2023-05 |
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

Implement the waitlist-offer notification orchestration for EP-005. This backend task builds the missing notification behavior on top of the planned waitlist engine so newly opened slots trigger email and SMS offers within 5 minutes, candidates are contacted in waitlist registration order, invalid contact records are skipped cleanly, and unclaimed offers age out after 24 hours before the next eligible patient is advanced. The logic must reuse the existing email and SMS integrations, preserve first-confirm-wins behavior from the booking flow, and keep `NotificationLog` and waitlist-entry state synchronized across offer, skip, expiry, and advance events.

## Dependent Tasks

- US_020 task_002_be_waitlist_registration_orchestration (Waitlist registration, offer trigger path, and claim-link handling must exist)
- US_020 task_003_db_waitlist_schema (Waitlist entry lifecycle state and notification timestamps must exist)
- US_032 task_002_be_notification_email_composition_and_logging (Email delivery orchestration must exist)
- US_033 task_002_be_notification_sms_orchestration_and_logging (SMS delivery orchestration must exist)
- US_018 task_002_be_appointment_booking_api (First-confirm-wins booking flow and hold handling must exist)

## Impacted Components

- **NEW** `IWaitlistOfferNotificationService` / `WaitlistOfferNotificationService` - Applies FIFO waitlist offer dispatch, invalid-contact skip, and 24-hour advance rules (Server/Services/Notifications/)
- **NEW** `WaitlistOfferNotificationRequest` - Contract describing slot-availability context, targeted waitlist entry, and offer timing metadata (Server/Models/DTOs/)
- **MODIFY** `WaitlistOfferProcessor` - Dispatch only the next eligible waitlist candidate in FIFO order and re-enter progression after expiry or invalid contact outcomes (Server/BackgroundServices/)
- **MODIFY** `WaitlistService` - Update waitlist entry lifecycle state for offered, expired, skipped-contact, and booked transitions (Server/Services/)
- **MODIFY** notification integration path - Record waitlist offer email and SMS delivery outcomes alongside progression events (Server/Services/Notifications/)
- **MODIFY** `Program.cs` - Register waitlist offer notification services

## Implementation Plan

1. **Create a waitlist offer notification service** that accepts newly available slot context and selects the next eligible waitlist entry in FIFO order based on registration timestamp.
2. **Send waitlist offers via email and SMS within the 5-minute window** by reusing the existing channel services and composing offer payloads around slot details and the existing claim-link flow.
3. **Skip invalid contact records deterministically** by recognizing bounced or invalid-contact delivery outcomes, logging the skip reason, and immediately advancing to the next FIFO waitlist candidate.
4. **Track active-offer state for 24 hours** so a patient who does not claim the slot in time is marked expired and the next waitlisted patient is contacted automatically.
5. **Preserve first-confirm-wins behavior** by allowing only one active candidate advance at a time for a slot while still relying on the existing booking hold and confirmation flow to resolve races safely.
6. **Keep waitlist lifecycle and audit data synchronized** by updating waitlist entry state and logging per-channel delivery plus expiry or skip outcomes consistently.
7. **Reuse the orchestration for later waitlist expansion** by keeping FIFO selection, expiry handling, and notification composition independent from slot-detection code.

## Current Project State

```text
Server/
  Controllers/
    WaitlistController.cs
  Services/
    WaitlistService.cs
    Notifications/
      NotificationEmailService.cs
      NotificationSmsService.cs
  BackgroundServices/
    WaitlistOfferProcessor.cs
  Models/
    Entities/
      WaitlistEntry.cs
      NotificationLog.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Notifications/IWaitlistOfferNotificationService.cs | Interface for FIFO waitlist offer dispatch and expiry advancement |
| CREATE | Server/Services/Notifications/WaitlistOfferNotificationService.cs | Orchestrate waitlist offer email or SMS delivery, invalid-contact skip, and 24-hour progression |
| CREATE | Server/Models/DTOs/WaitlistOfferNotificationRequest.cs | Slot-availability and candidate context payload for waitlist offer notifications |
| MODIFY | Server/BackgroundServices/WaitlistOfferProcessor.cs | Advance waitlist offers in FIFO order and requeue progression after expiry or invalid contact |
| MODIFY | Server/Services/WaitlistService.cs | Persist waitlist offer lifecycle transitions and next-candidate advancement state |
| MODIFY | Server/Program.cs | Register waitlist offer notification services |

## External References

- ASP.NET Core hosted services: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0
- EF Core querying: https://learn.microsoft.com/en-us/ef/core/querying/
- ASP.NET Core distributed caching: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Notify newly eligible waitlist patients via email and SMS within 5 minutes of slot availability
- [ ] Enforce FIFO candidate ordering from waitlist registration time
- [ ] Skip invalid-contact patients, log the error, and advance immediately to the next candidate
- [ ] Expire unclaimed offers after 24 hours and notify the next waitlisted patient automatically
- [ ] Keep first-confirm-wins behavior intact while only one candidate is actively advanced for a slot at a time
- [ ] Persist waitlist lifecycle and `NotificationLog` outcomes consistently for offer, skip, expiry, and booking transitions