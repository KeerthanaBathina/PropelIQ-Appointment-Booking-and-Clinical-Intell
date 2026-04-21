# Task - task_002_ai_conversational_intake_orchestration

## Requirement Reference

- User Story: US_027
- Story Location: .propel/context/tasks/EP-004/us_027/us_027.md
- Acceptance Criteria:
    - AC-1: Given I select "AI-Assisted Intake" from my dashboard, When the chat interface loads, Then the AI greets me and explains the intake process with a progress indicator.
    - AC-2: Given the AI asks a question, When I provide a response, Then the AI validates my answer, asks clarifying follow-ups if needed, and moves to the next question within 1 second.
    - AC-3: Given mandatory fields are required, When the AI collects my information, Then it asks for full name, DOB, contact info, and emergency contact before marking mandatory collection complete.
    - AC-4: Given the intake session is active, When the AI displays a summary, Then I can review all collected information and correct any errors before submission.
- Edge Case:
    - EC-1: Ambiguous medical terminology must trigger grounded clarification prompts using RAG context and patient-friendly examples.
    - EC-2: When the session resumes after timeout, the AI must continue from the last completed question rather than restarting the flow.

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
| AI Model Provider | OpenAI GPT-4o-mini + Anthropic Claude 3.5 Sonnet | 2024-07-18 / claude-3-5-sonnet-20241022 |
| Vector Store | PostgreSQL pgvector | 0.5.x |
| AI Gateway | Custom .NET Service with Polly | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Language | C# | 12 / .NET 8 |
| Mobile | N/A | N/A |

**Note**: All code, and libraries, MUST be compatible with versions above.

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | Yes |
| **AIR Requirements** | AIR-001, AIR-O02, AIR-S06, AIR-R02 |
| **AI Pattern** | RAG + Tool Calling |
| **Prompt Template Path** | Server/AI/ConversationalIntake/Prompts/ |
| **Guardrails Config** | Server/AI/ConversationalIntake/guardrails.json |
| **Model Provider** | OpenAI GPT-4o-mini primary, Claude 3.5 Sonnet fallback via AI Gateway |

### **CRITICAL: AI Implementation Requirement (AI Tasks Only)**

**IF AI Impact = Yes:**
- **MUST** reference prompt templates from Prompt Template Path during implementation
- **MUST** implement guardrails for input sanitization and output validation
- **MUST** enforce token budget limits per AIR-O02 requirements
- **MUST** implement fallback logic for low-confidence responses
- **MUST** log all prompts/responses for audit (redact PII)
- **MUST** handle model failures gracefully (timeout, rate limit, 5xx errors)

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement the AI orchestration layer for conversational intake. This task delivers AIR-001 and the AI portion of FR-026 by building the grounded conversational workflow that interprets patient responses, retrieves medical terminology context through pgvector-backed RAG, validates mandatory-field coverage, asks clarifying follow-up questions, and produces a structured summary for review. The orchestration must satisfy the sub-second exchange target, enforce token and safety constraints, and degrade safely to retry or manual-form handoff if the AI path cannot continue confidently.

## Dependent Tasks

- EP-014 - Cross-Epic - RAG knowledge base and pgvector retrieval infrastructure must exist
- task_003_be_ai_intake_session_api (Session lifecycle and persistence endpoints must exist for orchestration calls)
- US_028 - Same-Epic - Manual intake handoff contract should exist for seamless switch without data loss

## Impacted Components

- **NEW** `ConversationalIntakeService` - Core orchestration for prompt assembly, tool invocation, field extraction, and next-question generation (Server/AI/ConversationalIntake/)
- **NEW** `IntakePromptBuilder` - Assembles system and task prompts with session state, required-field status, and retrieved context (Server/AI/ConversationalIntake/)
- **NEW** `IntakeFieldExtractionValidator` - Validates extracted structured fields and determines whether clarification is required (Server/AI/ConversationalIntake/)
- **NEW** `IntakeRagRetriever` - Retrieves medical terminology and intake-flow context from pgvector indexes (Server/AI/ConversationalIntake/)
- **NEW** prompt templates and guardrails config - Versioned conversational-intake prompt artifacts and safety rules (Server/AI/ConversationalIntake/Prompts/)

