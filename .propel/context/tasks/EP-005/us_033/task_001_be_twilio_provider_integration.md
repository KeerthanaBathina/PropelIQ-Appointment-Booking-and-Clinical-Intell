# Task - task_001_be_twilio_provider_integration

## Requirement Reference

- User Story: US_033
- Story Location: .propel/context/tasks/EP-005/us_033/us_033.md
- Acceptance Criteria:
    - AC-1: Given Twilio is configured, When an SMS is triggered, Then it is sent to the patient's phone number within 30 seconds.
    - AC-3: Given SMS delivery fails, When the failure is detected, Then the system retries up to 3 times with exponential backoff and logs each attempt.
    - AC-4: Given delivery succeeds, When Twilio confirms delivery, Then status is logged as "sent" in NotificationLog.
- Edge Case:
    - EC-1: If Twilio trial credits are exhausted, disable SMS, log an alert, and continue with email-only notifications.
    - EC-2: If the phone number is international, reject it with validation because Phase 1 supports US `+1` numbers only.

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
| Resilience | Polly | 8.x |
| Logging | Serilog | 8.x |
| API Documentation | Swagger (Swashbuckle) | 6.x |
| SMS Gateway | Twilio API | 2023-05 |
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

Implement the Twilio provider integration layer for SMS notifications. This backend task establishes a reusable Twilio transport abstraction with US-number validation, 30-second send target enforcement, retry behavior with exponential backoff, and an operational disable path when trial credits are exhausted. The provider layer must return clear delivery attempt results so higher-level SMS workflows can persist final outcomes and skip SMS cleanly when the gateway is unavailable.

## Dependent Tasks

- US_001 - Foundational - Backend scaffold and DI container must exist
- task_002_be_notification_sms_orchestration_and_logging (Notification-layer SMS orchestration will consume this provider abstraction)
- task_003_db_sms_opt_out_and_notification_status (Opt-out and status persistence must exist for downstream logging)

## Impacted Components

- **NEW** `ISmsTransport` / `TwilioSmsTransport` - Low-level Twilio delivery abstraction with retry and provider-disable behavior (Server/Services/Notifications/)
- **NEW** `SmsProviderOptions` - Centralized Twilio configuration model for SID, auth token, sender number, and operational flags (Server/Configuration/)
- **NEW** `SmsDeliveryAttemptResult` - Transport-level result contract indicating sent, failed, opted-out, or gateway-disabled outcomes (Server/Models/DTOs/)
- **MODIFY** `Program.cs` - Register Twilio options, transport services, and resilience policies
- **MODIFY** `appsettings.json` expectation - Add Twilio configuration placeholders and feature-toggle settings

## Implementation Plan

1. **Create a Twilio transport abstraction** so notification orchestration can send SMS without depending directly on Twilio SDK details.
2. **Bind Twilio settings from centralized configuration** including account SID, auth token, from number, and a runtime flag that can disable SMS when credits are exhausted.
3. **Validate phone-number scope up front** by accepting only US `+1` numbers in Phase 1 and returning deterministic validation errors for international numbers.
4. **Implement retry behavior with exponential backoff** for transient Twilio failures, logging each attempt with correlation data.
5. **Detect exhausted trial-credit or disabled-gateway conditions** and return a gateway-disabled result so the system can continue with email-only notifications.
6. **Emit provider-level structured diagnostics** for successful sends, retries, hard validation failures, and gateway disablement without leaking secrets.
7. **Register transport and resilience services** so downstream confirmation and reminder workflows can use them through DI.

## Current Project State

```text
Server/
  Services/
  Program.cs
appsettings.json
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Notifications/ISmsTransport.cs | Interface for Twilio SMS delivery abstraction |
| CREATE | Server/Services/Notifications/TwilioSmsTransport.cs | Twilio transport with retries, US-number validation, and gateway disable logic |
| CREATE | Server/Configuration/SmsProviderOptions.cs | Strongly typed Twilio configuration model |
| CREATE | Server/Models/DTOs/SmsDeliveryAttemptResult.cs | Transport-level SMS delivery result contract |
| MODIFY | Server/Program.cs | Register Twilio options, transport services, and resilience policies |
| MODIFY | appsettings.json | Add Twilio configuration placeholders and SMS enablement settings |

## External References

- Twilio messaging API docs: https://www.twilio.com/docs/messaging/api
- ASP.NET Core options pattern: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0
- Polly resilience pipelines (.NET 8): https://learn.microsoft.com/en-us/dotnet/core/resilience/
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [X] Add a Twilio transport abstraction for SMS delivery
- [X] Load Twilio configuration from centralized appsettings with environment overrides
- [X] Accept only US `+1` numbers in Phase 1 and reject international numbers deterministically
- [X] Retry transient SMS failures with exponential backoff and log each attempt
- [X] Disable SMS and surface gateway-disabled outcomes when Twilio trial credits are exhausted
- [X] Register transport services and structured diagnostics without exposing secrets