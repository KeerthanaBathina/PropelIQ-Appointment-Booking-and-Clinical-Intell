# Task - task_001_be_booking_validation_rules

## Requirement Reference

- User Story: us_085
- Story Location: .propel/context/tasks/EP-016/us_085/us_085.md
- Acceptance Criteria:
  - AC-1: Given an appointment booking is submitted, When the date is validated, Then the system rejects dates beyond 90 days from today with a descriptive error message.
  - AC-2: Given a patient email is submitted, When format validation runs, Then the system validates against a standard email regex pattern and rejects invalid formats.
  - AC-3: Given a booking request is submitted, When duplicate checking runs, Then the system enforces the patient_id + appointment_time unique constraint and returns a clear error for duplicates.
- Edge Case:
  - What happens when a validation rule is updated while requests are in-flight? Validation rules are loaded at application startup and refreshed on configuration change; in-flight requests use the rules at the time of processing.
  - How does the system handle timezone edge cases for date validation (booking at 11:59 PM)? All dates are normalized to the clinic's local timezone before validation.

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
| Backend | FluentValidation.AspNetCore | 11.x |
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

> This task implements deterministic FluentValidation rules for appointment booking and email format. No LLM inference involved.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Enhance the existing FluentValidation infrastructure (established in US_010 task_002) with production-grade booking validation rules per DR-012 and DR-014. This task provides three capabilities: (1) **Timezone-aware date range validation** — enhance the existing `AppointmentDateValidator` to normalize appointment dates to the clinic's configured local timezone before applying the 90-day range check, preventing edge cases where a booking submitted at 11:59 PM local time is incorrectly rejected because the UTC conversion pushes it to the next day (edge case 2); (2) **Application-level duplicate booking prevention** — implement a pre-database-check validator that queries for existing bookings with the same `(patient_id, appointment_time)` before the request reaches EF Core, returning a descriptive 409 Conflict error with the conflicting appointment details (AC-3), complementing the database-level unique constraint from US_010; (3) **Enhanced email validation** — upgrade the existing `EmailValidator` (from US_010) with a more descriptive error message, example format hint, and configurable regex pattern loaded from `IOptionsMonitor<T>` to support hot-reload on configuration change (edge case 1). All validators are registered via FluentValidation's auto-validation pipeline, rejecting invalid input at the API boundary before reaching business logic.

## Dependent Tasks

- US_008 task_002_be_efcore_configuration_migrations — Requires entity models (Appointment, Patient) and `ApplicationDbContext`.
- US_010 task_001_be_constraint_migration — Requires `(patient_id, appointment_time)` unique constraint at database level.
- US_010 task_002_be_validation_error_handling — Requires FluentValidation infrastructure, `EmailValidator`, `AppointmentDateValidator`, and `GlobalExceptionHandlerMiddleware`.

## Impacted Components

- **NEW** `src/UPACIP.Service/Validation/DuplicateBookingValidator.cs` — Application-level duplicate booking check via EF Core query before DB constraint
- **NEW** `src/UPACIP.Service/Validation/Models/ValidationRuleOptions.cs` — Configuration: ClinicTimezoneId, MaxBookingDays, EmailRegexPattern
- **MODIFY** `src/UPACIP.Service/Validation/AppointmentDateValidator.cs` — Add timezone normalization for clinic local time, use configurable max booking days
- **MODIFY** `src/UPACIP.Service/Validation/EmailValidator.cs` — Add descriptive error with format hint, use configurable regex from IOptionsMonitor
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add ValidationRules configuration section with timezone and regex settings
- **MODIFY** `src/UPACIP.Api/Program.cs` — Bind ValidationRuleOptions, register DuplicateBookingValidator

## Implementation Plan

