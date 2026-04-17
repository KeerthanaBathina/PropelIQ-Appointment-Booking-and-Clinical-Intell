# Task - TASK_004

## Requirement Reference

- User Story: US_058
- Story Location: .propel/context/tasks/EP-010/us_058/us_058.md
- Acceptance Criteria:
    - AC-3: **Given** the admin dashboard is loaded, **When** the admin navigates to user management, **Then** they see a list of all staff and admin accounts with status, last login, and role.
    - AC-4: **Given** the admin clicks configuration, **When** the configuration panel opens, **Then** it provides tabs for appointment templates, business hours, notification templates, and risk thresholds.
- Edge Case:
    - What happens when system metrics data is temporarily unavailable? Dashboard shows cached values with a "Data as of [timestamp]" indicator.

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
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |

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

Implement the backend API endpoints for admin configuration management and user management. This task creates CRUD endpoints for four configuration categories (appointment slot templates, notification templates, business hours/holidays, risk thresholds) and user management endpoints for listing, creating, and deactivating staff/admin accounts. Follows the UC-009 sequence diagram: Admin → React → API → ConfigSvc/UserSvc → DB → Audit. All endpoints enforce admin role authorization (NFR-011), input validation/sanitization (NFR-018), audit logging with admin attribution (NFR-012), and idempotent operations (NFR-034).

## Dependent Tasks

- US_001 — Foundational — Requires backend API scaffold (.NET 8 project, middleware, auth pipeline)
- US_008 — Foundational — Requires all domain entities (User, Appointment, NotificationLog)
- task_005_db_admin_metrics_config_schema — Requires configuration tables (system_configuration, slot_templates, notification_templates)

## Impacted Components

- **NEW** `Server/Controllers/AdminConfigController.cs` — API controller for configuration CRUD endpoints
- **NEW** `Server/Controllers/AdminUserController.cs` — API controller for user management endpoints
- **NEW** `Server/Services/IConfigurationService.cs` — Interface for configuration management
- **NEW** `Server/Services/ConfigurationService.cs` — Implementation for configuration CRUD with validation
- **NEW** `Server/Services/IAdminUserService.cs` — Interface for admin user management
- **NEW** `Server/Services/AdminUserService.cs` — Implementation for user CRUD with audit logging
- **NEW** `Server/Models/DTOs/SlotTemplateDto.cs` — DTO for slot template configuration
- **NEW** `Server/Models/DTOs/NotificationTemplateDto.cs` — DTO for notification template configuration
- **NEW** `Server/Models/DTOs/BusinessHoursDto.cs` — DTO for business hours and holiday configuration
- **NEW** `Server/Models/DTOs/RiskThresholdDto.cs` — DTO for risk threshold configuration
- **NEW** `Server/Models/DTOs/AdminUserDto.cs` — DTO for staff/admin user listing
- **NEW** `Server/Models/DTOs/CreateUserDto.cs` — DTO for user creation request
- **MODIFY** `Server/Program.cs` — Register ConfigurationService and AdminUserService in DI container

## Implementation Plan

1. **Create configuration DTOs** — Define DTOs per UC-009 sequence diagram:
   - `SlotTemplateDto`: ProviderId (Guid), DayOfWeek (enum), TimeSlots (List of {StartTime, EndTime, IsAvailable}), SlotDuration (int minutes), BufferTime (int minutes)
   - `NotificationTemplateDto`: TemplateId (Guid), TemplateName (string), Channel (enum: Email, SMS, InApp), TriggerEvent (string), MessageBody (string with {{variable}} placeholders), IsActive (bool)
   - `BusinessHoursDto`: Schedule (Dictionary<DayOfWeek, {OpenTime, CloseTime, IsClosed}>), Holidays (List of {Date, Name, Type: Closed|HalfDay})
   - `RiskThresholdDto`: NoShowRiskThreshold (int 0-100), ScoringWeights (Dictionary<string, decimal>), AlertEnabled (bool)
   - `AdminUserDto`: UserId (Guid), Email (string), FullName (string), Role (enum), Status (Active/Inactive), LastLoginAt (DateTime?), CreatedAt (DateTime)
   - `CreateUserDto`: Email (string, required), FullName (string, required), Role (enum, required)

2. **Implement ConfigurationService** — Create service with methods aligned to UC-009 flows:
   - `GetConfigAsync(category)`: Load current configuration values by category from DB
   - `UpdateSlotTemplatesAsync(dto)`: Validate business rules (no overlapping slots, buffer compliance), update `slot_templates` table, log audit
   - `UpdateNotificationTemplatesAsync(dto)`: Validate template syntax (ensure {{variable}} placeholders resolve), update `notification_templates` table, log audit
   - `UpdateBusinessHoursAsync(dto)`: Validate schedule (open < close, no holiday conflicts), update `system_configuration` table, log audit
   - `UpdateRiskThresholdsAsync(dto)`: Validate threshold range (0-100), weights sum to 1.0, update `system_configuration` table, log audit
   - Each method returns validation errors with specific field-level messages for UC-009 extension 5a.

3. **Implement AdminUserService** — Create service with methods:
   - `GetAllUsersAsync()`: Return list of staff and admin accounts with status, last login, role (AC-3)
   - `CreateUserAsync(dto)`: Create new staff user, send invite email, log audit (UC-009 alt: Manage user accounts)
   - `DeactivateUserAsync(userId)`: Soft-deactivate user without deleting historical data (FR-088), log audit
   - `ActivateUserAsync(userId)`: Reactivate previously deactivated user, log audit

