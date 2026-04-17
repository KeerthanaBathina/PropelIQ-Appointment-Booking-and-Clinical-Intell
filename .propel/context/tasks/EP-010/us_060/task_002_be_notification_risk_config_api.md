# Task - TASK_002

## Requirement Reference

- User Story: us_060
- Story Location: .propel/context/tasks/EP-010/us_060/us_060.md
- Acceptance Criteria:
    - AC-1: Given the admin opens notification template configuration, When they select a template (booking confirmation, 24h reminder, 2h reminder), Then they can edit subject line, body text, and variable placeholders (patient name, date, time, provider).
    - AC-2: Given the admin edits a notification template, When they save changes, Then all future notifications use the updated template without affecting already-sent messages.
    - AC-3: Given the admin opens risk configuration, When they adjust the no-show risk threshold, Then the system recalculates risk scoring display using the new threshold values.
    - AC-4: Given the admin modifies scoring parameters, When they save changes, Then the system logs the parameter change with admin attribution and timestamp.
- Edge Cases:
    - Invalid variable placeholders: System validates template syntax on save and rejects templates with unrecognized variables. Return 422 with specific error identifying unrecognized variables.
    - Risk threshold changes on active appointments: Risk scores for existing appointments are recalculated in the next batch run (not retroactively applied immediately). API returns confirmation with deferred recalculation notice.

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
| Frontend | N/A | - |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| ORM | Entity Framework Core | 8.x |
| Database | PostgreSQL (consumed via EF Core) | 16.x |
| Auth | ASP.NET Core Identity + JWT | 8.x |
| Cache | Upstash Redis | 7.x |
| Email | SendGrid / Gmail SMTP | - |
| SMS | Twilio API | 2023-05 |
| Logging | Serilog + Seq | 8.x / 2024.x |
| AI/ML | N/A | - |
| Vector Store | N/A | - |
| AI Gateway | N/A | - |
| Mobile | N/A | - |

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

Implement the ASP.NET Core Web API endpoints for notification template management and risk configuration within the Admin Configuration domain. This includes CRUD operations for notification templates (booking confirmation, 24h reminder, 2h reminder) with template syntax validation ensuring only recognized variable placeholders are accepted, and GET/PUT endpoints for risk configuration (no-show threshold and scoring parameters). All configuration changes are audit-logged with admin attribution and timestamp per AC-4. Template updates are forward-only — they apply to future notifications without affecting already-sent messages (AC-2). Risk threshold changes trigger a deferred recalculation flag rather than immediate retroactive scoring (edge case).

## Dependent Tasks

- US_008 (EP-DATA) — Domain entities (NotificationLog, AuditLog) must exist
- US_001 (EP-TECH) — Backend API scaffold, authentication middleware, base controller patterns
- task_003_db_notification_risk_config_schema (US_060) — Database schema and migrations must be applied

## Impacted Components

- **NEW** `Server/Controllers/AdminConfigController.cs` — API controller for notification template and risk config endpoints (or extend existing admin controller if created by US_058)
- **NEW** `Server/Services/NotificationTemplateService.cs` — Business logic for template CRUD, validation, and versioning
- **NEW** `Server/Services/RiskConfigService.cs` — Business logic for risk threshold/scoring parameter management
- **NEW** `Server/Validators/TemplateValidator.cs` — FluentValidation validator for notification template syntax and variable placeholders
- **NEW** `Server/Validators/RiskConfigValidator.cs` — FluentValidation validator for risk threshold (0–100) and scoring parameter weights (positive, sum to 1.0)
- **NEW** `Server/DTOs/NotificationTemplateDto.cs` — Request/response DTOs for notification template endpoints
- **NEW** `Server/DTOs/RiskConfigDto.cs` — Request/response DTOs for risk configuration endpoints
- **MODIFY** `Server/Services/AuditService.cs` — Extend to log configuration changes with admin attribution (AC-4)

## Implementation Plan

1. **Define DTOs** for API request/response payloads:
   - `NotificationTemplateListDto` — List response with id, templateType, channel, subject preview, lastUpdated
   - `NotificationTemplateDetailDto` — Full template with id, templateType, channel, subject, bodyText, variables[], updatedAt, updatedBy
   - `UpdateNotificationTemplateRequest` — subject, bodyText (validated for placeholder syntax)
   - `RiskConfigDto` — noShowThreshold (int 0–100), scoringParams (Dictionary<string, decimal>), updatedAt, updatedBy
   - `UpdateRiskConfigRequest` — noShowThreshold, scoringParams

