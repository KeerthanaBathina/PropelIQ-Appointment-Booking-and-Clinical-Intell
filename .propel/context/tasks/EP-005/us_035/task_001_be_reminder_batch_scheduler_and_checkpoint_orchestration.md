# Task - task_001_be_reminder_batch_scheduler_and_checkpoint_orchestration

## Requirement Reference

- User Story: US_035
- Story Location: .propel/context/tasks/EP-005/us_035/us_035.md
- Acceptance Criteria:
    - AC-1: Given an appointment is scheduled for tomorrow, When the 24-hour reminder batch job runs, Then the system sends a personalized email and SMS with appointment details and cancellation link.
    - AC-2: Given an appointment is scheduled in 2 hours, When the 2-hour reminder batch job runs, Then the system sends an SMS-only reminder to the patient.
    - AC-4: Given the reminder batch job runs, When it processes all scheduled appointments, Then the entire batch completes within 10 minutes.
- Edge Case:
    - EC-1: If the reminder job fails mid-batch, the system resumes from the last successful record on next run using checkpoint tracking.
    - EC-2: All reminder scheduling uses the clinic's local timezone; patient timezone is not tracked in Phase 1.

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
| Caching / Queue | Upstash Redis | 7.x |
| Logging | Serilog | 8.x |
| Deployment Scheduler | Windows Task Scheduler | N/A |
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

Implement the reminder-batch job orchestration for EP-005. This backend task defines the scheduled worker flow for 24-hour and 2-hour reminder runs, selects eligible appointments using the clinic's local timezone, tracks progress through persistent checkpoints, and resumes safely after interruptions without reprocessing already completed appointments. The worker must keep batch execution under 10 minutes, separate 24-hour and 2-hour windows cleanly, and drive reminder dispatch through downstream notification services rather than embedding channel logic directly in the scheduler.

## Dependent Tasks

- US_032 task_001_be_smtp_provider_integration (Email transport must exist)
- US_033 task_001_be_twilio_provider_integration (SMS transport must exist)
- US_035 task_002_be_reminder_notification_dispatch_and_skip_handling (Reminder-specific dispatch must exist)
- US_018 task_002_be_appointment_booking_api (Scheduled appointments and appointment metadata must exist)
- US_019 task_002_be_appointment_cancellation_api (Cancelled appointments and status semantics must exist)
- task_003_db_reminder_checkpoint_and_notification_status_support (Checkpoint persistence and reminder status support must exist)

## Impacted Components

- **NEW** `IReminderBatchSchedulerService` / `ReminderBatchSchedulerService` - Coordinates scheduled 24-hour and 2-hour reminder runs with checkpoint resume (Server/Services/Notifications/)
- **NEW** `ReminderBatchExecutionContext` - Internal or DTO contract for batch type, timezone window, cursor, and throughput metrics (Server/Models/DTOs/)
- **NEW** `ReminderBatchWorker` - Hosted or scheduled worker entry point for reminder processing (Server/BackgroundServices/)
- **MODIFY** appointment query path - Support reminder-window selection by clinic-local timezone and appointment status (Server/Services/ or query layer)
- **MODIFY** `Program.cs` - Register reminder worker and scheduler services
- **MODIFY** operational scheduling configuration - Define 24-hour and 2-hour run cadence aligned to clinic-local time (Server configuration or deployment scheduling contract)

## Implementation Plan

1. **Create a reminder batch scheduler service** that runs distinct 24-hour and 2-hour reminder batches and delegates per-appointment dispatch to downstream reminder notification services.
2. **Translate reminder windows using the clinic's local timezone** so tomorrow and two-hours-from-now calculations are consistent even though patient timezone is not tracked in Phase 1.
3. **Process appointments in deterministic batches** ordered by stable appointment identifiers or scheduled times so checkpoint resume can continue from the last successfully processed record.
4. **Persist and restore batch checkpoints** so mid-batch failures or worker restarts resume from the last successful appointment instead of replaying the whole run.
5. **Exclude ineligible appointments up front** including cancelled rows and already-processed reminders for the same notification window.
6. **Capture batch metrics and structured logs** for processed count, skipped count, failures, resume position, and total run duration to verify the under-10-minute requirement.
7. **Keep scheduling concerns isolated** from channel delivery so future reminder or waitlist workers can reuse the same checkpointed batch pattern.

## Current Project State

```text
Server/
  Services/
    Notifications/
      NotificationEmailService.cs
      NotificationSmsService.cs
  Models/
    Entities/
      Appointment.cs
      NotificationLog.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Notifications/IReminderBatchSchedulerService.cs | Interface for checkpoint-aware reminder batch orchestration |
| CREATE | Server/Services/Notifications/ReminderBatchSchedulerService.cs | Batch runner for 24-hour and 2-hour reminder windows |
| CREATE | Server/BackgroundServices/ReminderBatchWorker.cs | Hosted or scheduled worker entry point for reminder execution |
| CREATE | Server/Models/DTOs/ReminderBatchExecutionContext.cs | Batch execution context for reminder type, window, checkpoint, and metrics |
| MODIFY | Server/Program.cs | Register reminder batch scheduling and background worker services |

## External References

- ASP.NET Core hosted services: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0
- ASP.NET Core background tasks: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/background-tasks?view=aspnetcore-8.0
- TimeZoneInfo guidance in .NET: https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Run separate checkpoint-aware 24-hour and 2-hour reminder batches
- [ ] Calculate reminder windows using the clinic's local timezone
- [ ] Resume mid-batch failures from the last successful appointment checkpoint
- [ ] Skip cancelled or already-reminded appointments before dispatch
- [ ] Keep total batch duration under 10 minutes with structured duration metrics
- [ ] Register background worker and scheduling services without embedding channel-specific send logic