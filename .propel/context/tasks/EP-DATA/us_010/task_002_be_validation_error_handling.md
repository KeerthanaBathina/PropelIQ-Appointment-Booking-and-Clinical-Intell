# Task - task_002_be_validation_error_handling

## Requirement Reference

- User Story: us_010
- Story Location: .propel/context/tasks/EP-DATA/us_010/us_010.md
- Acceptance Criteria:
  - AC-3: Given (patient_id, appointment_time) unique constraint exists, When a duplicate booking is attempted, Then the database prevents the insert and the API returns 409 Conflict.
  - AC-5: Given email format validation is active, When an invalid email format is submitted, Then the application rejects it before persistence with a descriptive validation error.
- Edge Case:
  - What happens when a foreign key referenced record is soft-deleted? Application-level check prevents references to soft-deleted records.

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
| Backend | Entity Framework Core | 8.x |
| Library | FluentValidation.AspNetCore | 11.x |
| Database | PostgreSQL | 16.x |

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

Implement application-level validation rules using FluentValidation for email format (DR-011), appointment date range within 90 days (DR-012), and soft-delete reference prevention. Extend the existing `GlobalExceptionHandlerMiddleware` to catch `DbUpdateException` from EF Core and map PostgreSQL constraint violations to appropriate HTTP status codes: unique violation (SqlState 23505) → 409 Conflict, FK violation (SqlState 23503) → 400 Bad Request with descriptive error messages. Create reusable validator base classes and register them in the DI pipeline for automatic request validation.

## Dependent Tasks

- task_001_be_constraint_migration — Database constraints must be in place so that `DbUpdateException` scenarios can be triggered and handled.
- US_001 task_002_be_middleware_pipeline — `GlobalExceptionHandlerMiddleware` must exist for extending with database exception mapping.

## Impacted Components

- **NEW** `src/UPACIP.Service/Validation/EmailValidator.cs` — Reusable email format validation rule (DR-011)
- **NEW** `src/UPACIP.Service/Validation/AppointmentDateValidator.cs` — Validates appointment date is within 90 days from current date (DR-012)
- **NEW** `src/UPACIP.Service/Validation/SoftDeleteReferenceValidator.cs` — Validates that referenced Patient ID is not soft-deleted
- **MODIFY** `src/UPACIP.Api/Middleware/GlobalExceptionHandlerMiddleware.cs` — Add `DbUpdateException` handling: map PostgreSQL SqlState 23505 → 409, SqlState 23503 → 400
- **MODIFY** `src/UPACIP.Api/Program.cs` — Register FluentValidation services and validators in DI
- **NEW** `src/UPACIP.Api/UPACIP.Api.csproj` (MODIFY) — Add `FluentValidation.AspNetCore` 11.x NuGet package

## Implementation Plan

1. **Install FluentValidation.AspNetCore**: Add `FluentValidation.AspNetCore` (11.x) NuGet package to the `UPACIP.Api` project. In `Program.cs`, register validators with `builder.Services.AddFluentValidationAutoValidation().AddFluentValidationClientsideAdapters()` and `builder.Services.AddValidatorsFromAssemblyContaining<EmailValidator>()`. This auto-validates incoming request DTOs before controller actions execute, returning 400 Bad Request with field-level errors for invalid input.

2. **Create `EmailValidator` reusable rule**: In `src/UPACIP.Service/Validation/EmailValidator.cs`, create a static class with a reusable rule extension method `RuleFor(x => x.Email).Must(BeValidEmail)` using the regex pattern `^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$` per DR-011. Provide descriptive error message: `"Email must be in a valid format (e.g., user@example.com)"`. This is reused by Patient registration and User creation validators.

3. **Create `AppointmentDateValidator`**: In `src/UPACIP.Service/Validation/AppointmentDateValidator.cs`, implement `AbstractValidator<CreateAppointmentRequest>` (or equivalent DTO). Rule: `RuleFor(x => x.AppointmentTime).Must(date => date >= DateTime.UtcNow && date <= DateTime.UtcNow.AddDays(90))` with message `"Appointment date must be between today and 90 days from now"` per DR-012. This prevents bookings in the past or too far in the future.

4. **Create `SoftDeleteReferenceValidator`**: In `src/UPACIP.Service/Validation/SoftDeleteReferenceValidator.cs`, implement a validator that checks whether a referenced `PatientId` belongs to a soft-deleted patient by querying `ApplicationDbContext.Patients.IgnoreQueryFilters().Where(p => p.Id == patientId && p.DeletedAt != null).AnyAsync()`. If the patient is soft-deleted, return validation error: `"Referenced patient has been deactivated"`. This addresses the edge case where a new Appointment references a soft-deleted Patient (the global query filter hides the record, but the FK is still valid at DB level).

5. **Extend `GlobalExceptionHandlerMiddleware` for database exceptions**: In the existing middleware's `catch` block, add handling for `DbUpdateException`:
   - Check if `InnerException` is `Npgsql.PostgresException` with `SqlState`:
     - `"23505"` (unique_violation) → Return 409 Conflict with `{ "error": "Duplicate record", "detail": "<constraint name>" }`. Parse the constraint name from the exception to identify whether it's the email uniqueness or the (patient_id, appointment_time) composite constraint.
     - `"23503"` (foreign_key_violation) → Return 400 Bad Request with `{ "error": "Referenced record does not exist", "detail": "<FK column>" }`.
     - `"23514"` (check_violation) → Return 400 Bad Request with `{ "error": "Data validation failed", "detail": "<constraint name>" }`.
   - For `DbUpdateConcurrencyException` (already partially handled by the optimistic locking from US_008), ensure it returns 409 Conflict with `{ "error": "Conflict", "detail": "Record was modified by another user" }`.

