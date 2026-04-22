# Task - task_001_be_booking_confirmation_notification_orchestration

## Requirement Reference

- User Story: US_034
- Story Location: .propel/context/tasks/EP-005/us_034/us_034.md
- Acceptance Criteria:
    - AC-1: Given a patient successfully books an appointment, When the booking is confirmed, Then the system sends a confirmation email and SMS within 30 seconds.
    - AC-3: Given a booking confirmation is sent, When the patient views the email, Then it contains a cancellation link that pre-fills the cancellation workflow.
    - AC-4: Given the patient has opted out of SMS, When a booking confirmation is triggered, Then only email (with PDF) is sent and SMS is skipped with status logged as "opted-out."
- Edge Case:
    - EC-1: If PDF generation fails, still send the confirmation email without the PDF, log the generation failure, and queue a retry for PDF generation.
    - EC-2: If booking and cancellation occur within seconds of each other, cancellation takes priority and any confirmation content sent after cancellation must include a cancelled note.

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
| Caching / Queue | Upstash Redis | 7.x |
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| Logging | Serilog | 8.x |
| API Documentation | Swagger (Swashbuckle) | 6.x |
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

Implement the booking-confirmation notification orchestration that runs immediately after successful appointment creation. This backend task coordinates the booking service, email service, SMS service, cancellation-link generation, and final notification status logging so patients receive confirmation within 30 seconds. The orchestration must respect SMS opt-out settings, attach or defer the PDF artifact depending on generation success, and re-check appointment state before send so cancellations that race with confirmation take precedence in the patient-facing content.

## Dependent Tasks

- US_032 task_001_be_smtp_provider_integration (SMTP transport must exist)
- US_032 task_002_be_notification_email_composition_and_logging (Email composition and NotificationLog persistence must exist)
- US_033 task_001_be_twilio_provider_integration (Twilio transport must exist)
- US_033 task_002_be_notification_sms_orchestration_and_logging (SMS orchestration and opt-out handling must exist)
- US_018 task_002_be_appointment_booking_api (Booking success trigger source must exist)
- US_019 task_002_be_appointment_cancellation_api (Cancellation workflow and prefilled cancellation route must exist)
- task_002_be_pdf_confirmation_and_qr_generation (PDF and QR artifact generation contract must exist)

## Impacted Components

- **NEW** `IBookingConfirmationNotificationService` / `BookingConfirmationNotificationService` - Coordinates immediate confirmation email and SMS after booking (Server/Services/Notifications/)
- **NEW** `BookingConfirmationRequest` - Contract describing appointment confirmation payload and cancellation-link inputs (Server/Models/DTOs/)
- **MODIFY** `AppointmentBookingService` - Trigger confirmation orchestration after a successful booking commit (Server/Services/)
- **MODIFY** email notification path - Append cancellation link and cancelled-note handling to confirmation emails (Server/Services/Notifications/)
- **MODIFY** SMS notification path - Send or skip SMS confirmation based on patient opt-out status (Server/Services/Notifications/)
- **MODIFY** `Program.cs` - Register booking confirmation orchestration service

## Implementation Plan

1. **Create a booking-confirmation orchestration service** that is invoked after booking success and coordinates downstream email, SMS, and PDF services.
2. **Generate a prefilled cancellation link** that reuses the cancellation workflow contract and includes the appointment context needed to launch the patient cancellation path directly.
3. **Invoke email and SMS delivery in the same confirmation workflow** while respecting SMS opt-out status and persisting `opted-out` when SMS is intentionally skipped.
4. **Re-check appointment state before composing content** so if a cancellation has already been processed, the confirmation message includes a cancelled note instead of stale scheduled-only language.
5. **Handle PDF-generation dependency gracefully** by sending the email without attachment when PDF creation fails, logging that failure, and enqueueing a retry request for attachment generation.
6. **Persist final delivery outcomes and correlation data** so email, SMS, skipped-SMS, and fallback attachment states remain auditable across NotificationLog and operational logs.
7. **Keep the orchestration reusable** so future reminder and notification triggers can adopt the same appointment-context and link-generation patterns.

## Current Project State

```text
Server/
  Services/
    AppointmentBookingService.cs
    Notifications/
      NotificationEmailService.cs
      NotificationSmsService.cs
  Models/
    Entities/
      Appointment.cs
      NotificationLog.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Notifications/IBookingConfirmationNotificationService.cs | Interface for immediate booking confirmation orchestration |
| CREATE | Server/Services/Notifications/BookingConfirmationNotificationService.cs | Coordinate email, SMS, cancellation link, and PDF retry behavior |
| CREATE | Server/Models/DTOs/BookingConfirmationRequest.cs | Appointment-context payload for booking confirmation notifications |
| MODIFY | Server/Services/AppointmentBookingService.cs | Trigger confirmation orchestration after booking success |
| MODIFY | Server/Program.cs | Register booking confirmation orchestration service |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- EF Core transactions: https://learn.microsoft.com/en-us/ef/core/saving/transactions
- ASP.NET Core distributed caching: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [X] Trigger email and SMS confirmation delivery immediately after successful booking
- [X] Include a prefilled cancellation link in booking confirmation email content
- [X] Skip SMS for opted-out patients and persist `opted-out` without treating it as failure
- [X] Include a cancelled note when cancellation wins a near-simultaneous booking or confirmation race
- [X] Send confirmation email without PDF when attachment generation fails and queue the PDF retry path
- [X] Persist auditable delivery outcomes and correlation data for both channels