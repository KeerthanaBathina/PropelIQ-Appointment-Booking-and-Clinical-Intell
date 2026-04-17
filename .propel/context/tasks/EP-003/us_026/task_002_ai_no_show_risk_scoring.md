# Task - task_002_ai_no_show_risk_scoring

## Requirement Reference

- User Story: US_026
- Story Location: .propel/context/tasks/EP-003/us_026/us_026.md
- Acceptance Criteria:
    - AC-1: Given an appointment exists, When the risk score is calculated, Then it produces a score from 0-100 based on patient history (past no-shows, cancellations) and appointment characteristics (time of day, day of week).
    - AC-3: Given insufficient historical data (new patient, <3 appointments), When the risk score is calculated, Then the system uses rule-based defaults and displays "Estimated" label.
    - AC-4: Given risk scores are computed, When the system evaluates slot swap priority, Then lower no-show risk patients are prioritized for preferred slots.
- Edge Case:
    - EC-1: After a completed visit changes the patient's historical profile, the next score calculation must reflect the updated history without manual retraining during request flow.
    - EC-2: A patient with 100% no-show history must cap at 100 and trigger a high-risk outreach flag.

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
| Database | PostgreSQL | 16.x |
| AI Model Provider | In-process classification model | N/A |
| AI Gateway | Custom .NET Service with Polly | 8.x |
| Caching | Upstash Redis | 7.x |
| Language | C# | 12 / .NET 8 |
| Mobile | N/A | N/A |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-006, AIR-O04 |
| **AI Pattern** | Classification + Rule-based fallback |
| **Prompt Template Path** | Server/AI/NoShowRisk/no-show-risk-config.json |
| **Guardrails Config** | Server/AI/NoShowRisk/no-show-risk-config.json |
| **Model Provider** | In-process classification model with deterministic fallback |

### **CRITICAL: AI Implementation Requirement (AI Tasks Only)**

**IF AI Impact = Yes:**
- **MUST** reference the scoring configuration artifact from Prompt Template Path during implementation
- **MUST** implement guardrails for input sanitization and output validation
- **MUST** enforce model-path execution limits and fallback thresholds appropriate to the in-process classification flow
- **MUST** implement fallback logic for low-confidence responses
- **MUST** log scoring inputs and outputs at a summary level for audit (redact PII)
- **MUST** handle model failures gracefully (timeout, unavailable model artifact, invalid feature payload)

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the no-show risk scoring engine for US_026. This AI task delivers AIR-006 and the hybrid scoring portion of FR-014 by creating an in-process classification workflow that evaluates appointment-level no-show risk from patient history and appointment characteristics, returns a score from 0-100, and falls back to deterministic rules when historical data is insufficient or the model cannot be used safely. The scoring logic must remain real-time enough for appointment creation and slot-swap prioritization, cap scores at 100, expose whether a score is estimated, and avoid leaking PHI through logs or model metadata.

## Dependent Tasks

- US_008 - Foundational - Historical appointment data must exist for feature extraction
- task_003_db_no_show_risk_persistence (Persistence contract for risk score metadata must exist before scores are stored)
- US_021 task_001_be_dynamic_slot_swap_engine (Consumes the resulting score for preferred-slot prioritization)

## Impacted Components

- **NEW** `INoShowRiskScoringService` / `NoShowRiskScoringService` - Core scoring interface and orchestration for classification plus fallback rules (Server/Services/ or Server/AI/)
- **NEW** `NoShowRiskFeatureExtractor` - Builds model input features from appointment history and appointment characteristics (Server/AI/NoShowRisk/)
- **NEW** `NoShowRiskFallbackPolicy` - Deterministic rules for new patients or low-history scenarios, including estimated-score labeling (Server/AI/NoShowRisk/)
- **NEW** `NoShowRiskScoreResult` - Output model containing score, estimated flag, risk band, and outreach flag (Server/Models/DTOs/ or Server/AI/)
- **NEW** `no-show-risk-config.json` - Thresholds, caps, and fallback parameters for model and rule behavior (Server/AI/NoShowRisk/)

## Implementation Plan

1. **Define the score contract and features** using patient history inputs such as prior no-shows, cancellations, and appointment count plus appointment features such as weekday and time-of-day bucket.
2. **Implement the classification scoring service** so it returns a normalized 0-100 score and keeps feature extraction isolated from calling controllers and orchestration services.
3. **Add a deterministic fallback policy** for patients with fewer than three relevant historical appointments or when the model is unavailable, and mark those results as estimated.
4. **Cap and band the output safely** by constraining the score to 0-100, mapping it to low/medium/high risk bands, and flagging manual outreach for the highest-risk cases.
5. **Support slot-swap prioritization** by returning a stable score result that downstream orchestration can use to prefer lower-risk patients when multiple candidates match.
6. **Add guardrails and resilience** through input validation, confidence or readiness checks for the model path, and safe fallback behavior when scoring fails.
7. **Log scoring outcomes safely** by recording model path versus fallback path, score band, and reason codes without emitting raw patient history values into logs.

## Current Project State

```text
Server/
  Services/
    AppointmentBookingService.cs
    AppointmentReschedulingService.cs
    PreferredSlotSwapService.cs
  Models/
    Entities/
      Appointment.cs
      Patient.cs
  AI/
    (no no-show risk scoring components yet)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/Services/INoShowRiskScoringService.cs | Interface for no-show risk score generation |
| CREATE | Server/Services/NoShowRiskScoringService.cs | Classification-based score generation with deterministic fallback coordination |
| CREATE | Server/AI/NoShowRisk/NoShowRiskFeatureExtractor.cs | Feature assembly from patient history and appointment attributes |
| CREATE | Server/AI/NoShowRisk/NoShowRiskFallbackPolicy.cs | Rule-based fallback logic for insufficient history and safe degradation |
| CREATE | Server/Models/DTOs/NoShowRiskScoreResult.cs | Output contract with score, band, estimated flag, and outreach flag |
| CREATE | Server/AI/NoShowRisk/no-show-risk-config.json | Thresholds and fallback parameters for scoring behavior |

## External References

- Classification model overview: https://learn.microsoft.com/en-us/dotnet/machine-learning/resources/tasks#binary-classification
- Polly resilience pipelines (.NET 8): https://learn.microsoft.com/en-us/dotnet/core/resilience/
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Scoring configuration validated with representative test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Model-path execution limits and fallback thresholds verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)

## Implementation Checklist

- [ ] Define the feature set and output contract for no-show risk scoring from appointment history and appointment characteristics
- [ ] Implement the scoring service to return a normalized 0-100 score and stable risk band
- [ ] Add deterministic fallback rules for patients with fewer than three historical appointments and mark those results as estimated
- [ ] Cap extreme scores at 100 and surface a high-risk outreach flag for manual follow-up workflows
- [ ] Return a score result that downstream slot-swap prioritization can consume deterministically
- [ ] Add validation, safe-degradation, and logging guardrails so scoring failures do not block booking or queue workflows
- [ ] Verify AIR-006 output quality and fallback behavior with representative high-risk, low-risk, and insufficient-history cases
- **[AI Tasks - MANDATORY]** Reference the scoring configuration artifact from AI References table during implementation
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete
- **[AI Tasks - MANDATORY]** Verify AIR-XXX requirements are met (quality, safety, operational)