1. **Create `ValidationRuleOptions` configuration model (edge case 1)**: Create `ValidationRuleOptions` with: `string ClinicTimezoneId` (default: `"America/New_York"` — configurable per clinic deployment), `int MaxBookingDaysAhead` (default: 90, per DR-012/FR-013), `string EmailRegexPattern` (default: `^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$`). Register via `IOptionsMonitor<ValidationRuleOptions>` (not `IOptions<T>`) to support runtime configuration refresh without restart — when `appsettings.json` is updated, the next validation request automatically uses the new values (edge case 1: in-flight requests use the rules at the time of processing). Add to `appsettings.json`:
   ```json
   "ValidationRules": {
     "ClinicTimezoneId": "America/New_York",
     "MaxBookingDaysAhead": 90,
     "EmailRegexPattern": "^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$"
   }
   ```

2. **Enhance `AppointmentDateValidator` with timezone normalization (AC-1, edge case 2)**: Modify the existing `AppointmentDateValidator` (from US_010) to inject `IOptionsMonitor<ValidationRuleOptions>`. Replace the current UTC-based date range check with a timezone-aware implementation:
   - (a) Resolve the clinic's `TimeZoneInfo` from `ClinicTimezoneId` using `TimeZoneInfo.FindSystemTimeZoneById()` (on Windows) or IANA timezone ID (cross-platform via `TimeZoneInfo.TryFindSystemTimeZoneById`).
   - (b) Convert the incoming `AppointmentTime` (assumed UTC from the API) to clinic local time: `TimeZoneInfo.ConvertTimeFromUtc(appointmentTimeUtc, clinicTz)`.
   - (c) Compute `today` and `maxDate` in clinic local time: `today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, clinicTz).Date`, `maxDate = today.AddDays(options.MaxBookingDaysAhead)`.
   - (d) Validate: `localAppointmentDate >= today && localAppointmentDate <= maxDate`.
   - (e) Error message: `"Appointment date must be between {today:yyyy-MM-dd} and {maxDate:yyyy-MM-dd} ({ClinicTimezoneId} timezone). Received: {localAppointmentDate:yyyy-MM-dd}."`.
   This resolves the 11:59 PM edge case — a booking at 11:59 PM EST on the last valid day is correctly validated in EST, not incorrectly rejected because UTC has rolled to the next day.

3. **Enhance `EmailValidator` with descriptive errors (AC-2, edge case 1)**: Modify the existing `EmailValidator` (from US_010) to inject `IOptionsMonitor<ValidationRuleOptions>`. Changes:
   - (a) Use the configurable regex pattern from `options.CurrentValue.EmailRegexPattern` instead of a hardcoded pattern. Compile the regex with `RegexOptions.Compiled` and cache it — recompile only when the pattern changes (detected by comparing against the previous pattern string).
   - (b) Enhance the error message: `"Invalid email format. Expected format: user@example.com. Received: '{PropertyValue}'."`.
   - (c) Add `Regex.MatchTimeout` of 100ms to prevent ReDoS attacks from maliciously crafted email strings.
   - (d) Add a maximum length check: `RuleFor(x => x.Email).MaximumLength(254)` — the RFC 5321 maximum email length.

4. **Implement `DuplicateBookingValidator` (AC-3)**: Create `DuplicateBookingValidator` as a service-layer validator (not a FluentValidation `AbstractValidator` since it requires async database access). Implement `IDuplicateBookingValidator` with method `ValidateNoDuplicateAsync(Guid patientId, DateTime appointmentTime, CancellationToken ct)`:
   - (a) Query `ApplicationDbContext.Appointments.AnyAsync(a => a.PatientId == patientId && a.AppointmentTime == appointmentTime && a.Status != AppointmentStatus.Cancelled, ct)`.
   - (b) If a duplicate exists, throw a custom `DuplicateBookingException` with: `PatientId`, `AppointmentTime`, `ExistingAppointmentId` (for reference), and message `"A booking already exists for this patient at {appointmentTime:yyyy-MM-dd HH:mm}. Please choose a different time slot."`.
   - (c) This check runs BEFORE the booking service attempts the database insert, providing a user-friendly error instead of a raw database constraint violation. The database-level unique constraint (from US_010) remains as the ultimate safety net for race conditions.
   Register as scoped service (`services.AddScoped<IDuplicateBookingValidator, DuplicateBookingValidator>()`). Inject into `AppointmentBookingService.BookAppointmentAsync()` and call before the booking transaction begins.

