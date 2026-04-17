# Task - task_002_be_manual_intake_api

## Requirement Reference

- User Story: US_028
- Story Location: .propel/context/tasks/EP-004/us_028/us_028.md
- Acceptance Criteria:
    - AC-1: Given I select "Manual Form" from intake options, When the form loads, Then it displays sections for Personal Information, Medical History, and Insurance with all mandatory and optional fields.
    - AC-2: Given AI intake has pre-filled some data, When the manual form loads, Then the pre-filled fields display the AI-collected data with visual indication of pre-filled status.
    - AC-3: Given I am filling out the form, When I leave a mandatory field empty and attempt to submit, Then inline validation highlights the missing field within 200ms with a descriptive error.
    - AC-4: Given I complete and submit the form, When submission succeeds, Then intake status is marked "completed" and staff is notified for review.
- Edge Case:
    - EC-1: If the patient navigates away mid-completion, autosave must preserve the current draft and reload it on return.
    - EC-2: Browser-back or retry actions during submission must not create duplicate completion events or lose data.

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

Implement the deterministic backend APIs for manual intake draft loading, autosave, submission, and post-submit updates. This task delivers the backend portion of FR-027, FR-029, FR-030, and FR-031 by exposing patient-authenticated endpoints that load the current manual intake draft, merge AI-prefilled values from the shared `IntakeData` record, autosave progress every 30 seconds, validate mandatory fields on submit, mark intake as completed, and trigger staff-review notification workflows. The API must remain idempotent around final submission, preserve carried-over AI data, and emit structured diagnostics for draft recovery and completion outcomes.

## Dependent Tasks

- US_027 task_003_be_ai_intake_session_api (Shared intake session and switch-to-manual data handoff contract must exist)
- US_027 task_004_db_ai_intake_session_persistence (Shared `IntakeData` draft persistence and autosave fields must exist)
- US_008 - Foundational - `IntakeData` entity must exist
- US_034 - Cross-Epic - Notification integration for staff-review follow-up should be reused if available

## Impacted Components

- **NEW** `ManualIntakeController` - Patient-authenticated endpoints for load, autosave, submit, and update operations (Server/Controllers/)
- **NEW** `IManualIntakeService` / `ManualIntakeService` - Business logic for draft merge, validation, completion, and idempotent updates (Server/Services/)
- **NEW** `ManualIntakeDraftResponse`, `SaveManualIntakeDraftRequest`, and `SubmitManualIntakeRequest` - DTOs for manual intake lifecycle actions (Server/Models/DTOs/)
- **MODIFY** shared intake integration path - Reuse carried-over AI-collected fields when loading manual form drafts (Server/Services/)
- **MODIFY** `Program.cs` - Register manual intake services and dependencies

## Implementation Plan

1. **Add a draft-load endpoint** that returns the current intake draft with mandatory and optional field groups plus metadata identifying which values were carried over from AI intake.
2. **Add autosave and save-draft endpoints** that persist partial updates into the shared `IntakeData` record on a recurring 30-second cadence without creating duplicate drafts.
3. **Implement submit validation** that enforces the required manual intake fields and returns field-specific validation messages in a structure the UI can render inline.
4. **Support final submission and later edits** by marking the intake as completed on first submit while still allowing controlled patient updates after initial completion per FR-031.
5. **Preserve AI-to-manual carryover** by merging AI-collected values with manual changes deterministically, preferring explicit patient edits over earlier AI drafts.
6. **Prevent duplicate submission** with idempotent completion handling and safe retry behavior when the browser back button or network retries replay requests.
7. **Trigger downstream staff-review notifications and logs** when manual intake completes or is materially updated after completion.

## Current Project State

```text
Server/
  Controllers/
    AIIntakeController.cs
  Services/
    AIIntakeSessionService.cs
  Models/
    Entities/
      IntakeData.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Controllers/ManualIntakeController.cs | Patient-authenticated manual intake draft, autosave, submit, and update endpoints |
| CREATE | Server/Services/IManualIntakeService.cs | Interface for manual intake draft and completion operations |
| CREATE | Server/Services/ManualIntakeService.cs | Draft merge, validation, completion, and post-submit update orchestration |
| CREATE | Server/Models/DTOs/ManualIntakeDraftResponse.cs | Draft payload including prefilled AI field metadata |
| CREATE | Server/Models/DTOs/SaveManualIntakeDraftRequest.cs | Partial-update request contract for autosave and draft save |
| CREATE | Server/Models/DTOs/SubmitManualIntakeRequest.cs | Final submission and update request contract for manual intake |
| MODIFY | Server/Program.cs | Register manual intake services and dependencies |

## External References

- ASP.NET Core 8 Web API documentation: https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-8.0
- ASP.NET Core model validation: https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-8.0
- ASP.NET Core idempotency guidance: https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotent-message-processing
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)

## Implementation Checklist

- [ ] Return the current manual intake draft with mandatory, optional, and AI-prefilled field metadata
- [ ] Persist autosave updates into the shared `IntakeData` record without creating duplicate drafts
- [ ] Validate required fields on submit and return field-specific errors that the UI can render inline
- [ ] Mark intake as completed on first successful submit and support controlled post-submit patient edits per FR-031
- [ ] Preserve explicit manual edits over earlier AI-carried values when both are present
- [ ] Make final submission retry-safe and resistant to duplicate completion on browser back or network replay
- [ ] Trigger staff-review notifications and structured logs when manual intake is completed or materially updated