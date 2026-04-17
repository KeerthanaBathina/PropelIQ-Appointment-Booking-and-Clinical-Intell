# Task - TASK_002

## Requirement Reference

- User Story: US_059
- Story Location: .propel/context/tasks/EP-010/us_059/us_059.md
- Acceptance Criteria:
  - AC-1: Given the admin opens slot template configuration, When they select a provider and day, Then the system displays the current slot template with configurable time blocks and appointment types.
  - AC-2: Given the admin modifies a slot template, When they save changes, Then the template applies to all future appointments for that provider/day combination without affecting existing bookings.
  - AC-3: Given the admin opens business hours configuration, When they set hours for each day of the week, Then the system enforces these hours in the appointment booking interface.
  - AC-4: Given the admin adds a holiday, When the holiday date is saved, Then no appointment slots are available for that date and existing bookings for the date are flagged for staff review.
- Edge Case:
  - What happens when a template change conflicts with existing appointments? System shows a list of affected appointments and requires admin confirmation before applying.
  - How does the system handle recurring holidays (e.g., Christmas)? Admin can set annual recurring holidays that automatically block slots each year.

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
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |

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

Implement the backend API endpoints and service layer for managing appointment slot templates, business hours, and holiday schedules. This includes CRUD operations for slot templates by provider/day, business hours per day of week, and holidays (including recurring). The service layer enforces business rules: template changes apply only to future appointments, holiday creation flags existing bookings for review, and configuration changes are audit-logged. Slot template and business hours data are cached in Redis with 5-minute TTL for booking interface performance.

## Dependent Tasks

- task_001_db_slot_template_business_hours_schema — Requires entity models and database schema
- US_058 — Requires admin dashboard API framework and authentication middleware
- US_008 — Requires Appointment entity and existing booking query patterns

## Impacted Components

- **NEW** — `SlotTemplateController` — API controller for slot template CRUD
- **NEW** — `BusinessHoursController` — API controller for business hours and holiday management
- **NEW** — `ISlotTemplateService` / `SlotTemplateService` — Business logic for slot template operations
- **NEW** — `IBusinessHoursService` / `BusinessHoursService` — Business logic for hours and holiday operations
- **NEW** — `SlotTemplateDto`, `SlotTemplateBlockDto` — Request/response DTOs
- **NEW** — `BusinessHoursDto`, `HolidayDto` — Request/response DTOs
- **NEW** — `SlotTemplateValidator`, `BusinessHoursValidator`, `HolidayValidator` — FluentValidation validators
- **MODIFY** — Audit service — Log configuration changes with admin attribution

## Implementation Plan

1. Create DTOs for slot template CRUD (CreateSlotTemplateRequest, UpdateSlotTemplateRequest, SlotTemplateResponse with nested blocks)
2. Create DTOs for business hours (UpdateBusinessHoursRequest per day, BusinessHoursResponse) and holidays (CreateHolidayRequest, HolidayResponse)
3. Implement FluentValidation validators: validate time ranges (start < end), day_of_week enum, non-overlapping blocks, holiday date not in the past
4. Implement `SlotTemplateService` with methods: GetByProviderAndDay, CreateOrUpdate (with optimistic locking), GetAffectedAppointments (for conflict detection), ApplyTemplate (marks future-only)
5. Implement `BusinessHoursService` with methods: GetAll, UpdateByDay, GetHolidays, AddHoliday (flags existing bookings), RemoveHoliday (soft delete), GetRecurringHolidays
6. Implement `SlotTemplateController` with endpoints: GET/PUT `/api/admin/config/slots/{providerId}/{dayOfWeek}`, GET `/api/admin/config/slots/{providerId}` (all days)
7. Implement `BusinessHoursController` with endpoints: GET/PUT `/api/admin/config/hours`, GET/POST/DELETE `/api/admin/config/holidays`
8. Integrate Redis caching (5-min TTL) for slot templates and business hours; invalidate on update

## Current Project State

