# Task - task_002_be_appointment_history_api

## Requirement Reference

- User Story: US_024
- Story Location: .propel/context/tasks/EP-003/us_024/us_024.md
- Acceptance Criteria:
    - AC-1: Given I navigate to appointment history, When the page loads, Then all my appointments are displayed in a paginated table sorted by date (newest first) with status badges (scheduled, completed, cancelled, no-show).
    - AC-2: Given the history table is displayed, When I click a column header (date), Then the table sorts by that column in ascending/descending order.
    - AC-3: Given I have 50+ appointments, When the table loads, Then pagination shows 10 items per page with Previous/Next navigation.
    - AC-4: Given appointments exist, When the data is fetched, Then each row shows date, time, provider, type, and status badge with consistent status colors per UXR-401.
- Edge Case:
    - EC-1: If the patient has no appointment history, the API must return an empty result set with pagination metadata rather than an error.
    - EC-2: Cancelled appointments must remain in history results with their `cancelled` status so the UI can render the grey badge and muted styling.

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

Implement the authenticated patient appointment history API for US_024. This backend task exposes a paginated history endpoint for FR-024 that returns the requesting patient's appointments in newest-first order by default, supports date sort toggling, includes status and provider summary fields required by SCR-007, and preserves cancelled and no-show records in the result set. The endpoint must enforce patient ownership, return stable pagination metadata for large histories, and align with NFR-004, NFR-011, NFR-017, and NFR-035 by keeping response times efficient, restricting access to the authenticated patient, redacting PII from logs, and emitting structured diagnostics.

## Dependent Tasks

- US_008 - Foundational - Requires `Appointment` entity with persisted status tracking
- US_018 task_002_be_appointment_booking_api (Appointment creation flow must exist so scheduled appointments populate history)
- US_019 task_002_be_appointment_cancellation_api (Cancelled appointments must remain queryable in patient history)
- US_023 task_002_be_appointment_rescheduling_api (Updated appointment records must appear accurately in history after reschedule)

## Impacted Components

- **NEW** `AppointmentHistoryQuery` - Query contract carrying page number, fixed page size, and date sort direction for patient history retrieval (Server/Models/DTOs/)
- **NEW** `AppointmentHistoryItemDto` / `AppointmentHistoryResponse` - DTOs for paginated appointment history rows and metadata (Server/Models/DTOs/)
- **NEW** `IAppointmentHistoryService` / `AppointmentHistoryService` - Authenticated patient history retrieval with pagination, projection, and sorting rules (Server/Services/)
- **MODIFY** `AppointmentBookingController` or dedicated appointments controller - Add GET endpoint for patient appointment history (Server/Controllers/)
- **MODIFY** `AppointmentSlotCacheService` or shared cache utilities - Add or reuse patient-scoped cache support for repeat history reads when appropriate (Server/Services/)
- **MODIFY** `Program.cs` - Register history services and endpoint dependencies

## Implementation Plan

1. **Add appointment history DTOs and query models** that carry page number, date sort direction, total count, total pages, and the row fields required by the UI: date, time, provider, appointment type, and status.
2. **Implement `AppointmentHistoryService`** to filter appointments by the authenticated patient, order by appointment date descending by default, apply ascending or descending date sort when requested, and project directly to lightweight DTOs.
3. **Keep pagination deterministic** by enforcing a page size of 10 for this story, validating page bounds, and returning stable metadata even when the requested page yields zero results. Treat the user story's 10-item requirement as authoritative over the generic large-data example in `figma_spec.md`.
4. **Add a patient-authenticated GET endpoint** for appointment history that binds the query model, sources the patient identifier from JWT claims instead of caller input, and returns 200 responses for both populated and empty history sets.
5. **Preserve all relevant statuses in results** so scheduled, completed, cancelled, and no-show appointments remain visible and can be rendered consistently by the frontend, including cancelled entries that should not be filtered out.
6. **Optimize response behavior** with efficient EF Core projection, appropriate ordering, and optional patient-scoped caching that avoids cross-user leakage while supporting the history page's repeated pagination and sort requests.
7. **Add structured logging and OpenAPI metadata** for history queries, invalid pagination inputs, and access-denied scenarios without logging raw provider or patient PII values unnecessarily.

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
    IAppointmentSlotService.cs
  Models/
    DTOs/
      BookingRequest.cs
      BookingResponse.cs
      CancelAppointmentRequest.cs
      CancelAppointmentResponse.cs
      RescheduleAppointmentRequest.cs
      RescheduleAppointmentResponse.cs
      SlotAvailabilityResponse.cs
      SlotQueryParameters.cs
    Entities/
      Appointment.cs
      NotificationLog.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Models/DTOs/AppointmentHistoryQuery.cs | Query model for page number and date sort direction |
| CREATE | Server/Models/DTOs/AppointmentHistoryItemDto.cs | Row projection for appointment history table data |
| CREATE | Server/Models/DTOs/AppointmentHistoryResponse.cs | Paginated response envelope with items and pagination metadata |
| CREATE | Server/Services/IAppointmentHistoryService.cs | Interface for patient-scoped appointment history retrieval |
| CREATE | Server/Services/AppointmentHistoryService.cs | EF Core-backed history query service with sorting and paging |
| MODIFY | Server/Controllers/AppointmentBookingController.cs | Add authenticated GET endpoint for patient appointment history |
| MODIFY | Server/Program.cs | Register history service and related dependencies |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- ASP.NET Core model binding: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-8.0
- EF Core query performance guidance: https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying
- EF Core projection with LINQ: https://learn.microsoft.com/en-us/ef/core/querying/
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Add query and response DTOs for patient history rows and pagination metadata with a fixed page size of 10
- [x] Implement patient-scoped history retrieval with newest-first default order and ascending or descending date sort support
- [x] Return scheduled, completed, cancelled, and no-show appointments without filtering cancelled history records out of the response
- [x] Add an authenticated GET endpoint that derives patient identity from JWT claims and returns empty-state payloads as 200 responses
- [x] Validate page bounds and return consistent total-count and total-page metadata for large histories
- [x] Optimize the query path with lightweight EF Core projection and patient-safe caching or equivalent reuse where appropriate
- [x] Add structured logs and OpenAPI documentation for history retrieval, invalid query parameters, and authorization failures