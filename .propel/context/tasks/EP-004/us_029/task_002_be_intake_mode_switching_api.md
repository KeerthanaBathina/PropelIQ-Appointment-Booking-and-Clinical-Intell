# Task - task_002_be_intake_mode_switching_api

## Requirement Reference

- User Story: US_029
- Story Location: .propel/context/tasks/EP-004/us_029/us_029.md
- Acceptance Criteria:
    - AC-1: Given I am in AI intake mode, When I click "Switch to Manual Form", Then the manual form loads with all previously collected data pre-filled.
    - AC-2: Given I am in manual form mode, When I click "Switch to AI Intake", Then the AI chat resumes with context of previously entered data and continues from the next uncollected field.
    - AC-3: Given I switch modes, When the transition completes, Then no data is lost and all previously provided answers are preserved.
    - AC-4: Given I switch modes multiple times, When I eventually submit, Then the final intake contains the merged data from all modes with correct source attribution.
- Edge Case:
    - EC-1: When AI and manual values conflict, the most recent patient entry must win while the replaced value is recorded as a conflict note with source metadata.
    - EC-2: When AI service availability is degraded, the switch-to-AI operation must reject safely and expose a deterministic availability flag for the UI.

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

Implement the deterministic mode-switch orchestration APIs that sit between the AI intake session flow and the manual intake flow. This backend task delivers FR-028 and the integration portion of AIR-008 by exposing authenticated operations that switch a patient from AI to manual or manual to AI, merge field values from both modes, preserve next-question progress, expose AI availability state, and return source attribution metadata needed by the UI and final submit pipeline. The orchestration must be retry-safe, patient-scoped, and consistent with the shared `IntakeData` persistence model so repeated switching never loses collected answers.

## Dependent Tasks

- US_027 task_003_be_ai_intake_session_api (AI intake lifecycle endpoints and resume contract must exist)
- US_028 task_002_be_manual_intake_api (Manual draft load, autosave, and submit APIs must exist)
- task_003_db_intake_mode_switching_attribution (Persistence for source attribution and conflict history must exist)
- US_027 task_002_ai_conversational_intake_orchestration (AI question progression service must be callable for resume-after-switch behavior)

## Impacted Components

- **NEW** `IntakeModeSwitchController` - Patient-authenticated endpoints for switch-to-manual, switch-to-ai, and availability probing (Server/Controllers/)
- **NEW** `IIntakeModeSwitchService` / `IntakeModeSwitchService` - Merge, attribution, conflict resolution, and next-step orchestration (Server/Services/)
- **NEW** `IntakeModeSwitchResponse`, `SwitchToManualRequest`, and `SwitchToAIRequest` - DTOs describing merged state, target mode, progress, and availability flags (Server/Models/DTOs/)
- **MODIFY** `AIIntakeSessionService` - Reuse resume logic for next-uncollected-field continuation after switching from manual (Server/Services/)
- **MODIFY** `ManualIntakeService` - Reuse draft hydration and prefilled field metadata when switching from AI (Server/Services/)
- **MODIFY** `Program.cs` - Register mode-switch orchestration services

## Implementation Plan

1. **Add dedicated mode-switch endpoints** for AI-to-manual and manual-to-AI transitions so switch behavior is explicit and not hidden inside unrelated session endpoints.
2. **Implement deterministic merge rules** that preserve all previously entered values, select the most recent answer on conflict, and attach source-attribution metadata for each merged field.
3. **Resume AI at the next missing field** by combining manual-form data with the existing AI session snapshot and computing the next uncollected required or optional prompt.
4. **Hydrate manual form drafts from AI progress** by returning a normalized field payload with prefill indicators and conflict notes for values overwritten by newer manual entries.
5. **Expose AI availability status** from the orchestration layer so the UI can disable switch-to-AI before the patient triggers a failing transition, while still safely rejecting stale requests.
6. **Make switch operations idempotent and patient-scoped** with correlation IDs, authorization checks, and safe retry behavior for browser refresh or back-button replays.
7. **Emit structured logging and OpenAPI metadata** for successful switches, rejected AI-unavailable attempts, conflict merges, and final merged-state handoffs.

## Current Project State

```text
Server/
  Controllers/
    AIIntakeController.cs
    ManualIntakeController.cs
  Services/
    AIIntakeSessionService.cs
    ManualIntakeService.cs
  Models/
    DTOs/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/IntakeModeSwitchController.cs | Endpoints for switch-to-manual, switch-to-ai, and AI availability checks |
| CREATE | Server/Services/IIntakeModeSwitchService.cs | Interface for deterministic mode-switch orchestration |
| CREATE | Server/Services/IntakeModeSwitchService.cs | Merge logic, attribution, conflict handling, and target-state orchestration |
| CREATE | Server/Models/DTOs/IntakeModeSwitchResponse.cs | Response contract containing merged values, conflict notes, progress, and target mode |
| CREATE | Server/Models/DTOs/SwitchToManualRequest.cs | Contract for AI-to-manual mode switch requests |
| CREATE | Server/Models/DTOs/SwitchToAIRequest.cs | Contract for manual-to-AI mode switch requests |
| MODIFY | Server/Services/AIIntakeSessionService.cs | Reuse next-question resume logic after manual-to-AI switch |
| MODIFY | Server/Services/ManualIntakeService.cs | Reuse normalized prefill projection for AI-to-manual switch |
| MODIFY | Server/Program.cs | Register mode-switch orchestration services |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- ASP.NET Core authorization: https://learn.microsoft.com/en-us/aspnet/core/security/authorization/introduction?view=aspnetcore-8.0
- ASP.NET Core health checks: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Add explicit AI-to-manual and manual-to-AI endpoints with patient-only authorization
- [ ] Merge AI and manual values without data loss and apply most-recent-entry-wins conflict resolution
- [ ] Return source attribution and conflict notes needed by the UI and final submit flow
- [ ] Resume AI intake at the next uncollected field after switching from manual mode
- [ ] Expose and enforce deterministic AI availability state for switch-to-AI behavior
- [ ] Make switch operations retry-safe and robust to browser refresh or replay
- [ ] Add structured logging and OpenAPI metadata for all switch outcomes