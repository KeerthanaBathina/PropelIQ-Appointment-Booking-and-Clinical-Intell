# Task - task_002_be_minor_guardian_and_insurance_precheck_api

## Requirement Reference

- User Story: US_031
- Story Location: .propel/context/tasks/EP-004/us_031/us_031.md
- Acceptance Criteria:
    - AC-1: Given a patient's DOB indicates they are under 18, When they attempt to book, Then the system requires guardian consent acknowledgment before proceeding.
    - AC-2: Given insurance details are provided during intake, When the soft pre-check runs, Then the system validates against dummy records and displays "Valid" or "Needs Review" status.
    - AC-3: Given insurance validation fails, When the failure is detected, Then the system flags the record for staff review and sends a notification to the staff dashboard.
    - AC-4: Given the insurance check runs, When it completes, Then the result is displayed inline to the patient with a clear explanation if review is needed.
- Edge Case:
    - EC-1: If both patient and guardian are minors, reject the guardian consent attempt with a deterministic validation error.
    - EC-2: If insurance information is missing, skip the pre-check, preserve the intake, and flag the record for staff collection during the visit.

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

Implement the deterministic backend rules for minor booking restriction and dummy insurance pre-check. This task enforces FR-032 at booking time by rejecting minor bookings that lack valid guardian consent, runs the FR-033 soft insurance validation against dummy records during intake updates, and fulfills FR-034 by flagging failed or missing insurance states for staff review. The service layer must return patient-safe inline status metadata, keep booking endpoints idempotent, and emit a review notification payload that the future staff dashboard can consume.

## Dependent Tasks

- US_028 task_002_be_manual_intake_api (Manual intake save and submit APIs must exist)
- US_018 task_002_be_appointment_booking_api (Patient booking confirmation flow must exist for minor-age enforcement)
- task_003_db_minor_guardian_and_insurance_persistence (Guardian consent and insurance validation persistence must exist)
- US_008 - Foundational - `Patient` and `IntakeData` entities must exist
- US_034 - Cross-Epic - Notification delivery integration should be reused when available

## Impacted Components

- **NEW** `IInsurancePrecheckService` / `InsurancePrecheckService` - Dummy-record insurance validation and status mapping (Server/Services/)
- **MODIFY** `ManualIntakeService` - Trigger insurance pre-check, return inline status metadata, and flag missing or failed insurance for staff follow-up (Server/Services/)
- **MODIFY** `AppointmentBookingService` - Enforce guardian-consent requirement for minor patients before confirming a booking (Server/Services/)
- **NEW** `InsurancePrecheckResultDto` - Status contract for "Valid", "Needs Review", and skipped-precheck explanations (Server/Models/DTOs/)
- **MODIFY** `ManualIntakeDraftResponse` / submit response DTOs - Include guardian consent and insurance validation status metadata (Server/Models/DTOs/)
- **MODIFY** notification integration path - Emit staff-review event or notification record when insurance needs review or collection (Server/Services/ or integration layer)
- **MODIFY** `Program.cs` - Register insurance pre-check services and dependencies

## Implementation Plan

1. **Add a dedicated insurance pre-check service** that validates provided insurance data against dummy records and maps results into patient-safe status responses.
2. **Trigger the pre-check from manual intake save or submit flows** when insurance fields are present, and skip it with explicit "collect during visit" status when those fields are absent.
3. **Persist and return inline result metadata** so the patient UI can show "Valid" or "Needs Review" plus clear explanatory copy.
4. **Enforce minor booking restriction in the booking service** by checking patient DOB, guardian consent acknowledgment, and guardian age before allowing final appointment creation.
5. **Reject invalid guardian scenarios deterministically** when the guardian is also under 18 or required guardian details are incomplete.
6. **Flag staff review needs** for failed or skipped insurance checks by creating a notification or review record consumable by the staff dashboard later.
7. **Add structured logging and OpenAPI metadata** for guardian-consent failures, insurance validation outcomes, and staff-review flag creation.

## Current Project State

```text
Server/
  Controllers/
    ManualIntakeController.cs
    AppointmentBookingController.cs
  Services/
    ManualIntakeService.cs
    AppointmentBookingService.cs
  Models/
    DTOs/
      ManualIntakeDraftResponse.cs
      SubmitManualIntakeRequest.cs
      BookingRequest.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IInsurancePrecheckService.cs | Interface for dummy insurance validation and status mapping |
| CREATE | Server/Services/InsurancePrecheckService.cs | Dummy-record insurance validation and review-status orchestration |
| CREATE | Server/Models/DTOs/InsurancePrecheckResultDto.cs | Inline patient-facing insurance validation result contract |
| MODIFY | Server/Services/ManualIntakeService.cs | Trigger pre-check and flag failed or missing insurance for staff follow-up |
| MODIFY | Server/Services/AppointmentBookingService.cs | Enforce guardian consent for minor patient bookings |
| MODIFY | Server/Models/DTOs/ManualIntakeDraftResponse.cs | Return guardian consent and insurance validation metadata |
| MODIFY | Server/Models/DTOs/SubmitManualIntakeRequest.cs | Accept guardian consent fields required for minor patients |
| MODIFY | Server/Program.cs | Register guardian-consent and insurance pre-check services |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- ASP.NET Core model validation: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-8.0
- ASP.NET Core idempotency guidance: https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotent-message-processing
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Validate insurance against dummy records and return "Valid" or "Needs Review" with explanation text
- [x] Skip insurance pre-check cleanly when insurance information is absent and mark for staff collection
- [x] Enforce guardian consent and guardian age >= 18 for minor patient bookings
- [x] Reject invalid guardian scenarios with deterministic field-level validation errors
- [x] Flag failed or missing insurance states for staff review notification
- [x] Return guardian and insurance status metadata needed by the patient UI and booking flow
- [x] Add structured logging and OpenAPI metadata for validation and review outcomes