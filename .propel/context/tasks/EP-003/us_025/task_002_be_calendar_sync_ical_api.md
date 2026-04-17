# Task - task_002_be_calendar_sync_ical_api

## Requirement Reference

- User Story: US_025
- Story Location: .propel/context/tasks/EP-003/us_025/us_025.md
- Acceptance Criteria:
    - AC-1: Given I have a confirmed appointment, When I click "Add to Calendar", Then the system generates an iCal (.ics) file with appointment details (date, time, provider, location).
    - AC-2: Given the iCal file is downloaded, When I open it, Then Google Calendar and Outlook correctly import the event with proper timezone handling.
    - AC-3: Given I reschedule an appointment, When a new iCal is generated, Then it includes the UID of the original event to update (not duplicate) the calendar entry.
- Edge Case:
    - EC-1: If a patient calendar client does not support iCal import, the server must still return a standards-compliant `.ics` file without requiring vendor-specific behavior.
    - EC-2: Timezone handling must include a `VTIMEZONE` definition using the clinic timezone while allowing the UI to display localized times independently.

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

Implement the backend calendar sync export flow for US_025. This task delivers FR-025 and TR-026 by exposing an authenticated endpoint that returns a standards-compliant iCalendar `.ics` file for a confirmed patient appointment, including appointment date and time, provider, location, and clinic timezone metadata. The service must preserve a stable event UID across reschedules so regenerated files update the same external calendar entry instead of duplicating it, enforce patient ownership over downloadable appointment data, and emit structured diagnostics without leaking PII.

## Dependent Tasks

- US_018 task_002_be_appointment_booking_api (Appointment creation and confirmation data must exist before generating calendar exports)
- US_023 task_002_be_appointment_rescheduling_api (Reschedule flow must preserve appointment identity so regenerated `.ics` files update existing calendar entries)
- US_008 - Foundational - Requires `Appointment` entity and persisted appointment status fields

## Impacted Components

- **NEW** `IAppointmentCalendarService` / `AppointmentCalendarService` - Service for building iCalendar payloads from appointment data (Server/Services/)
- **NEW** `AppointmentCalendarDownloadResponse` or equivalent internal result model - File payload metadata such as filename and MIME type (Server/Models/DTOs/)
- **MODIFY** `AppointmentBookingController` or dedicated appointments controller - Add authenticated download endpoint for `.ics` generation (Server/Controllers/)
- **MODIFY** `AppointmentBookingService` or shared appointment query path - Expose appointment lookup details needed for provider and location projection (Server/Services/)
- **MODIFY** `Program.cs` - Register calendar sync service and any related configuration dependencies

## Implementation Plan

1. **Add a calendar export service** that loads the requested appointment for the authenticated patient, gathers provider and location details, and maps them into an iCalendar event model.
2. **Generate a standards-compliant `.ics` payload** containing `VCALENDAR`, `VEVENT`, and `VTIMEZONE` components so Google Calendar and Outlook import the file with correct timezone behavior.
3. **Keep the event UID stable across reschedules** by deriving it from the persistent appointment identity and clinic domain rather than from appointment time, which ensures a regenerated export updates the original calendar event.
4. **Add an authenticated download endpoint** that returns `text/calendar` content with a deterministic filename, enforces patient ownership, and returns appropriate not-found or forbidden responses for unauthorized access attempts.
5. **Limit export eligibility appropriately** by allowing confirmed appointments while rejecting non-existent, inaccessible, or invalid appointment states without exposing other patients' data.
6. **Preserve interoperability** by emitting escaped text fields, UTC timestamps where required, clinic-timezone definitions, and import-safe event metadata for provider, location, and summary fields.
7. **Add structured logging and OpenAPI metadata** for calendar export attempts, ownership rejections, and reschedule-driven repeat downloads while redacting patient-identifying values in logs.

## Current Project State

```text
Server/
  Controllers/
    AppointmentBookingController.cs
    AppointmentSlotsController.cs
  Services/
    AppointmentBookingService.cs
    AppointmentCancellationService.cs
    AppointmentReschedulingService.cs
    AppointmentSlotCacheService.cs
    IAppointmentBookingService.cs
    IAppointmentCancellationService.cs
    IAppointmentReschedulingService.cs
  Models/
    DTOs/
      BookingRequest.cs
      BookingResponse.cs
      RescheduleAppointmentRequest.cs
      RescheduleAppointmentResponse.cs
    Entities/
      Appointment.cs
      NotificationLog.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IAppointmentCalendarService.cs | Interface for generating downloadable iCalendar payloads |
| CREATE | Server/Services/AppointmentCalendarService.cs | Builds `.ics` content with stable UID and clinic timezone metadata |
| CREATE | Server/Models/DTOs/AppointmentCalendarDownloadResponse.cs | Internal response model carrying filename, content type, and file bytes or stream contract |
| MODIFY | Server/Controllers/AppointmentBookingController.cs | Add authenticated GET endpoint for appointment calendar export |
| MODIFY | Server/Services/AppointmentBookingService.cs | Reuse or expose appointment lookup details required for calendar export generation |
| MODIFY | Server/Program.cs | Register calendar sync services and related configuration |

## External References

- RFC 5545 iCalendar specification: https://datatracker.ietf.org/doc/html/rfc5545
- ASP.NET Core FileResult documentation: https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/file-downloads?view=aspnetcore-8.0
- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Add a calendar export service that loads patient-owned appointment data and produces a downloadable `.ics` payload
- [ ] Emit `VCALENDAR`, `VEVENT`, and `VTIMEZONE` components with appointment date, time, provider, and location fields
- [ ] Keep the event UID stable across reschedules by deriving it from the persistent appointment identity rather than the appointment time
- [ ] Add an authenticated download endpoint returning `text/calendar` content with a deterministic filename
- [ ] Enforce patient ownership and reject inaccessible appointments without leaking cross-patient data
- [ ] Preserve Google Calendar and Outlook interoperability through standards-compliant formatting and escaped field values
- [ ] Add structured logs and OpenAPI docs for download attempts, failures, and ownership rejections