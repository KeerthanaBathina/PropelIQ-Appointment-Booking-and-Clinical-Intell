# Task - task_002_be_arrival_status_api

## Task ID

- ID: task_002_be_arrival_status_api

## Task Title

- Implement Arrival Status API Endpoints & Services

## Parent User Story

- **User Story**: US_052 — Patient Arrival Status Marking
- **Epic**: EP-009

## Description

Implement the backend API layer for patient arrival status management using .NET 8 ASP.NET Core Web API with layered architecture (Controller → Service → Data Access). Includes queue retrieval, arrival marking, cancellation with slot release, automated no-show detection via background service, arrived-late override with audit logging, duplicate arrival prevention, and Redis cache integration for sub-second response times.

## Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Framework | ASP.NET Core Web API | 8.x |
| ORM | Entity Framework Core | 8.x |
| Caching | Upstash Redis | 7.x |
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| Resilience | Polly | 8.x |
| Logging | Serilog | 8.x |

## Acceptance Criteria Mapping

| AC | Description | Coverage |
|----|-------------|----------|
| AC-1 | Staff marks patient as "arrived" → system records arrival timestamp and begins wait time calculation | POST /queue/arrive creates QueueEntry with arrival_timestamp = DateTime.UtcNow |
| AC-2 | 15 minutes elapse past appointment time → system auto-marks no-show and updates queue | NoShowDetectionService (IHostedService) scans every 60s for overdue appointments |
| AC-3 | Staff marks "cancelled" → status updates immediately and slot released for walk-ins | PUT /queue/{queueId}/status with slot release logic in AppointmentService |
| AC-4 | Wait time calculated in real-time from arrival timestamp to current time | QueueService.CalculateWaitTime computes diff on each GET /queue/today response |

## Edge Cases

| Edge Case | Implementation |
|-----------|----------------|
| No-show override to arrived-late | PUT /queue/{queueId}/override validates current status is no_show, transitions to arrived_late, requires reason string, logs to AuditService |
| Duplicate arrival marking | QueueService checks for existing QueueEntry with status=waiting for appointment_id; returns 409 Conflict with timestamp |
| Concurrent status updates | Optimistic locking via QueueEntry version field (TR-015) with EF Core concurrency token |
| Background service failure | NoShowDetectionService implements retry with exponential backoff; logs errors to Serilog; health check reports degraded status |

## Implementation Checklist

- [ ] Create `QueueController` with `GET /queue/today` endpoint returning sorted queue entries (by appointment_time + priority) with computed wait times; include OpenAPI annotations via Swashbuckle (`[ProducesResponseType]`, `[SwaggerOperation]`)
- [ ] Implement `POST /queue/arrive` endpoint in `QueueController` accepting `{ appointmentId }` body; delegate to `QueueService.MarkArrivalAsync` which creates/updates `QueueEntry` with `arrival_timestamp = DateTime.UtcNow` and `status = waiting`; return 201 with queue position
- [ ] Implement `PUT /queue/{queueId}/status` endpoint for cancellation; `QueueService.UpdateStatusAsync` sets `QueueEntry.status = cancelled` and `cancelled_at = DateTime.UtcNow`; call `AppointmentService.ReleaseSlotAsync` to update `Appointment.status = cancelled` and make slot available for walk-ins
- [ ] Build `NoShowDetectionService` as `BackgroundService` (IHostedService) that runs every 60 seconds; queries appointments where `appointment_time + 15 min < DateTime.UtcNow` AND `status = scheduled` AND no associated `QueueEntry` with `status = waiting`; marks `Appointment.status = no_show` and logs via `AuditService`
- [ ] Implement `PUT /queue/{queueId}/override` endpoint for arrived-late override; validate current `Appointment.status == no_show`; create `QueueEntry` with `status = arrived_late`, `arrival_timestamp = DateTime.UtcNow`, `override_reason = request.reason`; log state transition in `AuditService` with before/after status and staff user ID
- [ ] Add duplicate arrival validation in `QueueService.MarkArrivalAsync`: query `QueueEntry` by `appointment_id` where `status IN (waiting, arrived_late)`; if exists, throw `ConflictException` with message "Patient already marked as arrived at {existingEntry.ArrivalTimestamp:g}"
- [ ] Implement Redis cache layer: cache `GET /queue/today` response with key `queue:today:{date}` and 5-minute TTL (NFR-030); invalidate cache on any queue mutation (arrive, cancel, override, no-show) via `IDistributedCache.RemoveAsync`; use Polly circuit breaker for Redis connectivity failures
- [ ] Add audit trail logging for all arrival status changes: inject `IAuditService` into `QueueService`; log event type (arrival, cancellation, no_show_auto, no_show_override), entity IDs (queue_id, appointment_id, patient_id), before/after status, staff user ID from JWT claims, and correlation ID from request headers (TR-028)

