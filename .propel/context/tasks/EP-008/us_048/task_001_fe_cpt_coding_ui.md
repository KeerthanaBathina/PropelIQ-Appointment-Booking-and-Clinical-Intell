# Task - task_001_fe_cpt_coding_ui

## Requirement Reference

- User Story: US_048
- Story Location: .propel/context/tasks/EP-008/us_048/us_048.md
- Acceptance Criteria:
  - AC-2: Given a CPT mapping is generated, When the results are displayed, Then each code shows the CPT code, description, confidence score, and justification text.
  - AC-3: Given multiple CPT codes apply to a single procedure, When the AI identifies this, Then the system presents all applicable codes ranked by relevance with multi-code assignment support.
- Edge Case:
  - Ambiguous procedure description: System assigns the closest match with reduced confidence and flags for staff verification.
  - Bundled procedures: System identifies bundling opportunities and presents the bundled code option alongside individual codes.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-014-medical-coding.html` |
| **Screen Spec** | figma_spec.md#SCR-014 |
| **UXR Requirements** | UXR-003, UXR-105, UXR-201, UXR-202, UXR-203, UXR-204, UXR-205, UXR-301, UXR-302, UXR-304, UXR-402, UXR-502, UXR-601, UXR-605 |
| **Design Tokens** | designsystem.md#colors (AI Confidence Colors), designsystem.md#typography, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement (UI Tasks Only)**

**IF Wireframe Status = AVAILABLE or EXTERNAL:**

- **MUST** open and reference the wireframe file/URL during UI implementation
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
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Database | PostgreSQL | 16.x |

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

Implement the CPT Procedure Code section of the Medical Coding Review screen (SCR-014). This task builds the frontend components for displaying AI-suggested CPT codes with confidence scores, justification text, and staff approve/override workflows. The UI must support multi-code display ranked by relevance for bundled procedures and show color-coded confidence badges per UXR-105. It must also handle AI service unavailability with a manual coding fallback banner per UXR-605.

## Dependent Tasks

- US_008 tasks (EP-DATA) — MedicalCode entity must exist
- task_002_be_cpt_coding_api — API endpoints must be defined (contract-first; FE can stub with mock data)
- task_003_db_cpt_code_library — CPT code library must be seeded (for code validation display)

## Impacted Components

- **NEW** `CptCodeTable` — React component rendering CPT procedure code table with sortable columns
- **NEW** `CptCodeRow` — Row component with Approve/Override actions per code
- **NEW** `OverrideJustificationModal` — MUI Dialog for code override with replacement code and justification fields
- **NEW** `CptCodingSummary` — Summary cards showing total codes, approved, pending, overridden counts
- **NEW** `AiUnavailableBanner` — Alert banner for AI service unavailability with manual fallback CTA
- **NEW** `useCptCodes` — React Query hook for fetching pending CPT codes
- **NEW** `useCptApprove` — React Query mutation hook for approving CPT codes
- **NEW** `useCptOverride` — React Query mutation hook for overriding CPT codes
- **MODIFY** Medical Coding page — Integrate CPT section below ICD-10 section

## Implementation Plan

1. **Create `CptCodeTable` component** rendering a MUI Table with columns: Code, Description, AI Suggested, Confidence, Status, Actions. Use MUI `TableSortLabel` for sortable columns per wireframe. Each row displays a CPT code from the API response.
2. **Implement confidence badge with color-coding** using design tokens from `designsystem.md#AI Confidence Colors`: high (≥80%) green `#2E7D32`, medium (60-79%) amber `#ED6C02`, low (<60%) red `#D32F2F`. Use MUI `Chip` or custom `ConfidenceBadge` component with ARIA labels for accessibility (e.g., `aria-label="High confidence: 96%"`).
3. **Add Approve/Override action buttons** per row: Primary "Approve" button and Secondary "Override" button. Buttons are conditionally rendered — disabled when status is "Approved" or "Overridden", active only for "Pending" status. Match wireframe button sizing (`btn-sm`).
4. **Build `OverrideJustificationModal`** as MUI Dialog with: replacement code `TextField`, required justification `TextField` (multiline, 4 rows), HIPAA audit notice caption, Cancel/Submit Override buttons. Validate that justification is non-empty before submission. Modal opens on Override button click.
5. **Implement multi-code display** for bundled procedures: When multiple CPT codes apply to a single procedure (AC-3), render them as grouped rows with a visual indicator (MUI `Chip` with "Bundled" label). Show individual codes alongside the bundled option, ranked by relevance score.
6. **Add `AiUnavailableBanner`** as MUI `Alert` (severity="info") shown when AI service status is unavailable. Banner text: "AI Unavailable: Code suggestions from the AI engine are temporarily unavailable. Manual coding is available." Include dismiss action. Visibility controlled by API health check response or error state.
7. **Wire React Query hooks** for data fetching and mutations: `useCptCodes(patientId)` calls `GET /api/coding/cpt/pending/{patientId}`, `useCptApprove` calls `PUT /api/coding/cpt/approve`, `useCptOverride` calls `PUT /api/coding/cpt/override`. Handle loading, error, and empty states per SCR-014 screen states (Default, Loading, Empty, Error, Validation).