5. **Handle `DuplicateBookingException` in middleware**: Extend the existing `GlobalExceptionHandlerMiddleware` (from US_010) to catch `DuplicateBookingException` and return HTTP 409 Conflict with the structured `ErrorResponse`:
   ```json
   {
     "error": "Duplicate booking",
     "detail": "A booking already exists for this patient at 2026-04-18 10:00. Please choose a different time slot.",
     "existingAppointmentId": "abc123..."
   }
   ```
   This provides a more descriptive response than the raw database constraint error (SqlState 23505) which only returns the constraint name.

6. **Add past-date rejection with clear messaging (AC-1)**: Enhance the `AppointmentDateValidator` to explicitly reject past dates with a distinct error message: `"Appointment date cannot be in the past. Today is {today:yyyy-MM-dd} ({ClinicTimezoneId} timezone)."`. This separates the "too far in the future" error from the "in the past" error for better user guidance. The two rules:
   - `RuleFor(x => x.AppointmentTime).Must(date => ConvertToLocal(date) >= today).WithMessage("...past date...")`.
   - `RuleFor(x => x.AppointmentTime).Must(date => ConvertToLocal(date) <= maxDate).WithMessage("...beyond 90 days...")`.

7. **Integrate duplicate check into booking service**: Modify `AppointmentBookingService.BookAppointmentAsync()` to call `IDuplicateBookingValidator.ValidateNoDuplicateAsync(patientId, appointmentTime)` as the first operation before slot hold verification or database transaction. This ensures the duplicate check happens at the earliest possible point, avoiding unnecessary processing for duplicate requests. The validator query uses `AsNoTracking()` for read performance since it only checks existence.

8. **Register services and bind configuration**: In `Program.cs`: bind `ValidationRuleOptions` via `builder.Services.Configure<ValidationRuleOptions>(builder.Configuration.GetSection("ValidationRules"))` and register `services.AddScoped<IDuplicateBookingValidator, DuplicateBookingValidator>()`. The existing FluentValidation auto-registration from US_010 (`AddValidatorsFromAssemblyContaining<EmailValidator>()`) automatically picks up the modified validators without additional registration. Add the `DuplicateBookingException` catch block to `GlobalExceptionHandlerMiddleware`.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   │   ├── AppointmentController.cs
│   │   │   └── PatientController.cs
│   │   ├── Middleware/
│   │   │   ├── GlobalExceptionHandlerMiddleware.cs  ← from US_001/US_010
│   │   │   └── CorrelationIdMiddleware.cs
│   │   └── appsettings.json
│   ├── UPACIP.Service/
│   │   ├── UPACIP.Service.csproj
│   │   ├── Validation/
│   │   │   ├── EmailValidator.cs                    ← from US_010
│   │   │   ├── AppointmentDateValidator.cs          ← from US_010
│   │   │   └── SoftDeleteReferenceValidator.cs      ← from US_010
│   │   └── Caching/
│   └── UPACIP.DataAccess/
│       ├── ApplicationDbContext.cs
│       ├── Entities/
│       │   ├── Patient.cs                           ← from US_008
│       │   ├── Appointment.cs                       ← from US_008
│       │   └── MedicalCode.cs                       ← from US_008
│       └── Configurations/
│           ├── AppointmentConfiguration.cs          ← from US_008/US_010
│           └── PatientConfiguration.cs              ← from US_008
├── Server/
│   ├── Services/
│   │   ├── AppointmentBookingService.cs             ← from US_018
│   │   └── AppointmentSlotCacheService.cs           ← from US_017
│   └── AI/
├── app/
├── config/
└── scripts/
```

> Assumes US_008 (entities), US_010 (constraints + FluentValidation), US_017 (slot API), and US_018 (booking API) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | src/UPACIP.Service/Validation/DuplicateBookingValidator.cs | Application-level duplicate (patient_id, appointment_time) check with descriptive 409 |
| CREATE | src/UPACIP.Service/Validation/Models/ValidationRuleOptions.cs | Config: ClinicTimezoneId, MaxBookingDaysAhead, EmailRegexPattern |
| MODIFY | src/UPACIP.Service/Validation/AppointmentDateValidator.cs | Timezone normalization, configurable max days, separate past-date error |
| MODIFY | src/UPACIP.Service/Validation/EmailValidator.cs | Configurable regex via IOptionsMonitor, descriptive error, ReDoS timeout |
| MODIFY | src/UPACIP.Api/Middleware/GlobalExceptionHandlerMiddleware.cs | Add DuplicateBookingException → 409 Conflict handling |
| MODIFY | Server/Services/AppointmentBookingService.cs | Call IDuplicateBookingValidator before booking transaction |
| MODIFY | src/UPACIP.Api/Program.cs | Bind ValidationRuleOptions, register DuplicateBookingValidator |
| MODIFY | src/UPACIP.Api/appsettings.json | Add ValidationRules section with timezone, max days, email regex |

## External References

- [FluentValidation — Custom Validators](https://docs.fluentvalidation.net/en/latest/custom-validators.html)
- [TimeZoneInfo — .NET](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo)
- [IOptionsMonitor — Configuration Change Notifications](https://learn.microsoft.com/en-us/dotnet/core/extensions/options#ioptionsmonitor)
- [RFC 5321 — Email Address Length](https://www.rfc-editor.org/rfc/rfc5321#section-4.5.3.1.3)
- [Regex.MatchTimeout — ReDoS Protection](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex.-ctor#system-text-regularexpressions-regex-ctor(system-string-system-text-regularexpressions-regexoptions-system-timespan))

## Build Commands

```powershell
# Build Service project
dotnet build src/UPACIP.Service/UPACIP.Service.csproj

