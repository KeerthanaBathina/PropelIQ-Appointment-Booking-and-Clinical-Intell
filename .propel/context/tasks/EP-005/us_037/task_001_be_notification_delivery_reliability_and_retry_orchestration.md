# Task - task_001_be_notification_delivery_reliability_and_retry_orchestration

## Requirement Reference

- User Story: US_037
- Story Location: .propel/context/tasks/EP-005/us_037/us_037.md
- Acceptance Criteria:
    - AC-1: Given any notification is sent, When delivery completes, Then the system logs the attempt with status, timestamp, channel, and recipient.
    - AC-2: Given a notification delivery fails, When the failure is detected, Then the system retries up to 3 times with exponential backoff at 1 minute, 5 minutes, and 15 minutes.
    - AC-3: Given all retry attempts fail, When the final retry fails, Then the system marks the notification as `permanently_failed` and flags it for staff review.
- Edge Case:
    - EC-1: If the logging persistence path is unavailable, queue attempt records in memory with a hard limit of 1000 entries and flush them when persistence recovers.
    - EC-2: Bounced email outcomes must flag the patient for contact validation, while retryable failed outcomes continue through the retry schedule.

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
| Logging | Serilog | 8.x |
| Resilience | Polly | 8.x |
| Email Service | SMTP (SendGrid Free Tier or Gmail SMTP) | N/A |
| SMS Gateway | Twilio API | 2023-05 |
| Caching | Upstash Redis | 7.x |
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

Implement the orchestration layer that makes notification delivery observable and resilient across email and SMS channels. This backend task sits above the existing provider-level retry behavior so every delivery attempt is recorded, retryable failures are rescheduled using the story-level 1-minute, 5-minute, and 15-minute backoff policy, bounced emails are treated as contact-quality problems instead of retry candidates, and notifications that exhaust all retries are marked `permanently_failed` and surfaced for staff follow-up. The same orchestration must continue functioning when attempt persistence is temporarily unavailable by buffering up to 1000 pending log writes in memory and flushing them when storage recovers.

## Dependent Tasks

- US_032 task_002_be_notification_email_composition_and_logging (Email orchestration and bounced-email handling baseline must exist)
- US_033 task_002_be_notification_sms_orchestration_and_logging (SMS orchestration and failure detection baseline must exist)
- US_035 task_003_db_reminder_checkpoint_and_notification_status_support (Current expanded notification status baseline must exist)
- task_002_db_notification_delivery_attempt_and_status_persistence (Delivery-attempt persistence and `permanently_failed` support must exist)

## Impacted Components

- **NEW** `INotificationDeliveryReliabilityService` / `NotificationDeliveryReliabilityService` - Coordinates attempt logging, retry scheduling, permanent-failure handling, and staff-review escalation (Server/Services/Notifications/)
- **NEW** `NotificationRetryWorker` - Executes scheduled retry attempts at 1-minute, 5-minute, and 15-minute intervals (Server/BackgroundServices/)
- **NEW** `BufferedNotificationLogWriter` - Holds attempt records in a bounded in-memory queue and flushes them after persistence recovers (Server/Services/Notifications/)
- **NEW** `NotificationRetryRequest` - Contract describing notification identifier, channel payload reference, retry attempt number, and next-attempt timing (Server/Models/DTOs/)
- **MODIFY** `NotificationEmailService` - Delegate terminal delivery outcomes and bounced-email classification to the reliability orchestration path (Server/Services/Notifications/)
- **MODIFY** `NotificationSmsService` - Delegate retryable SMS failures and final outcomes to the reliability orchestration path (Server/Services/Notifications/)
- **MODIFY** `PatientService` - Flag patient contact validation when email bounces or recipient data is invalid (Server/Services/)
- **MODIFY** `Program.cs` - Register retry orchestration, background worker, and buffered log-writer services

## Implementation Plan

1. **Create a notification delivery reliability service** that receives final provider outcomes from email and SMS orchestration, writes attempt records, and decides whether the result is terminal, retryable, or contact-validation related.
2. **Separate transport retries from orchestration retries** by preserving the existing short provider-level retries while adding story-level retry scheduling only after the channel service returns a failed delivery outcome.
3. **Schedule retryable failures with explicit backoff windows** of 1 minute, 5 minutes, and 15 minutes, ensuring retry state is durable and deterministic across worker restarts.
4. **Treat bounced or invalid-recipient email outcomes as non-retryable** by flagging the patient for contact validation and ending the delivery flow without consuming the retry schedule.
5. **Mark notifications `permanently_failed` after the final retry attempt** and set a staff-review signal so operations teams can intervene without silently losing communications.
6. **Buffer attempt-log writes when persistence is unavailable** by using a bounded in-memory queue capped at 1000 entries, flushing records in order when storage recovers, and emitting an operational alert if the buffer reaches capacity.
7. **Keep the orchestration reusable across confirmations, reminders, waitlist offers, and slot-swap notifications** by passing channel-agnostic metadata instead of embedding story-specific rules in the worker.

## Current Project State

```text
Server/
  Services/
    Notifications/
      NotificationEmailService.cs
      NotificationSmsService.cs
  BackgroundServices/
  Models/
    Entities/
      NotificationLog.cs
      Patient.cs
  Program.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Notifications/INotificationDeliveryReliabilityService.cs | Interface for cross-channel attempt logging, retry scheduling, and permanent-failure handling |
| CREATE | Server/Services/Notifications/NotificationDeliveryReliabilityService.cs | Orchestrate retry decisions, buffered log writes, and staff-review escalation |
| CREATE | Server/Services/Notifications/BufferedNotificationLogWriter.cs | Buffer up to 1000 attempt records when persistence is unavailable and flush in order on recovery |
| CREATE | Server/BackgroundServices/NotificationRetryWorker.cs | Process 1-minute, 5-minute, and 15-minute retry schedules for failed deliveries |
| CREATE | Server/Models/DTOs/NotificationRetryRequest.cs | Retry scheduling payload for durable notification re-dispatch |
| MODIFY | Server/Services/Notifications/NotificationEmailService.cs | Route bounced or failed outcomes through reliability orchestration |
| MODIFY | Server/Services/Notifications/NotificationSmsService.cs | Route retryable failures and final outcomes through reliability orchestration |
| MODIFY | Server/Services/PatientService.cs | Flag patient contact validation for bounced or invalid email outcomes |
| MODIFY | Server/Program.cs | Register retry orchestration and background processing services |

## External References

- ASP.NET Core hosted services: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-8.0
- Polly resilience pipelines: https://www.pollydocs.org/
- EF Core concurrency and transactions: https://learn.microsoft.com/en-us/ef/core/saving/transactions
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Log every email and SMS delivery attempt with channel, recipient, status, and attempt timestamp
- [ ] Preserve existing short transport retries while adding orchestration retries at 1 minute, 5 minutes, and 15 minutes
- [ ] Treat bounced email outcomes as contact-validation events rather than retryable failures
- [ ] Mark notifications `permanently_failed` after the final retry and flag them for staff review
- [ ] Buffer pending attempt-log writes in memory with a hard cap of 1000 when persistence is unavailable
- [ ] Flush buffered attempt logs safely when persistence recovers without duplicating final outcomes
- [ ] Keep the orchestration reusable across all EP-005 notification trigger types