## Effort Estimate

- **Estimated Hours**: 7
- **Complexity**: Medium-High

## Dependencies

| Dependency | Type | Description |
|------------|------|-------------|
| task_003_db_arrival_status_schema | Internal | Database schema migration must be applied before API can operate on extended entities |
| US_008 | External | QueueEntry and Appointment EF Core entities and DbContext must exist |
| US_004 | External | Redis cache infrastructure (IDistributedCache) must be configured |

## API Endpoint Specifications

### GET /queue/today

| Attribute | Value |
|-----------|-------|
| Method | GET |
| Route | `/api/v1/queue/today` |
| Auth | JWT (Staff role) |
| Cache | Redis 5-min TTL |
| Response | 200: QueueEntryDto[] sorted by appointment_time + priority |

### POST /queue/arrive

| Attribute | Value |
|-----------|-------|
| Method | POST |
| Route | `/api/v1/queue/arrive` |
| Auth | JWT (Staff role) |
| Body | `{ "appointmentId": "uuid" }` |
| Response | 201: QueueEntryDto with queue position |
| Error | 409: Conflict (duplicate arrival) |
| Error | 404: Appointment not found |
| Idempotency | Idempotent via appointment_id uniqueness check (NFR-034) |

### PUT /queue/{queueId}/status

| Attribute | Value |
|-----------|-------|
| Method | PUT |
| Route | `/api/v1/queue/{queueId}/status` |
| Auth | JWT (Staff role) |
| Body | `{ "status": "cancelled" }` |
| Response | 200: Updated QueueEntryDto |
| Error | 404: Queue entry not found |
| Error | 422: Invalid status transition |

### PUT /queue/{queueId}/override

| Attribute | Value |
|-----------|-------|
| Method | PUT |
| Route | `/api/v1/queue/{queueId}/override` |
| Auth | JWT (Staff role) |
| Body | `{ "newStatus": "arrived_late", "reason": "string (min 10 chars)" }` |
| Response | 200: Updated QueueEntryDto with audit confirmation |
| Error | 404: Queue entry or appointment not found |
| Error | 422: Appointment status is not no_show |

## Architecture Notes

### Layered Architecture (TR-009)

```
QueueController (Presentation)
    ↓
QueueService / AppointmentService (Business Logic)
    ↓
QueueRepository / AppointmentRepository (Data Access)
    ↓
PostgreSQL via EF Core
```

### Background Service Pattern

```
NoShowDetectionService : BackgroundService
    → Timer: every 60 seconds
    → Query: overdue appointments (15 min threshold)
    → Action: mark no-show + audit log
    → Error: Serilog error + retry on next cycle
    → Health: IHealthCheck reports degraded if 3 consecutive failures
```

### Request Correlation (TR-028)

All endpoints propagate `X-Correlation-Id` header via middleware. Correlation ID included in all audit log entries and Serilog structured log properties.

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

## Traceability

| Reference | IDs |
|-----------|-----|
| Acceptance Criteria | AC-1, AC-2, AC-3, AC-4 |
| Functional Requirements | FR-071, FR-072 |
| Data Requirements | DR-008 |
| Technical Requirements | TR-007 (Redis), TR-009 (layered arch), TR-011 (RESTful), TR-015 (optimistic locking), TR-027 (rate limiting), TR-028 (correlation IDs) |
| Non-Functional Requirements | NFR-004 (sub-second cached views), NFR-030 (Redis 5-min TTL), NFR-034 (idempotent endpoints), NFR-038 (OpenAPI docs) |