# Build API project
dotnet build src/UPACIP.Api/UPACIP.Api.csproj

# Build full solution
dotnet build UPACIP.sln
```

## Implementation Validation Strategy

- [ ] `dotnet build` completes with zero errors for all projects
- [ ] Booking with date >90 days ahead returns 400 with descriptive "beyond 90 days" error
- [ ] Booking with past date returns 400 with distinct "in the past" error message
- [ ] Booking at 11:59 PM local time on the last valid day is accepted (timezone normalization)
- [ ] Booking at 12:01 AM local time on day 91 is rejected correctly
- [ ] Duplicate booking (same patient + time) returns 409 Conflict with existing appointment details
- [ ] Database-level unique constraint still catches race conditions on concurrent duplicate bookings
- [ ] Invalid email format returns 400 with "Expected format: user@example.com" hint
- [ ] Email regex pattern change in appsettings.json takes effect on next request without restart
- [ ] Regex evaluation times out after 100ms for maliciously long input (ReDoS protection)
- [ ] Email longer than 254 characters is rejected

## Implementation Checklist

- [ ] Create `ValidationRuleOptions` with ClinicTimezoneId, MaxBookingDaysAhead, EmailRegexPattern and bind via IOptionsMonitor
- [ ] Enhance `AppointmentDateValidator` with timezone normalization and separate past-date/future-date error messages
- [ ] Enhance `EmailValidator` with configurable regex, descriptive error, 254-char limit, and 100ms timeout
- [ ] Implement `IDuplicateBookingValidator` / `DuplicateBookingValidator` with async EF Core existence check
- [ ] Add `DuplicateBookingException` handling in `GlobalExceptionHandlerMiddleware` → 409 Conflict
- [ ] Integrate `IDuplicateBookingValidator` into `AppointmentBookingService` pre-transaction check
- [ ] Add ValidationRules configuration section to appsettings.json
- [ ] Register DuplicateBookingValidator and bind ValidationRuleOptions in Program.cs
