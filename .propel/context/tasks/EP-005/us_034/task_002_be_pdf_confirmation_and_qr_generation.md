# Task - task_002_be_pdf_confirmation_and_qr_generation

## Requirement Reference

- User Story: US_034
- Story Location: .propel/context/tasks/EP-005/us_034/us_034.md
- Acceptance Criteria:
    - AC-2: Given the confirmation is triggered, When the email is composed, Then it includes a PDF attachment with appointment details and a scannable QR code for check-in.
    - AC-1: Given a patient successfully books an appointment, When the booking is confirmed, Then the system sends a confirmation email and SMS within 30 seconds.
- Edge Case:
    - EC-1: If PDF generation fails, the system sends the email without PDF, logs generation failure, and queues a retry for PDF generation.
    - EC-2: If cancellation is processed before the confirmation artifact is finalized, the generated content must reflect cancelled state instead of stale scheduled status.

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
| Logging | Serilog | 8.x |
| API Documentation | Swagger (Swashbuckle) | 6.x |
| PDF Library | .NET-compatible PDF generator | N/A |
| QR Library | .NET-compatible QR code generator | N/A |
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

Implement the confirmation-document artifact pipeline for booking confirmations. This backend task generates a PDF containing appointment details plus a scannable QR code for check-in, returns it to the notification layer as an email attachment payload, and provides a retryable fallback path when artifact generation fails. The generated document must reflect the latest appointment state, including cancellations that overtake confirmation, and remain lightweight enough not to block the 30-second notification target.

## Dependent Tasks

- US_018 task_002_be_appointment_booking_api (Appointment details and booking reference must exist)
- US_019 task_002_be_appointment_cancellation_api (Cancellation state and workflow must exist)
- US_032 task_002_be_notification_email_composition_and_logging (Email orchestration will consume the generated attachment)

## Impacted Components

- **NEW** `IPdfConfirmationService` / `PdfConfirmationService` - Generate confirmation PDF bytes and attachment metadata (Server/Services/Notifications/)
- **NEW** `IQrCodeService` / `QrCodeService` - Produce scannable QR payloads for appointment check-in (Server/Services/Notifications/)
- **NEW** `PdfConfirmationResult` - Contract for generated PDF attachment or retryable failure outcome (Server/Models/DTOs/)
- **MODIFY** confirmation notification path - Consume PDF artifact and enqueue retry when generation fails (Server/Services/Notifications/)
- **MODIFY** operational background-work path - Add lightweight retry queue handling for deferred PDF generation (Server/Services/ or background worker integration)
- **MODIFY** `Program.cs` - Register PDF and QR generation services

## Implementation Plan

1. **Create a PDF confirmation service** that receives appointment context and returns attachment bytes plus filename and MIME metadata suitable for email delivery.
2. **Generate a QR code payload** that encodes appointment check-in context safely and can be embedded in the PDF confirmation document.
3. **Render appointment details into the PDF** including patient-facing appointment metadata, booking reference, and the QR code image.
4. **Re-read latest appointment status before final render** so the artifact can show a cancelled note when cancellation overtakes confirmation.
5. **Return retryable failure results** instead of throwing opaque exceptions so the notification layer can send email without the PDF and enqueue a deferred retry.
6. **Keep generation lightweight and deterministic** to preserve the overall 30-second confirmation-delivery target and avoid blocking notification dispatch longer than necessary.
7. **Register PDF and QR services** behind interfaces so later PDF preview and reminder attachment use cases can reuse the same pipeline.

## Current Project State

```text
Server/
  Services/
    Notifications/
  Models/
    Entities/
      Appointment.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Notifications/IPdfConfirmationService.cs | Interface for booking confirmation PDF generation |
| CREATE | Server/Services/Notifications/PdfConfirmationService.cs | Render appointment details and QR code into PDF attachment bytes |
| CREATE | Server/Services/Notifications/IQrCodeService.cs | Interface for QR payload generation |
| CREATE | Server/Services/Notifications/QrCodeService.cs | Generate scannable QR code payloads for check-in |
| CREATE | Server/Models/DTOs/PdfConfirmationResult.cs | Attachment result contract including retryable failure metadata |
| MODIFY | Server/Program.cs | Register PDF and QR generation services |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- .NET image processing guidance: https://learn.microsoft.com/en-us/dotnet/core/compatibility/
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Generate a PDF confirmation attachment with appointment details and booking reference
- [ ] Embed a scannable QR code for patient check-in in the generated PDF
- [ ] Reflect cancelled state in the artifact when cancellation overtakes confirmation generation
- [ ] Return retryable failure metadata so email can still be sent without the PDF
- [ ] Queue deferred PDF generation retries after attachment failures
- [ ] Keep artifact generation lightweight enough to support the 30-second confirmation target