- Placeholder — to be updated based on completion of dependent tasks (task_001, US_058).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/SlotTemplateController.cs | API endpoints: GET/PUT slot templates by provider and day |
| CREATE | Server/Controllers/BusinessHoursController.cs | API endpoints: GET/PUT business hours, GET/POST/DELETE holidays |
| CREATE | Server/Services/ISlotTemplateService.cs | Interface for slot template business logic |
| CREATE | Server/Services/SlotTemplateService.cs | Slot template CRUD, conflict detection, optimistic locking, cache invalidation |
| CREATE | Server/Services/IBusinessHoursService.cs | Interface for business hours and holiday logic |
| CREATE | Server/Services/BusinessHoursService.cs | Business hours CRUD, holiday management, booking flag logic |
| CREATE | Server/DTOs/SlotTemplateDto.cs | Request/response DTOs for slot template endpoints |
| CREATE | Server/DTOs/BusinessHoursDto.cs | Request/response DTOs for business hours and holiday endpoints |
| CREATE | Server/Validators/SlotTemplateValidator.cs | FluentValidation rules for slot template requests |
| CREATE | Server/Validators/BusinessHoursValidator.cs | FluentValidation rules for business hours and holiday requests |
| MODIFY | Server/Program.cs | Register new services and validators in DI container |

## External References

- [ASP.NET Core 8 — Controller-based APIs](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [EF Core 8 — Optimistic Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [FluentValidation — ASP.NET Core Integration](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [StackExchange.Redis — Caching Patterns](https://stackexchange.github.io/StackExchange.Redis/)
- [ASP.NET Core 8 — Response Caching](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0)

## Build Commands

- `dotnet build Server`
- `dotnet test Server.Tests`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] GET `/api/admin/config/slots/{providerId}/{dayOfWeek}` returns 200 with slot template data
- [ ] PUT `/api/admin/config/slots/{providerId}/{dayOfWeek}` returns 200 on valid update, 409 on concurrency conflict
- [ ] GET `/api/admin/config/hours` returns 200 with all 7 days
- [ ] PUT `/api/admin/config/hours` returns 200 on valid update, 422 on validation failure
- [ ] POST `/api/admin/config/holidays` returns 201 and flags affected bookings
- [ ] DELETE `/api/admin/config/holidays/{id}` performs soft delete and returns 204
- [ ] Redis cache invalidated on template/hours update

## Implementation Checklist

- [ ] Create `SlotTemplateDto.cs` with CreateSlotTemplateRequest (providerId, dayOfWeek, blocks[]), UpdateSlotTemplateRequest (blocks[], version), SlotTemplateResponse, SlotTemplateBlockDto (startTime, endTime, appointmentType, isAvailable)
- [ ] Create `BusinessHoursDto.cs` with UpdateBusinessHoursRequest (entries[]), BusinessHoursEntryDto (dayOfWeek, openTime, closeTime, isClosed), HolidayDto (date, name, isRecurring, isHalfDay), CreateHolidayRequest, HolidayResponse
- [ ] Implement `SlotTemplateValidator` and `BusinessHoursValidator` with FluentValidation: time range checks (start < end), non-overlapping blocks, valid day_of_week enum, holiday date validation (not past for non-recurring)
- [ ] Implement `SlotTemplateService`: GetByProviderAndDay (cache-first with Redis 5-min TTL), CreateOrUpdate with EF Core optimistic locking (catch DbUpdateConcurrencyException → return 409), GetAffectedAppointments for conflict preview
- [ ] Implement `BusinessHoursService`: GetAll (cache-first), UpdateByDay with audit logging, AddHoliday with query to flag existing Appointment records on that date (set flagged_for_review), RemoveHoliday (soft delete via deleted_at), expand recurring holidays for date range queries
- [ ] Implement `SlotTemplateController` with [Authorize(Roles = "Admin")] attribute: GET `/api/admin/config/slots/{providerId}/{dayOfWeek}`, GET `/api/admin/config/slots/{providerId}`, PUT `/api/admin/config/slots/{providerId}/{dayOfWeek}`
- [ ] Implement `BusinessHoursController` with [Authorize(Roles = "Admin")] attribute: GET `/api/admin/config/hours`, PUT `/api/admin/config/hours`, GET `/api/admin/config/holidays`, POST `/api/admin/config/holidays`, DELETE `/api/admin/config/holidays/{id}`
- [ ] Register services in DI (Program.cs), add Redis cache integration with key patterns `config:slots:{providerId}:{day}` and `config:hours`, invalidate on mutation; add audit log calls via existing AuditService for all configuration changes
