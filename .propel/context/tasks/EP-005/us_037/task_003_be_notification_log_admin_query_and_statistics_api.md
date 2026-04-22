# Task - task_003_be_notification_log_admin_query_and_statistics_api

## Requirement Reference

- User Story: US_037
- Story Location: .propel/context/tasks/EP-005/us_037/us_037.md
- Acceptance Criteria:
    - AC-4: Given an admin views the notification log, When they filter by status, Then the log displays delivery statistics including success rate, failure rate, and average delivery time.
    - AC-3: Given notifications are marked `permanently_failed`, When staff review is required, Then those records remain visible for operational follow-up.
    - AC-1: Given attempts are logged, When the admin queries the log, Then recipient, channel, status, and timing data are available for inspection.
- Edge Case:
    - EC-1: Statistics must exclude `opted-out` and `cancelled-before-send` rows from delivery-failure rate calculations so admin metrics reflect actual attempted deliveries.
    - EC-2: Records buffered during a temporary logging outage must appear in the admin query once they are flushed, without duplicate aggregate counts.

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
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| API Documentation | Swagger (Swashbuckle) | 6.x |
| Logging | Serilog | 8.x |
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

Implement the backend query and reporting surface that administrators will use to monitor notification reliability. This task exposes filtered notification-log retrieval and summary statistics so operations staff can inspect status, recipient, channel, retry history, and permanent-failure cases without reading raw database tables. The API must return success rate, failure rate, and average delivery time from persisted notification and attempt data while keeping operational metrics accurate by excluding non-delivery outcomes such as `opted-out` and `cancelled-before-send` from failure calculations.

## Dependent Tasks

- task_001_be_notification_delivery_reliability_and_retry_orchestration (Retry orchestration and permanent-failure semantics must exist)
- task_002_db_notification_delivery_attempt_and_status_persistence (Attempt-level persistence and analytics fields must exist)
- US_010 task_001_be_constraint_migration (NotificationLog entity baseline and DbContext conventions must exist)

## Impacted Components

- **NEW** `AdminNotificationLogController` - Authorized admin endpoints for filtered notification log retrieval and delivery statistics (Server/Controllers/)
- **NEW** `INotificationLogQueryService` / `NotificationLogQueryService` - Encapsulates filtering, aggregation, and paginated retrieval over notification logs and attempts (Server/Services/Notifications/)
- **NEW** `NotificationLogFilterRequest` - Query DTO for status, channel, date-range, and staff-review filters (Server/Models/DTOs/)
- **NEW** `NotificationLogSummaryDto` - DTO containing success rate, failure rate, average delivery time, and counts by status (Server/Models/DTOs/)
- **MODIFY** Swagger configuration or controller registration path - Document secured admin notification-log endpoints (Server/Program.cs or Server/Extensions/)

## Implementation Plan

1. **Create an admin-scoped notification log query service** that can filter by status, channel, notification type, date range, and staff-review-required state.
2. **Return paginated notification-log records with attempt summaries** so administrators can inspect recipient, current status, retry count, last-attempt timestamp, and whether a contact-validation action is pending.
3. **Compute operational statistics from persisted data** by returning success rate, failure rate, and average delivery time for the filtered result set.
4. **Keep metrics semantically correct** by excluding `opted-out` and `cancelled-before-send` from attempted-delivery success/failure percentages while still making those statuses filterable in the raw log view.
5. **Expose `permanently_failed` and staff-review-required filters explicitly** so operations staff can focus on unresolved delivery issues.
6. **Secure the endpoints for admin-only access** and document the response contracts for downstream UI or reporting work.

## Current Project State

```text
Server/
  Controllers/
  Services/
    Notifications/
  Models/
    DTOs/
    Entities/
      NotificationLog.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/AdminNotificationLogController.cs | Admin-only endpoints for filtered notification-log retrieval and statistics |
| CREATE | Server/Services/Notifications/INotificationLogQueryService.cs | Interface for notification-log filtering and aggregate reporting |
| CREATE | Server/Services/Notifications/NotificationLogQueryService.cs | Query and aggregate notification delivery metrics from persisted logs and attempts |
| CREATE | Server/Models/DTOs/NotificationLogFilterRequest.cs | Filter contract for status, channel, date-range, and staff-review queries |
| CREATE | Server/Models/DTOs/NotificationLogSummaryDto.cs | Delivery metric summary payload for success rate, failure rate, and average delivery time |
| MODIFY | Server/Program.cs | Register notification-log query services and secured admin API endpoints |

## External References

- ASP.NET Core authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-8.0
- ASP.NET Core model binding: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-8.0
- EF Core aggregate queries: https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators
- Swashbuckle ASP.NET Core: https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-8.0

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Expose admin-only endpoints for filtered notification-log retrieval and aggregate delivery statistics
- [x] Return recipient, channel, status, retry count, and timing data needed for operational review
- [x] Include success rate, failure rate, and average delivery time for the filtered result set
- [x] Exclude `opted-out` and `cancelled-before-send` from attempted-delivery failure metrics while keeping them filterable
- [x] Make `permanently_failed` and staff-review-required records easy to isolate for follow-up
- [x] Keep the API reusable for a later UI implementation without coupling it to a specific screen