# Task - task_002_be_notification_sms_orchestration_and_logging

## Requirement Reference

- User Story: US_033
- Story Location: .propel/context/tasks/EP-005/us_033/us_033.md
- Acceptance Criteria:
    - AC-2: Given the patient opts out of SMS, When an SMS reminder is scheduled, Then the system skips SMS and logs "opted-out" status.
    - AC-3: Given SMS delivery fails, When the failure is detected, Then the system retries up to 3 times with exponential backoff and logs each attempt.
    - AC-4: Given delivery succeeds, When Twilio confirms delivery, Then status is logged as "sent" in NotificationLog.
    - AC-1: Given Twilio is configured, When an SMS is triggered, Then it is sent to the patient's phone number within 30 seconds.
- Edge Case:
    - EC-1: If Twilio trial credits are exhausted, emit an operational alert and continue with email-only notifications.
    - EC-2: If the patient has opted out, skip transport invocation and persist `opted-out` without counting it as a failed delivery.

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
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| Logging | Serilog | 8.x |
| API Documentation | Swagger (Swashbuckle) | 6.x |
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

Implement the notification-layer SMS orchestration that respects patient opt-out settings, invokes Twilio delivery, and persists delivery outcomes to `NotificationLog`. This backend task converts appointment notification events into SMS requests, skips delivery for opted-out patients, records `sent`, `failed`, or `opted-out` outcomes with retry history, and emits operational alerts when the SMS gateway is disabled. The orchestration should remain reusable for confirmation and reminder workflows across EP-005.

## Dependent Tasks

- task_001_be_twilio_provider_integration (Twilio transport and retry abstraction must exist)
- task_003_db_sms_opt_out_and_notification_status (Opt-out preference persistence and `NotificationLog` status support must exist)
- US_018 task_002_be_appointment_booking_api (Booking confirmation trigger source should be reused when wiring SMS confirmations)

## Impacted Components

- **NEW** `INotificationSmsService` / `NotificationSmsService` - SMS composition, transport invocation, and NotificationLog persistence (Server/Services/Notifications/)
- **NEW** `NotificationSmsRequest` - Contract describing appointment notification context for SMS composition (Server/Models/DTOs/)
- **MODIFY** notification orchestration path - Persist `sent`, `failed`, and `opted-out` outcomes with retry counts, timestamps, and provider metadata (Server/Services/)
- **MODIFY** patient preference integration path - Consult the patient SMS opt-out setting before invoking Twilio (Server/Services/ or patient profile integration path)
- **MODIFY** `Program.cs` - Register notification SMS orchestration services

## Implementation Plan

1. **Create a notification SMS service** that accepts appointment-context payloads, consults patient SMS preferences, calls the Twilio transport when allowed, and persists the final result.
2. **Skip SMS transport for opted-out patients** and record `opted-out` as a first-class delivery outcome instead of a failure.
3. **Persist SMS delivery outcomes into `NotificationLog`** with status, timestamp, retry count, delivery channel, and provider details.
4. **Capture retry attempt history** so Twilio retries and final results are logged consistently for reminders and confirmations.
5. **Honor gateway-disabled outcomes** by emitting an operational alert and allowing the surrounding notification workflow to continue in email-only mode.
6. **Keep the orchestration reusable** for booking confirmations, reminder jobs, and later waitlist or slot-swap SMS scenarios.

## Current Project State

```text
Server/
  Services/
    Notifications/
  Models/
    Entities/
      NotificationLog.cs
      Appointment.cs
      Patient.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Notifications/INotificationSmsService.cs | Interface for appointment-SMS delivery orchestration |
| CREATE | Server/Services/Notifications/NotificationSmsService.cs | Consult SMS preferences, invoke Twilio transport, and persist NotificationLog outcomes |
| CREATE | Server/Models/DTOs/NotificationSmsRequest.cs | Appointment-context payload for SMS composition and delivery |
| MODIFY | Server/Services/PatientService.cs | Expose or consult SMS opt-out preference during notification delivery |
| MODIFY | Server/Program.cs | Register notification SMS orchestration services |

## External References

- Twilio messaging API docs: https://www.twilio.com/docs/messaging/api
- EF Core saving related data: https://learn.microsoft.com/en-us/ef/core/saving/related-data
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [X] Reuse the Twilio transport layer to send reminder and notification SMS within the notification workflow
- [X] Skip SMS cleanly for opted-out patients and persist `opted-out` as a delivery outcome
- [X] Persist `sent`, `failed`, and `opted-out` SMS outcomes with timestamps and retry counts in `NotificationLog`
- [X] Continue in email-only mode when the SMS gateway is disabled or credits are exhausted
- [X] Consult the patient SMS preference before any transport invocation
- [X] Keep the SMS orchestration reusable for later EP-005 triggers beyond the initial reminder flow