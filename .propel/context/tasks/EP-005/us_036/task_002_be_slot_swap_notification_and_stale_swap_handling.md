# Task - task_002_be_slot_swap_notification_and_stale_swap_handling

## Requirement Reference

- User Story: US_036
- Story Location: .propel/context/tasks/EP-005/us_036/us_036.md
- Acceptance Criteria:
    - AC-2: Given a dynamic slot swap occurs for a patient, When the swap is confirmed, Then the system sends a notification listing old and new appointment times.
- Edge Case:
    - EC-1: If the patient's contact info is invalid during slot-swap notification delivery, the system logs the delivery failure without rolling back the already-completed swap.
    - EC-2: If a slot swap is evaluated for a patient who has since cancelled, the system skips the swap, releases the slot back to availability, and logs the event.

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

Implement slot-swap notification delivery and stale-swap safeguards for EP-005. This backend task turns completed dynamic slot swaps into patient-facing email and SMS notifications that clearly list the original and new appointment times, while also adding the last-mile guardrails needed when swap execution encounters cancelled patients or unusable contact data. The notification flow must reuse the existing channel services, preserve the swap transaction as the source of truth, release newly opened slots back to availability when a stale cancelled appointment is detected before swap completion, and keep delivery plus skip outcomes auditable in `NotificationLog` and operational logs.

## Dependent Tasks

- US_021 task_001_be_dynamic_slot_swap_engine (Swap eligibility, execution, and slot-release logic must exist)
- US_021 task_002_db_preferred_slot_swap_controls (Swap notification typing and preferred-slot control persistence must exist)
- US_032 task_002_be_notification_email_composition_and_logging (Email delivery orchestration must exist)
- US_033 task_002_be_notification_sms_orchestration_and_logging (SMS delivery orchestration must exist)
- US_019 task_002_be_appointment_cancellation_api (Cancelled appointment state and slot-release semantics must exist)

## Impacted Components

- **NEW** `ISlotSwapNotificationService` / `SlotSwapNotificationService` - Compose and deliver slot-swap notifications with old and new appointment times (Server/Services/Notifications/)
- **NEW** `SlotSwapNotificationRequest` - Contract describing original slot, new slot, patient context, and swap outcome metadata (Server/Models/DTOs/)
- **MODIFY** `PreferredSlotSwapService` - Invoke the slot-swap notification service after successful swaps and re-check cancelled state before committing stale swaps (Server/Services/)
- **MODIFY** `PreferredSlotSwapProcessor` - Log skipped stale-swap events and return released slots to availability when cancellation invalidates the swap candidate (Server/BackgroundServices/)
- **MODIFY** notification integration path - Persist slot-swap delivery and failure outcomes in `NotificationLog` without rolling back completed swaps (Server/Services/Notifications/)
- **MODIFY** `Program.cs` - Register slot-swap notification services

## Implementation Plan

1. **Create a slot-swap notification service** that receives completed swap details and dispatches patient notifications through existing email and SMS channel services.
2. **Compose swap notifications with explicit before-and-after context** so patients receive the old appointment time, the new appointment time, and any provider or date changes relevant to the swap.
3. **Re-check cancellation state before finalizing swap-dependent notifications** so appointments cancelled since selection are skipped rather than mutated or messaged incorrectly.
4. **Release the newly opened slot back to availability when stale cancelled appointments invalidate the swap** and record the skip reason for audit and troubleshooting.
5. **Treat invalid contact information as a delivery concern, not a swap rollback trigger** by logging channel failures or bounced outcomes while preserving the already-applied swap result.
6. **Persist slot-swap delivery outcomes consistently** so successful, failed, or bounced email and SMS notifications remain auditable alongside the swap transaction.
7. **Keep the notification service reusable** for later manual-confirmation or slot-upgrade messaging by isolating old-versus-new appointment formatting from the swap engine core.

## Current Project State

```text
Server/
  Services/
    PreferredSlotSwapService.cs
    Notifications/
      NotificationEmailService.cs
      NotificationSmsService.cs
  BackgroundServices/
    PreferredSlotSwapProcessor.cs
  Models/
    Entities/
      Appointment.cs
      NotificationLog.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Notifications/ISlotSwapNotificationService.cs | Interface for slot-swap notification delivery with old and new appointment details |
| CREATE | Server/Services/Notifications/SlotSwapNotificationService.cs | Compose and send slot-swap email and SMS notifications |
| CREATE | Server/Models/DTOs/SlotSwapNotificationRequest.cs | Swap-notification payload containing original and updated appointment context |
| MODIFY | Server/Services/PreferredSlotSwapService.cs | Invoke notification delivery after successful swaps and guard against stale cancelled appointments |
| MODIFY | Server/BackgroundServices/PreferredSlotSwapProcessor.cs | Log stale-swap skips and ensure released-slot availability is restored when cancellation invalidates a swap |
| MODIFY | Server/Program.cs | Register slot-swap notification services |

## External References

- ASP.NET Core hosted services: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0
- EF Core optimistic concurrency: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- EF Core querying: https://learn.microsoft.com/en-us/ef/core/querying/
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Send slot-swap notifications that list old and new appointment times after successful swaps
- [x] Reuse existing email and SMS integrations without duplicating provider-specific logic
- [x] Skip stale swap candidates that have since been cancelled and release the slot back to availability
- [x] Log stale-swap skip events for audit and troubleshooting
- [x] Record invalid-contact or delivery-failure outcomes without rolling back an already-completed swap
- [x] Persist per-channel slot-swap notification outcomes in `NotificationLog`