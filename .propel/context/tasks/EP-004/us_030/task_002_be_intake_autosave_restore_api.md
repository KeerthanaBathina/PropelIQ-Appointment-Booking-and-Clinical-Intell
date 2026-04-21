# Task - task_002_be_intake_autosave_restore_api

## Requirement Reference

- User Story: US_030
- Story Location: .propel/context/tasks/EP-004/us_030/us_030.md
- Acceptance Criteria:
    - AC-1: Given I am filling intake (AI or manual), When 30 seconds elapse since the last change, Then the system saves current progress to the server and displays "Auto-saved" indicator.
    - AC-2: Given my session is interrupted (timeout, network loss), When I return to the intake, Then the system restores the last auto-saved state with all fields populated.
    - AC-3: Given the auto-save triggers, When the save completes, Then the auto-save indicator briefly appears and fades, confirming the save without disrupting the user flow.
- Edge Case:
    - EC-1: If autosave fails due to transient network loss, the API contract must support one client retry without creating duplicate or regressed draft state.
    - EC-2: When rapid changes occur within a 30-second window, only the last draft snapshot sent at the boundary should be persisted as the current autosaved state.

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
| AI/ML | N/A | N/A |
| Vector Store | N/A | N/A |
| AI Gateway | N/A | N/A |
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

Implement the shared autosave and restore API behavior for EP-004 intake drafts. This backend task delivers the deterministic service slice behind FR-035 by ensuring AI and manual intake flows can persist the latest draft snapshot, restore the most recent autosaved state after interruption, and safely accept one retry without corrupting draft order or losing data. The API layer should reuse the shared `IntakeData` persistence introduced earlier and focus on consistent contracts, idempotent updates, and structured diagnostics for restore failures.

## Dependent Tasks

- US_027 task_003_be_ai_intake_session_api (AI intake draft endpoints and resume contract must exist)
- US_028 task_002_be_manual_intake_api (Manual intake autosave and draft endpoints must exist)
- US_027 task_004_db_ai_intake_session_persistence (Shared draft persistence and last-autosaved metadata must exist)
- US_008 - Foundational - `IntakeData` entity must exist

## Impacted Components

- **MODIFY** `AIIntakeSessionService` - Normalize autosave snapshot writes, retry-safe updates, and restore projection for conversational intake (Server/Services/)
- **MODIFY** `ManualIntakeService` - Normalize autosave snapshot writes and restore projection for manual intake (Server/Services/)
- **NEW** `IIntakeAutosaveService` / `IntakeAutosaveService` - Shared draft snapshot persistence and restore orchestration reused by both intake modes (Server/Services/)
- **NEW** `AutosaveDraftRequest` / `AutosaveDraftResponse` - Common DTOs for autosave timestamping, snapshot versioning, and restore metadata (Server/Models/DTOs/)
- **MODIFY** `AIIntakeController` - Reuse shared autosave contracts and expose restore-ready metadata in start or resume responses (Server/Controllers/)
- **MODIFY** `ManualIntakeController` - Reuse shared autosave contracts for draft save and restore responses (Server/Controllers/)
- **MODIFY** `Program.cs` - Register shared autosave orchestration service

## Implementation Plan

1. **Introduce a shared autosave service** so AI and manual intake flows persist draft snapshots through one deterministic path instead of maintaining separate autosave rules.
2. **Persist the latest snapshot only** by accepting boundary-save payloads from the client and replacing the current autosaved draft atomically, rather than recording every intermediate edit.
3. **Return restore-ready metadata** including last autosave timestamp and normalized draft payload so either intake surface can repopulate fields after timeout or interruption.
4. **Make autosave writes idempotent and retry-safe** so one repeated request after network failure does not create duplicate side effects or roll the draft backward.
5. **Preserve partial-draft integrity** by validating patient ownership, payload bounds, and stale snapshot ordering before writing autosaved state.
6. **Emit structured logs with correlation IDs** for autosave success, retry replay, restore operations, and failed draft writes to support troubleshooting.

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
    Entities/
      IntakeData.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/IIntakeAutosaveService.cs | Interface for shared intake autosave and restore orchestration |
| CREATE | Server/Services/IntakeAutosaveService.cs | Shared autosave snapshot persistence and restore logic for both intake modes |
| CREATE | Server/Models/DTOs/AutosaveDraftRequest.cs | Request contract for boundary autosave snapshots |
| CREATE | Server/Models/DTOs/AutosaveDraftResponse.cs | Response contract containing autosave timestamp and restore metadata |
| MODIFY | Server/Services/AIIntakeSessionService.cs | Reuse shared autosave service and expose normalized restore metadata |
| MODIFY | Server/Services/ManualIntakeService.cs | Reuse shared autosave service and expose normalized restore metadata |
| MODIFY | Server/Controllers/AIIntakeController.cs | Align autosave and restore responses to the shared contract |
| MODIFY | Server/Controllers/ManualIntakeController.cs | Align autosave and restore responses to the shared contract |
| MODIFY | Server/Program.cs | Register shared intake autosave services |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- ASP.NET Core model binding: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/model-binding?view=aspnetcore-8.0
- ASP.NET Core idempotency guidance: https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotent-message-processing
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [x] Reuse a shared autosave service for AI and manual intake draft persistence
- [x] Persist only the latest 30-second boundary snapshot as the current autosaved draft
- [x] Return the last autosaved state and timestamp for interruption recovery
- [x] Keep autosave writes idempotent and safe for one client retry after failure
- [x] Prevent stale or cross-patient draft writes from overwriting current intake state
- [x] Add structured logging and correlation data for autosave and restore operations