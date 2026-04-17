# Task - task_002_be_noshow_detection_service

## Requirement Reference
- User Story: US_055
- Story Location: .propel/context/tasks/EP-009/us_055/us_055.md
- Acceptance Criteria:
    - AC-1: Given a patient has not been marked as arrived, When 15 minutes pass after their scheduled appointment time, Then the system automatically marks the appointment as "no-show" and updates the queue.
    - AC-2: Given a patient has been waiting, When their wait time exceeds the configurable threshold (default 30 minutes), Then the system displays a visual alert (amber/red badge) on the queue entry and notifies the staff member.
    - AC-3: Given the wait time threshold is configurable, When an admin changes the threshold value, Then the alert behavior updates immediately for all active queue entries.
    - AC-4: Given auto no-show detection runs, When a patient is marked as no-show, Then the event is logged in the audit trail with timestamp and "auto-detected" attribution.
- Edge Case:
    - System outage recovery: System processes all overdue appointments retroactively on recovery, marking them with "delayed-detection" flag.
    - Patient arrives at 14 minutes (just before auto no-show): The 15-minute timer resets upon arrival marking; auto no-show is cancelled.

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
| Frontend | N/A | N/A |
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| ORM | Entity Framework Core | 8.x |
| Caching | Upstash Redis | 7.x |
| Database | PostgreSQL | 16.x |
| AI/ML | N/A | N/A |

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
Implement the backend services for automatic no-show detection (15-minute timer) and configurable wait time threshold alerting. This includes a hosted background service that periodically scans scheduled appointments for no-show candidates, an API endpoint for admin threshold configuration, wait time alert computation in the Queue Service, and comprehensive audit logging for all auto-detected no-show events. The service must handle edge cases including system outage recovery (retroactive processing with "delayed-detection" flag) and late arrival cancellation of the no-show timer.

## Dependent Tasks
- US_008 tasks (EP-DATA/us_008/) — QueueEntry and Appointment entity models and migrations must exist.
- US_052 tasks (EP-009/us_052/) — Queue Service base implementation with arrival status marking must be complete.
- task_003_db_noshow_threshold_config.md — Database schema for threshold configuration and no-show audit fields must be migrated.

## Impacted Components
- **NEW** `NoShowDetectionService` (IHostedService) — Background service running every 60 seconds to detect and mark no-shows (Server/Services/Queue/)
- **NEW** `WaitThresholdAlertService` — Service to evaluate wait times against threshold and produce alert data (Server/Services/Queue/)
- **MODIFY** `QueueService` — Add methods: `GetWaitThresholdConfig()`, `UpdateWaitThreshold()`, `GetPatientsExceedingThreshold()`, cancel no-show timer on arrival (Server/Services/Queue/)
- **NEW** `QueueConfigController` — Admin API endpoints for threshold CRUD: `GET /api/queue/config/threshold`, `PUT /api/queue/config/threshold` (Server/Controllers/)
- **MODIFY** `QueueController` — Extend `GET /api/queue/today` response to include `isAutoNoShow`, `isDelayedDetection`, `exceedsWaitThreshold`, `waitAlertLevel` fields (Server/Controllers/)
- **MODIFY** `AuditService` — Add audit event type `auto_noshow_detected` with attribution "system" and optional "delayed-detection" flag (Server/Services/Audit/)

## Implementation Plan
1. **Create `NoShowDetectionService` as IHostedService**: Implement a .NET `BackgroundService` that runs every 60 seconds. On each tick:
   - Query all Appointments for today where `status = scheduled` AND `appointment_time + 15 minutes < DateTime.UtcNow` AND no corresponding QueueEntry with `status = waiting/in_visit`.
   - For each matching appointment: update `Appointment.status` to `no_show`, create/update QueueEntry with `status = no_show`.
   - Log audit event via AuditService with `action = auto_noshow_detected`, `resource_type = Appointment`, `attribution = system-auto`.
   - **Outage recovery**: On service startup, check for all overdue appointments (beyond 15 minutes) that are still `scheduled`. Process them with `delayed_detection = true` flag in the audit log entry.

2. **Implement arrival cancellation of no-show timer**: Modify the existing `QueueService.MarkArrival()` method (from US_052). When a patient is marked as arrived:
   - If `appointment_time + 15 minutes > DateTime.UtcNow` (within the 15-min window), the arrival simply proceeds normally — no-show detection will skip this appointment on next tick because a QueueEntry with `status = waiting` now exists.
   - If the appointment was already auto-marked as no-show, staff can override via the existing `MarkArrival` endpoint which updates the appointment status from `no_show` to `scheduled` and the QueueEntry status to `waiting`. Log audit event with `action = noshow_override`, `attribution = staff`.

3. **Create threshold configuration API**:
   - `GET /api/queue/config/threshold` — Returns current threshold value (default 30 minutes). Read from `SystemConfig` table (key: `queue.wait_threshold_minutes`). Cache in Redis with 60-second TTL for fast reads.
   - `PUT /api/queue/config/threshold` — Admin-only endpoint (authorize `[Authorize(Roles = "admin")]`). Accepts `{ thresholdMinutes: int }` (validation: min 5, max 120). Updates `SystemConfig` table and invalidates Redis cache. Log audit event with `action = config_change`.

