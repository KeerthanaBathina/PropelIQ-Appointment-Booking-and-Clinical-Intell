# Task - task_002_be_reminder_notification_dispatch_and_skip_handling

## Requirement Reference

- User Story: US_035
- Story Location: .propel/context/tasks/EP-005/us_035/us_035.md
- Acceptance Criteria:
    - AC-1: Given an appointment is scheduled for tomorrow, When the 24-hour reminder batch job runs, Then the system sends a personalized email and SMS with appointment details and cancellation link.
    - AC-2: Given an appointment is scheduled in 2 hours, When the 2-hour reminder batch job runs, Then the system sends an SMS-only reminder to the patient.
    - AC-3: Given a patient has opted out of SMS, When the 24-hour reminder job runs, Then only the email reminder is sent and SMS is skipped with "opted-out" logged.
    - AC-5: Given an appointment is cancelled before the reminder window, When the batch job processes it, Then the reminder is skipped and status logged as "cancelled-before-send."
- Edge Case:
    - EC-1: If the reminder job fails mid-batch, the next run resumes using persistent checkpoint tracking while preserving per-appointment delivery outcomes already written.
    - EC-2: Cancellation links in reminder communications must continue to use the existing cancellation workflow contract.

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

Implement reminder-specific notification dispatch for the 24-hour and 2-hour flows. This backend task reuses the existing email and SMS transports but adds reminder payload composition, channel selection by reminder type, cancellation-link inclusion, SMS opt-out handling, and explicit `cancelled-before-send` logging when appointments are no longer eligible by send time. The logic must keep reminder dispatch idempotent enough for checkpoint resume, avoid double-sends across reruns, and preserve per-channel auditability in `NotificationLog`.

## Dependent Tasks

- US_032 task_002_be_notification_email_composition_and_logging (Email orchestration and NotificationLog persistence must exist)
- US_033 task_002_be_notification_sms_orchestration_and_logging (SMS orchestration and opt-out handling must exist)
- US_034 task_001_be_booking_confirmation_notification_orchestration (Cancellation-link generation and notification orchestration patterns should be reused)
- US_019 task_002_be_appointment_cancellation_api (Cancellation workflow contract must exist)
- task_003_db_reminder_checkpoint_and_notification_status_support (Reminder status and checkpoint persistence support must exist)

## Impacted Components

- **NEW** `IReminderNotificationService` / `ReminderNotificationService` - Reminder-specific composition and dispatch orchestration for email and SMS (Server/Services/Notifications/)
- **NEW** `ReminderNotificationRequest` - Contract describing reminder type, appointment context, and reminder-window metadata (Server/Models/DTOs/)
- **MODIFY** email notification path - Support reminder template composition with appointment details and cancellation link (Server/Services/Notifications/)
- **MODIFY** SMS notification path - Support 24-hour and 2-hour reminder messaging while preserving opt-out and retry behavior (Server/Services/Notifications/)
- **MODIFY** `NotificationLog` integration path - Persist reminder-specific skip and delivery outcomes, including `cancelled-before-send` (Server/Services/)
- **MODIFY** `Program.cs` - Register reminder notification orchestration service

## Implementation Plan

1. **Create a reminder notification service** that accepts reminder batch work items and routes them to email plus SMS for 24-hour reminders and SMS-only for 2-hour reminders.
2. **Compose personalized reminder payloads** with appointment date, time, provider details, and the existing cancellation link contract for every communication that supports cancellation.
3. **Re-check appointment eligibility immediately before dispatch** so reminders are skipped and logged as `cancelled-before-send` when the appointment was cancelled after batch selection but before channel send.
4. **Honor SMS opt-out rules** by sending email-only for 24-hour reminders when the patient has opted out and recording `opted-out` without treating it as a failure.
5. **Avoid duplicate sends across checkpoint resumes** by consulting reminder delivery history before dispatching a reminder channel that has already completed for the same appointment and reminder window.
6. **Persist channel-level outcomes consistently** across `sent`, `failed`, `opted-out`, and `cancelled-before-send` statuses so reminder audits remain queryable.
7. **Keep the service reusable** for later waitlist and swap reminders by separating reminder-specific composition from the underlying provider integrations.

## Current Project State

```text
Server/
  Services/
    Notifications/
      NotificationEmailService.cs
      NotificationSmsService.cs
      BookingConfirmationNotificationService.cs
  Models/
    Entities/
      Appointment.cs
      NotificationLog.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Notifications/IReminderNotificationService.cs | Interface for reminder-specific email and SMS dispatch orchestration |
| CREATE | Server/Services/Notifications/ReminderNotificationService.cs | Compose and send 24-hour and 2-hour reminders with skip handling |
| CREATE | Server/Models/DTOs/ReminderNotificationRequest.cs | Reminder dispatch payload with appointment context and reminder type |
| MODIFY | Server/Services/Notifications/NotificationEmailService.cs | Add reminder-template rendering and cancellation-link support reuse |
| MODIFY | Server/Services/Notifications/NotificationSmsService.cs | Add reminder-specific SMS message composition and duplicate-send checks |
| MODIFY | Server/Program.cs | Register reminder notification orchestration service |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- EF Core querying: https://learn.microsoft.com/en-us/ef/core/querying/
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Send personalized email plus SMS for 24-hour reminders and SMS-only for 2-hour reminders
- [x] Include the existing cancellation-link workflow in reminder communications that support cancellation
- [x] Skip cancelled appointments at send time and log `cancelled-before-send`
- [x] Respect SMS opt-out by sending email-only for 24-hour reminders and logging `opted-out`
- [x] Prevent duplicate reminder sends when a checkpointed batch resumes
- [x] Persist per-channel reminder outcomes consistently in `NotificationLog`