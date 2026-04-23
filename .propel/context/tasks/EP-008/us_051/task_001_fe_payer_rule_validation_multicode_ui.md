# Task - TASK_001_FE_PAYER_RULE_VALIDATION_MULTICODE_UI

## Requirement Reference
- User Story: US_051
- Story Location: .propel/context/tasks/EP-008/us_051/us_051.md
- Acceptance Criteria:
    - AC-1: Given ICD-10 and CPT codes are assigned to a patient, When payer validation runs, Then the system checks code combinations against payer-specific rules and flags potential claim denial risks.
    - AC-2: Given a claim denial risk is flagged, When the staff member views the alert, Then the system displays the specific rule violation and suggests corrective actions.
    - AC-3: Given clinical documentation supports multiple billable diagnoses, When the coding workflow runs, Then the system supports multi-code assignment with each code individually verified.
    - AC-4: Given multi-code assignment is complete, When all codes are verified, Then the system validates the complete code set against bundling rules and modifier requirements.
- Edge Cases:
    - When payer rules conflict with clinical documentation: System flags the conflict, shows both the clinical rationale and payer rule, and lets the staff member decide.
    - When payer rules are unknown or new: System applies general CMS rules as default and flags the encounter for manual payer rule verification.

## Design References (Frontend Tasks Only)
| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-014-medical-coding.html |
| **Screen Spec** | figma_spec.md#SCR-014 |
| **UXR Requirements** | UXR-003, UXR-105, UXR-605 |
| **Design Tokens** | designsystem.md#colors (confidence colors, semantic colors), designsystem.md#typography, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement (UI Tasks Only)**
**Wireframe Status = AVAILABLE:**
- **MUST** open and reference the wireframe file during UI implementation
- **MUST** match layout, spacing, typography, and colors from the wireframe
- **MUST** implement all states shown in wireframe (default, hover, focus, error, loading)
- **MUST** validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack
| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | React | 18.x |
| UI Component Library | Material-UI (MUI) | 5.x |
| State Management | React Query + Zustand | 4.x / 4.x |
| Backend | N/A (consumed via REST) | - |
| Database | N/A | - |
| AI/ML | N/A | - |

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
Implement the frontend UI components for payer rule validation alerts, claim denial risk display, and multi-code assignment workflow on the Medical Coding Review screen (SCR-014). This task adds payer rule violation alerts with corrective action suggestions, multi-code assignment support for multiple billable diagnoses per encounter, and bundling/modifier rule validation display to the existing coding review interface.

## Dependent Tasks
- task_003_db_payer_rules_schema.md — Requires payer rules table and seed data to exist
- task_002_be_payer_rule_validation_multicode_api.md — Requires API endpoints for payer validation and multi-code assignment
- US_049 tasks — Requires verified codes UI (code review dashboard baseline)
- US_008 tasks — Requires MedicalCode entity and base UI

## Impacted Components
- **NEW**: `PayerRuleAlert` component — Displays payer rule violations with severity, rule details, and corrective actions
- **NEW**: `MultiCodeAssignmentPanel` component — Enables multi-code selection and individual code verification workflow
- **NEW**: `BundlingRuleWarning` component — Shows bundling/modifier rule validation results
- **NEW**: `PayerConflictDialog` component — Modal for resolving payer rule vs. clinical documentation conflicts
- **MODIFY**: Medical Coding Review page — Integrate payer validation alerts and multi-code assignment into existing SCR-014 layout
- **MODIFY**: Code table rows — Add claim denial risk badge and payer rule status column

## Implementation Plan
1. **Create `PayerRuleAlert` component**: Build an MUI `Alert` component variant that displays payer-specific rule violations. Include severity level (error/warning/info), rule ID, rule description, affected code combination, and a list of suggested corrective actions. Use `confidence` color tokens from designsystem.md for severity indicators.
2. **Create `MultiCodeAssignmentPanel` component**: Build a panel that lists multiple billable diagnosis codes for a single encounter. Each code row shows verification status (verified/pending/rejected) with individual approve/override actions. Support drag-and-drop reordering for billing priority ranking.
3. **Create `BundlingRuleWarning` component**: Display bundling rule violations and modifier requirements. Show which code pairs violate bundling rules and suggest applicable modifiers (e.g., modifier 59 for distinct procedures).
4. **Create `PayerConflictDialog` component**: Build a MUI `Dialog` that shows side-by-side comparison of clinical rationale vs. payer rule when conflicts occur. Include "Use Clinical Code", "Use Payer-Preferred Code", and "Flag for Manual Review" action buttons.
5. **Integrate payer validation into code table**: Add a "Payer Status" column to ICD-10 and CPT code tables. Show color-coded badges: green (valid), amber (warning — review recommended), red (denial risk). Add tooltip with rule details on hover.
6. **Wire API calls with React Query**: Create `usePayerValidation` and `useMultiCodeAssignment` hooks using React Query. Call `GET /api/coding/payer-rules/{patientId}` for validation results and `POST /api/coding/multi-assign` for multi-code operations. Implement optimistic updates for approve/override actions.
7. **Implement all 5 screen states**: Default (codes with payer validation results), Loading (skeleton loaders during validation), Empty (no codes assigned yet), Error (API failure with fallback banner per UXR-605), Validation (inline field errors for override justification).