6. **Create consistent error response model**: Define an `ErrorResponse` class (may already exist from US_001) with `Error` (string), `Detail` (string), and optional `ValidationErrors` (dictionary for FluentValidation field errors). Ensure all validation and constraint errors use this model for consistent API responses.

7. **Configure FluentValidation error response format**: Override the default FluentValidation error response factory in `Program.cs` to return 400 Bad Request with the `ErrorResponse` model format instead of the default `ValidationProblemDetails`. This ensures all validation errors (email format, date range, etc.) follow the same response structure as database constraint errors.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlerMiddleware.cs
│   │   │   └── CorrelationIdMiddleware.cs
│   │   ├── Models/
│   │   │   └── ErrorResponse.cs
│   │   └── Controllers/
│   ├── UPACIP.Service/
│   │   └── UPACIP.Service.csproj
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/ (10 domain entities)
│       ├── Configurations/ (9 configuration files with updated constraints)
│       └── Migrations/
│           ├── *_CreateDomainEntities.cs
│           └── *_AddReferentialIntegrityConstraints.cs
├── app/
└── scripts/
```

> Assumes US_001 (middleware pipeline), US_008 (entity models), and task_001_be_constraint_migration are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add `FluentValidation.AspNetCore` 11.x NuGet package |
| MODIFY | src/UPACIP.Api/Program.cs | Register FluentValidation auto-validation and assembly scanner, configure error response factory |
| MODIFY | src/UPACIP.Api/Middleware/GlobalExceptionHandlerMiddleware.cs | Handle `DbUpdateException` → 409 (23505 unique), 400 (23503 FK), and `DbUpdateConcurrencyException` → 409 |
| CREATE | src/UPACIP.Service/Validation/EmailValidator.cs | Static helper with reusable email regex validation rule per DR-011 |
| CREATE | src/UPACIP.Service/Validation/AppointmentDateValidator.cs | FluentValidation `AbstractValidator` enforcing 90-day date range per DR-012 |
| CREATE | src/UPACIP.Service/Validation/SoftDeleteReferenceValidator.cs | Validates PatientId is not soft-deleted before creating referencing records |

## External References

- [FluentValidation for ASP.NET Core](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [FluentValidation NuGet](https://www.nuget.org/packages/FluentValidation.AspNetCore)
- [PostgreSQL Error Codes (23505, 23503)](https://www.postgresql.org/docs/16/errcodes-appendix.html)
- [EF Core Handling Concurrency Conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [Npgsql PostgresException Class](https://www.npgsql.org/doc/api/Npgsql.PostgresException.html)
- [ASP.NET Core Error Handling Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-8.0)

## Build Commands

```powershell
# Restore packages
dotnet restore src/UPACIP.Api/UPACIP.Api.csproj

# Build
dotnet build UPACIP.sln

# Run API
dotnet run --project src/UPACIP.Api/UPACIP.Api.csproj

# Test email validation (should return 400)
curl -X POST http://localhost:5000/api/patients -H "Content-Type: application/json" -d '{"email": "not-an-email", "fullName": "Test"}'
# Expected: 400 Bad Request with validation error

# Test duplicate booking (should return 409)
# (Create two appointments with same patient_id + appointment_time)
# Expected: 409 Conflict on second attempt

# Test appointment date out of range (should return 400)
curl -X POST http://localhost:5000/api/appointments -H "Content-Type: application/json" -d '{"patientId": "...", "appointmentTime": "2027-01-01T10:00:00Z"}'
# Expected: 400 Bad Request — appointment date beyond 90 days

# Test FK violation (should return 400)
curl -X POST http://localhost:5000/api/appointments -H "Content-Type: application/json" -d '{"patientId": "00000000-0000-0000-0000-000000000000", "appointmentTime": "2026-04-20T10:00:00Z"}'
# Expected: 400 Bad Request — referenced patient does not exist
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors after adding FluentValidation package
- [ ] Submitting an invalid email format (e.g., "not-an-email") returns 400 with descriptive error before reaching the database
- [ ] Submitting an appointment date >90 days in the future returns 400 with date range validation error
- [ ] Attempting a duplicate booking returns 409 Conflict when the database rejects with unique constraint violation
- [ ] Attempting to insert an appointment for a non-existent patient returns 400 with "Referenced record does not exist"
- [ ] Creating a reference to a soft-deleted patient returns validation error "Referenced patient has been deactivated"
- [ ] All error responses follow consistent `ErrorResponse` model format with `error` and `detail` fields
- [ ] FluentValidation auto-validates request DTOs before controller actions execute

## Implementation Checklist

- [x] Add `FluentValidation.AspNetCore` 11.x to `UPACIP.Api.csproj` and register in `Program.cs` with `AddFluentValidationAutoValidation()` and `AddValidatorsFromAssemblyContaining<>()`
- [x] Create `EmailValidator` with reusable email regex rule (`^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$`) per DR-011
- [x] Create `AppointmentDateValidator` enforcing appointment time between now and now+90 days per DR-012
- [x] Create `SoftDeleteReferenceValidator` that checks `IgnoreQueryFilters()` to reject references to soft-deleted Patient records
- [x] Extend `GlobalExceptionHandlerMiddleware` to handle `DbUpdateException` mapping PostgreSQL SqlState 23505 → 409 Conflict and SqlState 23503 → 400 Bad Request
- [x] Ensure `DbUpdateConcurrencyException` returns 409 Conflict with consistent error response format
- [x] Configure FluentValidation error response factory in `Program.cs` to return `ErrorResponse` model with `ValidationErrors` dictionary for field-level errors
- [x] Verify all validation and constraint error responses follow consistent `ErrorResponse` structure
