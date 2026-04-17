# Task - task_004_be_no_show_risk_integration_api

## Requirement Reference

- User Story: US_026
- Story Location: .propel/context/tasks/EP-003/us_026/us_026.md
- Acceptance Criteria:
    - AC-1: Given an appointment exists, When the risk score is calculated, Then it produces a score from 0-100 based on patient history (past no-shows, cancellations) and appointment characteristics (time of day, day of week).
    - AC-2: Given risk scores are available, When staff views the appointment list, Then each appointment displays its risk score with color coding (green <30, amber 30-69, red >=70).
    - AC-3: Given insufficient historical data (new patient, <3 appointments), When the risk score is calculated, Then the system uses rule-based defaults and displays "Estimated" label.
    - AC-4: Given risk scores are computed, When the system evaluates slot swap priority, Then lower no-show risk patients are prioritized for preferred slots.
- Edge Case:
    - EC-1: After a completed visit changes patient history, the next appointment creation or refresh flow must recalculate or refresh the stored score.
    - EC-2: Appointments with capped score 100 must trigger a staff outreach indicator while remaining compatible with queue and schedule APIs.

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
| AI/ML | In-process classification model | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | Custom .NET Service with Polly | 8.x |
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

Implement the deterministic backend orchestration and API exposure for no-show risk scores. This task integrates the scoring engine from AIR-006 into appointment workflows by calculating or refreshing no-show risk metadata when appointments are created or otherwise prepared for staff views, exposing the score to schedule and queue endpoints, and feeding the preferred-slot swap prioritization path from US_021. The backend flow must keep staff endpoints efficient under NFR-005 load, enforce staff-only access to operational views, and emit structured, PII-safe diagnostics.

## Dependent Tasks

- task_002_ai_no_show_risk_scoring (Scoring engine and fallback rules must exist)
- task_003_db_no_show_risk_persistence (Persisted score metadata fields must exist)
- US_018 task_002_be_appointment_booking_api (Appointment creation flow must invoke score generation for new appointments)
- US_021 task_001_be_dynamic_slot_swap_engine (Consumes risk score ordering for preferred-slot prioritization)
- US_053 - Cross-Epic - Queue API surface must expose risk metadata for SCR-011
- US_057 - Cross-Epic - Staff dashboard API surface must expose risk metadata for SCR-010

## Impacted Components

- **NEW** `NoShowRiskOrchestrator` or equivalent integration service - Coordinates score calculation, persistence updates, and downstream consumers (Server/Services/)
- **NEW** `StaffAppointmentRiskDto` or equivalent shared projection - Carries score, band, estimated flag, and outreach indicator to staff endpoints (Server/Models/DTOs/)
- **MODIFY** `AppointmentBookingService` - Trigger score generation for new or refreshed appointments (Server/Services/)
- **MODIFY** staff dashboard and queue query services/controllers - Include no-show risk metadata in appointment list responses (Server/Controllers/ or Server/Services/)
- **MODIFY** `PreferredSlotSwapService` - Consume persisted no-show risk score when ranking candidates (Server/Services/)
- **MODIFY** `Program.cs` - Register orchestration and integration services

## Implementation Plan

1. **Add orchestration around score generation** so appointment workflows can request scoring, receive fallback-safe results, and persist the latest metadata on the appointment record.
2. **Integrate scoring into appointment lifecycle paths** by calculating or refreshing the risk score on new appointment creation and on subsequent refresh points when staff operational views need current data.
3. **Expose score metadata to staff-facing endpoints** for SCR-010 and SCR-011, including numeric score, risk band, estimated flag, and outreach-needed status.
4. **Reuse persisted score data in preferred-slot ranking** so lower-risk patients are prioritized consistently by the US_021 swap engine without recomputing scores inside every ranking pass.
5. **Trigger high-risk outreach signals** by surfacing a deterministic indicator for capped or red-band appointments that staff workflows can act on without coupling outreach delivery into this task.
6. **Protect performance and access control** by caching or projecting risk metadata efficiently for staff list endpoints and keeping patient callers out of staff-only responses.
7. **Add structured logs and OpenAPI metadata** for score refreshes, fallback-scored appointments, ranking inputs, and staff list retrievals without logging raw patient history features.

## Current Project State

```text
Server/
  Controllers/
    AppointmentBookingController.cs
    AppointmentSlotsController.cs
  Services/
    AppointmentBookingService.cs
    PreferredSlotSwapService.cs
    AppointmentSlotCacheService.cs
  Models/
    DTOs/
      BookingRequest.cs
      BookingResponse.cs
    Entities/
      Appointment.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/NoShowRiskOrchestrator.cs | Coordinates score calculation, persistence updates, and downstream integration points |
| CREATE | Server/Models/DTOs/StaffAppointmentRiskDto.cs | Shared projection for staff-facing risk score metadata |
| MODIFY | Server/Services/AppointmentBookingService.cs | Invoke no-show risk scoring and persistence during appointment workflows |
| MODIFY | Server/Services/PreferredSlotSwapService.cs | Use persisted no-show risk score for candidate prioritization |
| MODIFY | Server/Controllers/StaffDashboardController.cs | Expose risk score metadata for SCR-010 schedule rows |
| MODIFY | Server/Controllers/ArrivalQueueController.cs | Expose risk score metadata for SCR-011 queue rows |
| MODIFY | Server/Program.cs | Register no-show risk orchestration services and dependencies |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- EF Core querying: https://learn.microsoft.com/en-us/ef/core/querying/
- ASP.NET Core distributed caching: https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Add orchestration that invokes no-show risk scoring and persists the latest metadata on appointments
- [ ] Refresh risk metadata during appointment creation and other staff-facing refresh points without blocking core workflows on scoring failure
- [ ] Expose score, band, estimated-state, and outreach-needed metadata through staff schedule and queue APIs
- [ ] Feed persisted risk scores into preferred-slot swap ranking so lower-risk patients are prioritized deterministically
- [ ] Surface a deterministic outreach indicator for highest-risk appointments without embedding notification delivery into this task
- [ ] Keep staff-facing list queries efficient and protected by staff-only authorization
- [ ] Add structured logs and API documentation for score refreshes, fallback paths, and staff list retrieval