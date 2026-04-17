# Task - task_003_be_ai_intake_session_api

## Requirement Reference

- User Story: US_027
- Story Location: .propel/context/tasks/EP-004/us_027/us_027.md
- Acceptance Criteria:
    - AC-1: Given I select "AI-Assisted Intake" from my dashboard, When the chat interface loads, Then the AI greets me and explains the intake process with a progress indicator.
    - AC-2: Given the AI asks a question, When I provide a response, Then the AI validates my answer, asks clarifying follow-ups if needed, and moves to the next question within 1 second.
    - AC-4: Given the intake session is active, When the AI displays a summary, Then I can review all collected information and correct any errors before submission.
    - AC-5: Given the conversation progresses, When I look at the progress bar, Then it accurately reflects the number of fields collected out of total required (e.g., "4/8 fields").
- Edge Case:
    - EC-1: Ambiguous patient responses must round-trip through the AI orchestration and return clarification prompts with examples.
    - EC-2: Session timeout recovery must restore the last auto-saved state and resume from the last completed question.

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
| Caching | Upstash Redis | 7.x |
| Authentication | ASP.NET Core Identity + JWT | 8.x |
| Logging | Serilog | 8.x |
| API Documentation | Swagger (Swashbuckle) | 6.x |
| AI/ML | OpenAI GPT-4o-mini + Claude 3.5 Sonnet | 2024-07-18 / claude-3-5-sonnet-20241022 |
| Vector Store | PostgreSQL pgvector | 0.5.x |
| AI Gateway | Custom .NET Service with Polly | 8.x |
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

Implement the deterministic session and message API layer for conversational intake. This backend task exposes the authenticated endpoints that start or resume AI intake sessions, accept patient messages, return the next AI prompt plus progress metadata, provide the review summary, support switch-to-manual handoff, and finalize submission into `IntakeData`. The API layer must coordinate the AI orchestration without embedding prompt logic directly in controllers, preserve sub-second exchange latency targets, and remain robust under session timeout and autosave recovery scenarios.

## Dependent Tasks

- task_002_ai_conversational_intake_orchestration (AI prompt, retrieval, and field-extraction logic must exist)
- task_004_db_ai_intake_session_persistence (Session state and autosave persistence must exist)
- US_028 - Same-Epic - Manual intake flow must accept carried-over AI session data for mode switching
- US_008 - Foundational - IntakeData persistence model must exist

## Impacted Components

- **NEW** `AIIntakeController` - Authenticated endpoints for start, resume, message exchange, summary retrieval, switch-to-manual, and completion (Server/Controllers/)
- **NEW** `IAIIntakeSessionService` / `AIIntakeSessionService` - Deterministic orchestration around session lifecycle, persistence, and AI service invocation (Server/Services/)
- **NEW** `StartAIIntakeResponse`, `AIIntakeMessageRequest`, `AIIntakeMessageResponse`, and summary DTOs - Contracts for chat exchange and review state (Server/Models/DTOs/)
- **MODIFY** `Program.cs` - Register AI intake services and dependencies
- **MODIFY** manual intake integration path - Accept carried-over collected fields when switching modes (Server/Services/ or Server/Controllers/)

## Implementation Plan

1. **Add session lifecycle endpoints** for start/resume so the UI can obtain the greeting, session identifier, saved progress, and next-question metadata in one deterministic contract.
2. **Add a message-exchange endpoint** that accepts patient text, invokes the conversational AI service, persists the resulting state, and returns the next AI prompt plus progress counters.
3. **Provide a review-summary endpoint** that returns the collected mandatory and optional fields in a correction-friendly structure before final submission.
4. **Implement completion and handoff endpoints** so patients can either finalize AI intake or switch to the manual form without losing collected data.
5. **Support timeout recovery** by resuming the most recent active session, restoring the last auto-saved state, and rejecting stale or unauthorized session IDs safely.
6. **Protect performance and security** with authenticated patient-only access, correlation IDs, and bounded payload sizes for message exchange.
7. **Add structured logging and OpenAPI metadata** for session start, resume, autosave restore, manual handoff, AI exchange failures, and completion outcomes.

## Current Project State

```text
Server/
  Controllers/
    AppointmentBookingController.cs
  Services/
    AppointmentBookingService.cs
  Models/
    Entities/
      IntakeData.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/AIIntakeController.cs | Patient-authenticated AI intake session and message endpoints |
| CREATE | Server/Services/IAIIntakeSessionService.cs | Interface for AI intake session lifecycle orchestration |
| CREATE | Server/Services/AIIntakeSessionService.cs | Session lifecycle, autosave restore, summary, and completion orchestration |
| CREATE | Server/Models/DTOs/AIIntakeMessageRequest.cs | Patient message request contract for conversational intake |
| CREATE | Server/Models/DTOs/AIIntakeMessageResponse.cs | Next-question, progress, and summary-state response contract |
| CREATE | Server/Models/DTOs/StartAIIntakeResponse.cs | Start/resume response containing session and greeting metadata |
| MODIFY | Server/Program.cs | Register conversational intake API services and dependencies |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- ASP.NET Core model binding: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-8.0
- ASP.NET Core authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Add start and resume endpoints that return greeting, session identifier, and current progress state
- [ ] Add a message endpoint that round-trips patient responses through AI orchestration and persists the resulting state
- [ ] Return summary-review data that supports correction before final submission
- [ ] Support switch-to-manual and final-complete flows without losing previously collected data
- [ ] Restore the last auto-saved session state after timeout or return visit
- [ ] Enforce patient-only access, payload bounds, and structured request tracing on all session endpoints
- [ ] Add OpenAPI docs and structured logs for session lifecycle and AI exchange outcomes