## Current Project State
- [Placeholder — to be updated during task execution based on dependent task completion]

## Expected Changes
| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/components/coding/PayerRuleAlert.tsx | Payer rule violation alert component with severity and corrective actions |
| CREATE | app/src/components/coding/MultiCodeAssignmentPanel.tsx | Multi-code assignment panel with individual verification per code |
| CREATE | app/src/components/coding/BundlingRuleWarning.tsx | Bundling rule and modifier requirement warning display |
| CREATE | app/src/components/coding/PayerConflictDialog.tsx | Dialog for resolving payer vs. clinical documentation conflicts |
| CREATE | app/src/hooks/usePayerValidation.ts | React Query hook for payer rule validation API |
| CREATE | app/src/hooks/useMultiCodeAssignment.ts | React Query hook for multi-code assignment operations |
| MODIFY | app/src/pages/MedicalCodingReview.tsx | Integrate payer alerts, multi-code panel, and bundling warnings into SCR-014 |
| MODIFY | app/src/components/coding/CodeTable.tsx | Add payer status column with color-coded badges and tooltips |

## External References
- [MUI 5 Alert API](https://mui.com/material-ui/api/alert/) — Alert component for payer rule violations
- [MUI 5 Dialog API](https://mui.com/material-ui/api/dialog/) — Dialog for payer conflict resolution
- [MUI 5 Table API](https://mui.com/material-ui/api/table/) — Table enhancements for payer status column
- [React Query v4 Queries](https://tanstack.com/query/v4/docs/react/guides/queries) — Data fetching for payer validation
- [ICD-10-CM Official Guidelines](https://www.cms.gov/medicare/coding-billing/icd-10-codes) — Reference for bundling/modifier rules
- [NCCI Procedure-to-Procedure Edits](https://www.cms.gov/medicare/coding-billing/national-correct-coding-initiative-edits) — CMS bundling rules reference

## Build Commands
- `cd app && npm run build` — Build frontend
- `cd app && npm run lint` — Lint check
- `cd app && npm test` — Run unit tests

## Implementation Validation Strategy
- [ ] Unit tests pass
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Payer rule violation alerts render correctly with all severity levels (error, warning, info)
- [ ] Multi-code assignment panel allows individual code verification (approve/override per code)
- [ ] Bundling rule warnings display when invalid code combinations detected
- [ ] Payer conflict dialog shows side-by-side clinical vs. payer rule comparison
- [ ] All 5 screen states render correctly (Default, Loading, Empty, Error, Validation)
- [ ] AI unavailability banner displays with manual fallback option (UXR-605)
- [ ] Confidence scores use correct color tokens: green (>=80%), amber (60-79%), red (<60%) per UXR-105
- [ ] Breadcrumb navigation works (Staff Dashboard > Patient > Medical Coding) per UXR-003
- [ ] Keyboard navigation accessible for all interactive elements (WCAG 2.1 AA)
- [ ] Color contrast validated for payer status badges against neutral backgrounds

## Implementation Checklist
- [x] Create `PayerRuleAlert` component with severity-based styling using semantic color tokens
- [x] Create `MultiCodeAssignmentPanel` with individual code verification rows and billing priority ordering
- [x] Create `BundlingRuleWarning` component showing bundling violations and modifier suggestions
- [x] Create `PayerConflictDialog` with side-by-side clinical rationale vs. payer rule comparison
- [x] Add "Payer Status" column to ICD-10 and CPT code tables with color-coded badges
- [x] Implement `usePayerValidation` React Query hook with error/loading states
- [x] Implement `useMultiCodeAssignment` React Query hook with optimistic updates
- [x] Integrate all components into MedicalCodingReview page matching SCR-014 wireframe layout