## Current Project State

- No frontend codebase exists yet (green-field). Project structure to be established by foundational tasks (EP-TECH).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/features/medical-coding/components/CptCodeTable.tsx | CPT code table component with sortable columns |
| CREATE | app/src/features/medical-coding/components/CptCodeRow.tsx | Individual CPT code row with approve/override actions |
| CREATE | app/src/features/medical-coding/components/OverrideJustificationModal.tsx | Modal dialog for code override justification |
| CREATE | app/src/features/medical-coding/components/CptCodingSummary.tsx | Summary cards (total, approved, pending, overridden) |
| CREATE | app/src/features/medical-coding/components/AiUnavailableBanner.tsx | AI service unavailability alert banner |
| CREATE | app/src/features/medical-coding/hooks/useCptCodes.ts | React Query hook for fetching CPT codes |
| CREATE | app/src/features/medical-coding/hooks/useCptApprove.ts | React Query mutation for approving CPT codes |
| CREATE | app/src/features/medical-coding/hooks/useCptOverride.ts | React Query mutation for overriding CPT codes |
| CREATE | app/src/features/medical-coding/types/cpt.types.ts | TypeScript interfaces for CPT code data |
| MODIFY | app/src/features/medical-coding/pages/MedicalCodingPage.tsx | Integrate CptCodeTable section below ICD-10 section |

## External References

- [MUI Table API (v5)](https://mui.com/material-ui/api/table/)
- [MUI Dialog API (v5)](https://mui.com/material-ui/api/dialog/)
- [MUI Alert API (v5)](https://mui.com/material-ui/api/alert/)
- [React Query Mutations (v4)](https://tanstack.com/query/v4/docs/react/guides/mutations)
- [WCAG 2.1 AA Contrast Requirements](https://www.w3.org/WAI/WCAG21/Understanding/contrast-minimum.html)

## Build Commands

- Refer to applicable technology stack specific build commands in `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Confidence badge colors match designsystem.md AI Confidence Colors
- [ ] Override modal enforces required justification field
- [ ] AI unavailable banner displays and dismisses correctly
- [ ] Keyboard navigation works for all interactive elements (UXR-202)
- [ ] ARIA labels present on all interactive elements (UXR-203)
- [ ] Color contrast meets WCAG AA 4.5:1 for text (UXR-204)
- [ ] Responsive layout adapts at 375px, 768px, 1440px (UXR-301)

## Implementation Checklist

- [x] Create `CptCodeTable` component with MUI Table, sortable columns (Code, Description, AI Suggested, Confidence, Status, Actions)
- [x] Implement `ConfidenceBadge` with color-coded indicators: green (≥80%), amber (60-79%), red (<60%) per UXR-105 (reused from `@/components/coding/ConfidenceBadge`)
- [x] Add Approve/Override action buttons per CPT code row with conditional rendering based on status (Pending/Approved/Overridden)
- [x] Build `OverrideJustificationModal` MUI Dialog with replacement code field, required justification textarea, and HIPAA audit notice
- [x] Implement multi-code display with relevance ranking and bundled procedure visual indicators (AC-3)
- [x] Add `AiUnavailableBanner` MUI Alert with manual coding fallback message per UXR-605
- [x] Wire React Query hooks (`useCptCodes`, `useCptApprove`, `useCptOverride`) and handle all 5 screen states (Default, Loading, Empty, Error, Validation)
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