2. **Implement TemplateValidator** using FluentValidation:
   - Subject: required, max 200 chars
   - BodyText: required, max 5000 chars
   - Variable placeholder validation: regex `\{\{(\w+)\}\}` extracts all placeholders → validate each against allowed set (`patient_name`, `date`, `time`, `provider`) → reject with specific error listing unrecognized variables
   - Channel: must be "Email" or "SMS"

3. **Implement RiskConfigValidator** using FluentValidation:
   - NoShowThreshold: required, range 0–100, integer
   - ScoringParams: all values must be positive decimals, sum must equal 1.0 (with ±0.01 tolerance for floating point)

4. **Implement NotificationTemplateService**:
   - `GetAllTemplatesAsync()` — Return all templates grouped by type
   - `GetTemplateByIdAsync(int id)` — Return single template detail
   - `UpdateTemplateAsync(int id, UpdateNotificationTemplateRequest request, string adminId)` — Validate via TemplateValidator, update record, create audit log entry (AC-4). Template update is forward-only: update the template record but do not modify any NotificationLog entries already sent (AC-2)
   - Use EF Core optimistic concurrency (version field) to prevent concurrent admin edits

5. **Implement RiskConfigService**:
   - `GetRiskConfigAsync()` — Return current risk configuration (singleton config record)
   - `UpdateRiskConfigAsync(UpdateRiskConfigRequest request, string adminId)` — Validate via RiskConfigValidator, update config record, set `recalculation_pending` flag to true (triggers batch recalculation in next scheduled run), create audit log entry with old values and new values for admin attribution (AC-4)
   - `RecalculateRiskScoresAsync()` — Batch method to recalculate all active appointment risk scores using new threshold/params. Called by background job, not by API endpoint directly. Clears `recalculation_pending` flag on completion.

6. **Implement AdminConfigController endpoints**:
   - `GET /api/admin/config/notifications` — List all notification templates. Authorize: Admin role only.
   - `GET /api/admin/config/notifications/{id}` — Get single template detail. Authorize: Admin role only.
   - `PUT /api/admin/config/notifications/{id}` — Update template. Validate request body. Return 200 with updated template or 422 with validation errors. Authorize: Admin role only.
   - `GET /api/admin/config/risk` — Get current risk configuration. Authorize: Admin role only.
   - `PUT /api/admin/config/risk` — Update risk configuration. Validate request body. Return 200 with updated config and deferred recalculation notice, or 422 with validation errors. Authorize: Admin role only.

7. **Extend AuditService** to log configuration changes:
   - Log entry includes: entityType ("NotificationTemplate" or "RiskConfig"), entityId, action ("UPDATE"), changedBy (admin userId from JWT), changedAt (UTC timestamp), previousValues (JSON), newValues (JSON)
   - Use structured logging via Serilog for observability

8. **Add Redis cache invalidation** — Invalidate cached notification templates and risk config on update. Templates are frequently read by the notification sending pipeline, so cache with 5-minute TTL and invalidate on write.

**Focus on how to implement:**
- Use `[Authorize(Roles = "Admin")]` attribute on all controller actions
- Extract admin userId from `HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)`
- Use `IValidator<T>` from FluentValidation with ASP.NET Core integration for automatic model validation
- Return `ProblemDetails` (RFC 7807) for 422 validation errors with specific field-level errors
- Use EF Core `RowVersion`/`ConcurrencyToken` for optimistic concurrency on template updates
- Use `IDistributedCache` (Redis) for template caching with `SetStringAsync` and key pattern `config:notification:{id}`
- Log all config changes at `Information` level via Serilog with structured properties

## Current Project State

