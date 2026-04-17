# Task - task_001_be_smtp_provider_integration

## Requirement Reference

- User Story: US_032
- Story Location: .propel/context/tasks/EP-005/us_032/us_032.md
- Acceptance Criteria:
    - AC-1: Given SMTP is configured, When an email is triggered, Then it is sent via the configured SMTP provider within 30 seconds.
    - AC-3: Given the SMTP provider is unreachable, When delivery fails, Then the system retries up to 3 times with exponential backoff (1s, 2s, 4s) and logs each attempt.
- Edge Case:
    - EC-1: If the SendGrid free tier limit is exceeded, fall back to Gmail SMTP and alert admin.
    - EC-2: If the recipient email is invalid, propagate a bounced outcome so downstream logging can flag the patient record for contact update.

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
| Email Service | SMTP (SendGrid Free Tier or Gmail SMTP) | N/A |
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

Implement the SMTP provider integration layer for EP-005 notifications. This backend task establishes SendGrid and Gmail SMTP connectivity behind a unified provider abstraction, enforces the 30-second delivery target, retries transient failures with exponential backoff, and fails over to Gmail when SendGrid quota or availability limits are hit. The integration must use centralized configuration, structured attempt logging, and patient-safe error handling so higher-level notification workflows can rely on a consistent delivery contract.

## Dependent Tasks

- US_001 - Foundational - Backend scaffold and DI container must exist
- US_008 - Foundational - `NotificationLog` entity must exist for downstream status persistence
- task_002_be_notification_email_composition_and_logging (Delivery orchestration will consume the provider abstraction from this task)

## Impacted Components

- **NEW** `IEmailTransport` / `SmtpEmailTransport` - Low-level SMTP provider abstraction for SendGrid and Gmail delivery (Server/Services/Notifications/)
- **NEW** `EmailProviderOptions` - Centralized SMTP configuration model with primary and fallback provider settings (Server/Configuration/)
- **NEW** `EmailDeliveryAttemptResult` - Transport-level result contract indicating sent, failed, bounced, or fallback outcomes (Server/Models/DTOs/)
- **MODIFY** `Program.cs` - Register SMTP options, resilience policies, and transport services
- **MODIFY** `appsettings*.json` expectation - Add provider configuration structure for SendGrid and Gmail SMTP credentials and limits

## Implementation Plan

1. **Create a unified SMTP transport abstraction** that hides provider-specific details for SendGrid and Gmail while exposing a single send contract to the notification layer.
2. **Bind provider settings from centralized configuration** using appsettings plus environment overrides for host, port, TLS, credentials, sender identity, and free-tier quotas.
3. **Implement retry behavior with exponential backoff** at 1s, 2s, and 4s for transient connectivity failures, logging each attempt with correlation data.
4. **Detect SendGrid quota or availability exhaustion** and automatically fail over to Gmail SMTP for the same delivery request when allowed by policy.
5. **Differentiate invalid-recipient failures** from transient transport failures so downstream orchestration can mark delivery as bounced instead of retryable.
6. **Emit structured provider-level diagnostics** for primary send success, retries, failover activation, and final failure outcomes without leaking credentials or patient PII.
7. **Register the transport and resilience services** so later appointment confirmation and reminder workflows can consume them through DI.

## Current Project State

```text
Server/
  Services/
  Models/
    Entities/
      NotificationLog.cs
  Program.cs
appsettings.json
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/Notifications/IEmailTransport.cs | Interface for SMTP delivery abstraction |
| CREATE | Server/Services/Notifications/SmtpEmailTransport.cs | SendGrid/Gmail SMTP transport with retry and fallback support |
| CREATE | Server/Configuration/EmailProviderOptions.cs | Strongly typed SMTP provider configuration model |
| CREATE | Server/Models/DTOs/EmailDeliveryAttemptResult.cs | Transport-level send attempt result contract |
| MODIFY | Server/Program.cs | Register SMTP options, transport services, and resilience policies |
| MODIFY | appsettings.json | Add SMTP provider configuration placeholders for primary and fallback providers |

## External References

- ASP.NET Core options pattern: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-8.0
- .NET MailKit/SmtpClient guidance: https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient
- Polly resilience pipelines (.NET 8): https://learn.microsoft.com/en-us/dotnet/core/resilience/
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Add a unified SMTP transport abstraction for SendGrid and Gmail
- [ ] Load SMTP provider configuration from centralized appsettings with environment overrides
- [ ] Retry transient failures with 1s, 2s, and 4s backoff and log each attempt
- [ ] Fail over from SendGrid to Gmail when provider quota or availability limits are hit
- [ ] Distinguish bounced invalid-recipient outcomes from retryable transport failures
- [ ] Register transport services and structured diagnostics without exposing secrets