4. **Implement wait threshold alert computation**: In `QueueService.GetTodayQueue()` (or equivalent method from US_052), for each QueueEntry with `status = waiting`:
   - Compute `waitTimeMinutes = (DateTime.UtcNow - arrival_timestamp).TotalMinutes`.
   - Fetch threshold from cache/DB.
   - Set `exceedsWaitThreshold = waitTimeMinutes >= thresholdMinutes`.
   - Set `waitAlertLevel`: `"none"` if below threshold, `"warning"` if `>= threshold`, `"critical"` if `>= threshold * 1.5`.
   - Return these fields in the queue API response DTO.

5. **Extend queue API response DTO**: Add fields to `QueueEntryDto`:
   - `isAutoNoShow` (bool): true if the no-show was auto-detected by the background service.
   - `isDelayedDetection` (bool): true if the no-show was detected during outage recovery.
   - `exceedsWaitThreshold` (bool): true if wait time exceeds configured threshold.
   - `waitAlertLevel` (string): "none" | "warning" | "critical".
   - `waitThresholdMinutes` (int): current configured threshold for frontend reference.

6. **Audit logging**: All auto no-show events must create an AuditLog entry with:
   - `user_id`: null (system action)
   - `action`: "auto_noshow_detected"
   - `resource_type`: "Appointment"
   - `resource_id`: appointment_id
   - `timestamp`: UTC timestamp of detection
   - Additional metadata (JSON): `{ "attribution": "system-auto", "delayed_detection": false, "scheduled_time": "...", "detection_time": "..." }`

## Current Project State
- [Placeholder — to be updated based on completion of dependent tasks US_052 and US_008]

## Expected Changes
| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Queue/NoShowDetectionService.cs | IHostedService for 60-second no-show detection scan with outage recovery logic |
| CREATE | Server/Services/Queue/WaitThresholdAlertService.cs | Service for evaluating wait times against configurable threshold |
| CREATE | Server/Controllers/QueueConfigController.cs | Admin API endpoints for GET/PUT wait threshold configuration |
| CREATE | Server/DTOs/Queue/WaitThresholdConfigDto.cs | DTO for threshold config request/response |
| MODIFY | Server/Services/Queue/QueueService.cs | Add threshold retrieval, arrival cancellation of no-show, alert computation |
| MODIFY | Server/DTOs/Queue/QueueEntryDto.cs | Add isAutoNoShow, isDelayedDetection, exceedsWaitThreshold, waitAlertLevel fields |
| MODIFY | Server/Controllers/QueueController.cs | Include new alert fields in GET /api/queue/today response |
| MODIFY | Server/Services/Audit/AuditService.cs | Add auto_noshow_detected event type and system attribution |
| MODIFY | Server/Program.cs | Register NoShowDetectionService as hosted service in DI container |

> Only list concrete, verifiable file operations. No speculative directory trees.

## External References
- [.NET 8 BackgroundService](https://learn.microsoft.com/en-us/dotnet/core/extensions/timer-service) — Timer-based hosted service pattern for periodic no-show scanning
- [ASP.NET Core Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles?view=aspnetcore-8.0) — Role-based authorization for admin-only threshold endpoint
- [EF Core 8 Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters) — Global query filters for soft-deleted entities
- [StackExchange.Redis Cache Invalidation](https://stackexchange.github.io/StackExchange.Redis/) — Redis cache invalidation pattern for threshold config

## Build Commands
- [Refer to applicable technology stack specific build commands](.propel/build/)

## Implementation Validation Strategy
- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] NoShowDetectionService correctly marks appointments as no-show after 15 minutes with no arrival
- [ ] NoShowDetectionService processes overdue appointments on startup with delayed-detection flag
- [ ] Arrival marking within 15-minute window prevents auto no-show on next detection cycle
- [ ] Staff override of auto no-show (re-marking as arrived) creates correct audit entry
- [ ] GET /api/queue/config/threshold returns default 30 minutes when no config exists
- [ ] PUT /api/queue/config/threshold validates range (5-120 minutes) and requires admin role
- [ ] Redis cache invalidated on threshold update; subsequent reads return new value
- [ ] Queue API response includes exceedsWaitThreshold and waitAlertLevel computed correctly
- [ ] All auto no-show events create immutable AuditLog entries with system attribution

## Implementation Checklist
- [ ] Create `NoShowDetectionService` (IHostedService) with 60-second timer and no-show detection logic
- [ ] Implement outage recovery logic — scan and mark overdue appointments on service startup with "delayed-detection" flag
- [ ] Modify `QueueService.MarkArrival()` to cancel no-show timer (skip in next detection cycle) on patient arrival
- [ ] Create `QueueConfigController` with GET/PUT endpoints for wait threshold (admin-only)
- [ ] Implement Redis caching (60s TTL) for threshold config with invalidation on update
- [ ] Extend `QueueEntryDto` with alert fields (isAutoNoShow, isDelayedDetection, exceedsWaitThreshold, waitAlertLevel)
- [ ] Add wait threshold alert computation in `QueueService.GetTodayQueue()` response
- [ ] Create audit log entries for auto no-show events with system attribution and detection metadata

**Traceability:** US_055 AC-1, AC-2, AC-3, AC-4 | FR-076, FR-079 | NFR-012, NFR-032 | UC-008