## Implementation Plan

1. **Create prompt templates for conversational intake** covering greeting, mandatory-field collection, optional-field follow-ups, clarification prompts, summary generation, and resume behavior.
2. **Implement RAG-backed context retrieval** that fetches top medical terminology and intake-flow chunks for ambiguous or domain-specific patient inputs before model invocation.
3. **Build orchestration that extracts structured intake data** from patient utterances, validates required fields, and decides whether to ask a follow-up, move forward, or switch to summary.
4. **Enforce mandatory-field completion** so name, DOB, contact information, and emergency contact are all captured before the session can be marked ready for submission.
5. **Add guardrails and token budgeting** for prompt injection sanitization, output schema validation, exchange limits, and model fallback via the AI Gateway.
6. **Support resume and summary continuity** by reconstructing the next prompt from saved session state after timeout or return visit.
7. **Provide safe fallback paths** for low-confidence or repeated AI failures by surfacing a deterministic handoff recommendation to the manual form flow without losing collected data.

## Current Project State

```text
Server/
  AI/
    (no conversational intake orchestration yet)
  Services/
    (no intake orchestration service yet)
  Models/
    Entities/
      IntakeData.cs
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | Server/AI/ConversationalIntake/ConversationalIntakeService.cs | Orchestrates grounded intake exchanges and next-question generation |
| CREATE | Server/AI/ConversationalIntake/IntakePromptBuilder.cs | Builds prompts from session state and retrieved context |
| CREATE | Server/AI/ConversationalIntake/IntakeFieldExtractionValidator.cs | Validates extracted structured fields and follow-up requirements |
| CREATE | Server/AI/ConversationalIntake/IntakeRagRetriever.cs | Retrieves top relevant context chunks from pgvector-backed indexes |
| CREATE | Server/AI/ConversationalIntake/Prompts/intake-system.liquid | System prompt template for conversational intake |
| CREATE | Server/AI/ConversationalIntake/Prompts/intake-summary.liquid | Summary/review prompt template |
| CREATE | Server/AI/ConversationalIntake/guardrails.json | Input sanitization, output schema, and fallback thresholds |

## External References

- OpenAI GPT-4o-mini prompt engineering guide: https://platform.openai.com/docs/guides/text-generation
- PostgreSQL pgvector documentation: https://github.com/pgvector/pgvector
- Polly resilience pipelines (.NET 8): https://learn.microsoft.com/en-us/dotnet/core/resilience/
- Serilog structured logging: https://serilog.net/

## Build Commands

- Refer to applicable technology stack specific build commands

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[AI Tasks]** Prompt templates validated with test inputs
- [ ] **[AI Tasks]** Guardrails tested for input sanitization and output validation
- [ ] **[AI Tasks]** Fallback logic tested with low-confidence/error scenarios
- [ ] **[AI Tasks]** Token budget enforcement verified
- [ ] **[AI Tasks]** Audit logging verified (no PII in logs)

## Implementation Checklist

- [x] Build prompt templates for greeting, intake questioning, clarification, summary, and resume flows
- [x] Retrieve grounded medical terminology and intake-flow context before ambiguous follow-up generation
- [x] Extract and validate mandatory intake fields before advancing the conversation state
- [x] Enforce token and guardrail limits for each exchange and sanitize prompt-injection attempts
- [x] Support low-confidence and provider-failure fallback behavior without losing collected intake data
- [x] Reconstruct the next conversational step from saved session state after resume
- [x] Verify AIR-001 behavior with representative mandatory-field, optional-field, ambiguous-term, and resume scenarios
- **[AI Tasks - MANDATORY]** Reference prompt templates from AI References table during implementation ✅
- **[AI Tasks - MANDATORY]** Implement and test guardrails before marking task complete ✅
- **[AI Tasks - MANDATORY]** Verify AIR-XXX requirements are met (quality, safety, operational) ✅