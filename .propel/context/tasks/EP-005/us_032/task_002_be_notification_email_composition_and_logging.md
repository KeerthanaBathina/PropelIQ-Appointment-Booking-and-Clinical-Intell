# Task - task_002_be_notification_email_composition_and_logging

## Requirement Reference

- User Story: US_032
- Story Location: .propel/context/tasks/EP-005/us_032/us_032.md
- Acceptance Criteria:
    - AC-2: Given email delivery is attempted, When the send succeeds, Then delivery status is logged as "sent" with timestamp in NotificationLog.
    - AC-4: Given an email template is used, When the email is composed, Then variable placeholders (patient name, date, time, provider) are replaced with actual values.
    - AC-3: Given the SMTP provider is unreachable, When delivery fails, Then the system retries up to 3 times with exponential backoff (1s, 2s, 4s) and logs each attempt.
- Edge Case:
    - EC-1: If SendGrid quota exhaustion triggers Gmail fallback, persist the final provider used and notify admin through the existing operational alert path.
    - EC-2: If recipient email is invalid, record a "bounced" NotificationLog status and flag the patient record for contact update.

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
| Email Service | SMTP (SendGrid Free Tier or Gmail SMTP) | N/A |
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

Implement the notification-layer email orchestration that composes templates, invokes SMTP delivery, and persists delivery outcomes to `NotificationLog`. This backend task turns appointment events into email payloads by replacing placeholders with patient and appointment values, records sent or failed outcomes with timestamps and retry counts, marks invalid-recipient failures as bounced, and emits operational/admin alerts when provider fallback is engaged. The orchestration should remain reusable for confirmation and reminder workflows across EP-005.

## Dependent Tasks

- task_001_be_smtp_provider_integration (SMTP transport and failover abstraction must exist)
- US_008 - Foundational - `NotificationLog` entity must exist
- US_018 task_002_be_appointment_booking_api (Booking confirmation trigger source should be reused when wiring confirmation emails)

## Impacted Components

- **NEW** `INotificationEmailService` / `NotificationEmailService` - Template composition, transport invocation, and NotificationLog persistence (Server/Services/Notifications/)
- **NEW** `EmailTemplateRenderer` - Placeholder substitution for patient name, date, time, and provider variables (Server/Services/Notifications/)
- **NEW** `NotificationEmailRequest` - Contract describing appointment notification context for email composition (Server/Models/DTOs/)
- **MODIFY** `NotificationLog` integration path - Persist sent, failed, and bounced outcomes with retry counts, timestamps, and provider details (Server/Services/)
- **MODIFY** patient contact-quality workflow - Flag patient record for contact update when email bounces (Server/Services/ or patient profile integration path)
- **MODIFY** `Program.cs` - Register email orchestration services

## Implementation Plan

1. **Create a notification email service** that accepts appointment-context payloads, renders templates, calls the SMTP transport, and persists the final delivery result.
2. **Implement template rendering** for confirmation and reminder-style email bodies using placeholder substitution for patient name, appointment date, appointment time, and provider.
3. **Persist delivery outcomes into `NotificationLog`** with sent, failed, or bounced status, timestamp, retry count, and the final provider used after any failover.
4. **Capture attempt history for retries** so each retry and final outcome can be logged consistently even when the transport layer changes providers.
5. **Handle invalid-recipient outcomes explicitly** by recording `bounced` and triggering a patient contact-update flag for later staff follow-up.
6. **Emit operational/admin alert events** when primary-provider quota exhaustion forces fallback to Gmail so the free-tier limit is visible to administrators.
7. **Keep the orchestration reusable** for downstream confirmation and reminder workflows by avoiding hard-coded appointment-trigger logic in the transport layer.

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
| CREATE | Server/Services/Notifications/INotificationEmailService.cs | Interface for appointment-email composition and delivery orchestration |
| CREATE | Server/Services/Notifications/NotificationEmailService.cs | Compose emails, invoke SMTP transport, and persist NotificationLog outcomes |
| CREATE | Server/Services/Notifications/EmailTemplateRenderer.cs | Replace email template placeholders with actual appointment values |
| CREATE | Server/Models/DTOs/NotificationEmailRequest.cs | Appointment-context payload for notification email composition |
| MODIFY | Server/Services/PatientService.cs | Flag patient contact details for update after bounced email outcomes |
| MODIFY | Server/Program.cs | Register notification email orchestration services |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- EF Core saving related data: https://learn.microsoft.com/en-us/ef/core/saving/related-data
- Serilog structured logging: https://serilog.net/
- Microsoft templating guidance (general): https://learn.microsoft.com/en-us/dotnet/standard/base-types/composite-formatting

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [X] Compose appointment email content by replacing placeholders with patient and appointment values
- [X] Reuse the SMTP transport layer to send confirmation or reminder emails within the notification workflow
- [X] Persist `sent`, `failed`, and `bounced` delivery outcomes with timestamps and retry counts in `NotificationLog`
- [X] Record the final provider used after fallback so delivery history is auditable
- [X] Flag patient contact details for staff follow-up when recipient email bounces
- [X] Emit an admin or operational alert when SendGrid quota exhaustion forces Gmail fallback
- [X] Keep the email orchestration reusable for later reminder and confirmation triggers across EP-005