4. **Create AdminConfigController** — Implement RESTful endpoints following UC-009:
   - `GET /api/admin/config` → Returns all configuration categories
   - `PUT /api/admin/config/slots` → Update slot templates (body: `SlotTemplateDto`)
   - `PUT /api/admin/config/notifications` → Update notification templates (body: `NotificationTemplateDto`)
   - `PUT /api/admin/config/hours` → Update business hours and holidays (body: `BusinessHoursDto`)
   - `PUT /api/admin/config/risk-thresholds` → Update risk thresholds (body: `RiskThresholdDto`)
   - Return `422 Unprocessable Entity` for validation failures with field-level error details
   - Apply `[Authorize(Roles = "Admin")]` for all endpoints

5. **Create AdminUserController** — Implement endpoints:
   - `GET /api/admin/users` → Returns list of all staff/admin accounts (`List<AdminUserDto>`)
   - `POST /api/admin/users` → Create new staff/admin user (body: `CreateUserDto`)
   - `PUT /api/admin/users/{id}/deactivate` → Deactivate user account (preserves history per FR-088)
   - `PUT /api/admin/users/{id}/activate` → Reactivate user account
   - Apply `[Authorize(Roles = "Admin")]` for all endpoints

6. **Add audit logging** — Log all configuration changes and user management actions via the Audit Service with: action type (`data_modify` for config changes, `data_modify` for user management), resource type (config category or user), change details (old → new values), and admin user attribution (NFR-012). Use Serilog structured logging with correlation ID (NFR-035).

7. **Add input validation and sanitization** — Apply `[Required]`, `[Range]`, `[MaxLength]` data annotations on all DTOs. Sanitize string inputs to prevent injection attacks (NFR-018). Validate email format for user creation (DR-011). Ensure idempotent PUT operations (NFR-034) using entity version checking.

8. **Register services in DI** — Add `IConfigurationService`/`ConfigurationService` and `IAdminUserService`/`AdminUserService` as scoped services in `Program.cs`.

## Current Project State

- Project is in planning phase. No `Server/` folder exists yet.
- Backend scaffold will be established by US_001 (dependency).
- Placeholder to be updated during task execution based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `Server/Controllers/AdminConfigController.cs` | Configuration CRUD endpoints (GET config, PUT slots/notifications/hours/risk-thresholds) |
| CREATE | `Server/Controllers/AdminUserController.cs` | User management endpoints (GET users, POST user, PUT deactivate/activate) |
| CREATE | `Server/Services/IConfigurationService.cs` | Interface for configuration management operations |
| CREATE | `Server/Services/ConfigurationService.cs` | Configuration CRUD with business rule validation and audit logging |
| CREATE | `Server/Services/IAdminUserService.cs` | Interface for admin user management operations |
| CREATE | `Server/Services/AdminUserService.cs` | User CRUD with deactivation preservation and audit logging |
| CREATE | `Server/Models/DTOs/SlotTemplateDto.cs` | Slot template request/response DTO |
| CREATE | `Server/Models/DTOs/NotificationTemplateDto.cs` | Notification template request/response DTO |
| CREATE | `Server/Models/DTOs/BusinessHoursDto.cs` | Business hours and holidays request/response DTO |
| CREATE | `Server/Models/DTOs/RiskThresholdDto.cs` | Risk threshold request/response DTO |
| CREATE | `Server/Models/DTOs/AdminUserDto.cs` | User listing response DTO |
| CREATE | `Server/Models/DTOs/CreateUserDto.cs` | User creation request DTO |
| MODIFY | `Server/Program.cs` | Register ConfigurationService and AdminUserService in DI container |

## External References

- [ASP.NET Core 8 Web API Documentation](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0)
- [ASP.NET Core Model Validation](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-8.0)
- [Entity Framework Core 8 Saving Data](https://learn.microsoft.com/en-us/ef/core/saving/)
- [ASP.NET Core Identity User Management](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-8.0)
- [Serilog Structured Logging](https://serilog.net/)

## Build Commands

- Refer to applicable technology stack specific build commands in `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] GET /admin/config returns all configuration categories
- [ ] PUT /admin/config/slots saves and returns updated slot templates
- [ ] PUT /admin/config/notifications validates template syntax and saves
- [ ] PUT /admin/config/hours validates schedule and saves business hours/holidays
- [ ] PUT /admin/config/risk-thresholds validates range (0-100) and saves
- [ ] Validation failures return 422 with field-level error details
- [ ] GET /admin/users returns list with status, last login, role
- [ ] PUT /admin/users/{id}/deactivate preserves historical data (FR-088)
- [ ] Non-admin user receives 403 Forbidden on all endpoints
- [ ] Audit log entry created for every configuration change and user management action

## Implementation Checklist

- [ ] Create configuration DTOs (`SlotTemplateDto`, `NotificationTemplateDto`, `BusinessHoursDto`, `RiskThresholdDto`)
- [ ] Create user management DTOs (`AdminUserDto`, `CreateUserDto`)
- [ ] Implement `ConfigurationService` with CRUD methods and business rule validation per UC-009
- [ ] Implement `AdminUserService` with user listing, creation, deactivation, and activation
- [ ] Create `AdminConfigController` with admin-guarded GET/PUT configuration endpoints
- [ ] Create `AdminUserController` with admin-guarded GET/POST/PUT user management endpoints
- [ ] Add audit logging for all configuration changes and user management actions (NFR-012)
- [ ] Register services in DI container in `Program.cs`