```
[Placeholder — to be updated based on dependent task completion]
Server/
├── Controllers/
│   └── AdminConfigController.cs     (NEW)
├── Services/
│   ├── NotificationTemplateService.cs (NEW)
│   ├── RiskConfigService.cs          (NEW)
│   └── AuditService.cs              (MODIFY)
├── Validators/
│   ├── TemplateValidator.cs          (NEW)
│   └── RiskConfigValidator.cs        (NEW)
├── DTOs/
│   ├── NotificationTemplateDto.cs    (NEW)
│   └── RiskConfigDto.cs             (NEW)
└── Program.cs                        (MODIFY — register services)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/AdminConfigController.cs | API controller with GET/PUT endpoints for notification templates and risk config, Admin role authorization |
| CREATE | Server/Services/NotificationTemplateService.cs | Template CRUD business logic, validation orchestration, forward-only update, audit logging |
| CREATE | Server/Services/RiskConfigService.cs | Risk config get/update, deferred recalculation flag, batch recalculation method, audit logging |
| CREATE | Server/Validators/TemplateValidator.cs | FluentValidation rules for template subject, body, and variable placeholder syntax |
| CREATE | Server/Validators/RiskConfigValidator.cs | FluentValidation rules for threshold range (0–100) and scoring parameter weights (sum=1.0) |
| CREATE | Server/DTOs/NotificationTemplateDto.cs | Request/response DTOs for notification template list, detail, and update |
| CREATE | Server/DTOs/RiskConfigDto.cs | Request/response DTOs for risk config get and update |
| MODIFY | Server/Services/AuditService.cs | Add config change audit logging with entityType, previousValues, newValues JSON capture |
| MODIFY | Server/Program.cs | Register NotificationTemplateService, RiskConfigService, validators in DI container |

## External References

- [ASP.NET Core 8 Web API — Controller-based APIs](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [FluentValidation — ASP.NET Core Integration](https://docs.fluentvalidation.net/en/latest/aspnet.html)
- [EF Core 8 — Optimistic Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency?tabs=data-annotations)
- [ASP.NET Core 8 — Authorization Policies](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-8.0)
- [RFC 7807 — Problem Details](https://www.rfc-editor.org/rfc/rfc7807)
- [Serilog — Structured Logging](https://serilog.net/)
- [IDistributedCache — Redis](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-8.0)

## Build Commands

- `cd Server && dotnet restore` — Restore NuGet packages
- `cd Server && dotnet build` — Build the project
- `cd Server && dotnet run` — Run the API server

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] `GET /api/admin/config/notifications` returns all 3 template types (booking confirmation, 24h reminder, 2h reminder)
- [ ] `GET /api/admin/config/notifications/{id}` returns full template with variables list
- [ ] `PUT /api/admin/config/notifications/{id}` updates template and returns 200 with updated data
- [ ] `PUT /api/admin/config/notifications/{id}` with invalid `{{unknown_var}}` returns 422 with specific error
- [ ] Template update does not modify existing NotificationLog records (AC-2 forward-only)
- [ ] `GET /api/admin/config/risk` returns current threshold and scoring parameters
- [ ] `PUT /api/admin/config/risk` updates config and sets recalculation_pending flag
- [ ] `PUT /api/admin/config/risk` with threshold >100 or <0 returns 422
- [ ] `PUT /api/admin/config/risk` with scoring param weights not summing to 1.0 returns 422
- [ ] All endpoints return 401 for unauthenticated requests
- [ ] All endpoints return 403 for non-Admin role requests
- [ ] Audit log entries created for all config updates with admin attribution (AC-4)
- [ ] Redis cache invalidated after template or risk config update
- [ ] Optimistic concurrency prevents conflicting concurrent admin edits

## Implementation Checklist

- [ ] Create `NotificationTemplateDto.cs` and `RiskConfigDto.cs` with request/response DTOs
- [ ] Create `TemplateValidator.cs` — FluentValidation rules for subject, body, variable placeholder regex validation
- [ ] Create `RiskConfigValidator.cs` — FluentValidation rules for threshold range and scoring weight sum
- [ ] Implement `NotificationTemplateService.cs` — GetAll, GetById, Update with optimistic concurrency and audit logging
- [ ] Implement `RiskConfigService.cs` — Get, Update with deferred recalculation flag and audit logging
- [ ] Implement `AdminConfigController.cs` — 5 endpoints (GET list, GET detail, PUT template, GET risk, PUT risk) with Admin authorization
- [ ] Extend `AuditService.cs` — Add config change logging with previousValues/newValues JSON
- [ ] Register services and validators in `Program.cs` DI container
