# Task - TASK_004

## Requirement Reference

- User Story: US_047
- Story Location: .propel/context/tasks/EP-008/us_047/us_047.md
- Acceptance Criteria:
  - AC-1: **Given** clinical diagnoses have been extracted from patient documents, **When** AI coding runs, **Then** the system maps each diagnosis to the most appropriate ICD-10 code with a justification explaining the mapping rationale.
  - AC-2: **Given** an ICD-10 mapping is generated, **When** the results are displayed, **Then** each code shows the ICD-10 code, description, confidence score, and justification text.
  - AC-4: **Given** multiple ICD-10 codes apply to a single diagnosis, **When** the AI identifies this, **Then** the system presents all applicable codes ranked by relevance.
- Edge Case:
  - What happens when the AI cannot find a matching ICD-10 code? System assigns "uncodable" status with a confidence of 0.00 and flags for manual coding.
  - How does the system handle deprecated ICD-10 codes after a library update? System flags existing records using deprecated codes and suggests replacement codes for staff review.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-014-medical-coding.html |
| **Screen Spec** | figma_spec.md#SCR-014 |
| **UXR Requirements** | UXR-105, UXR-605 |
| **Design Tokens** | designsystem.md#typography, designsystem.md#colors (AI Confidence Colors), designsystem.md#ConfidenceBadge |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement**

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

Implement the ICD-10 coding review UI components within the Medical Coding Review screen (SCR-014). This includes the ICD-10 code table with sortable columns displaying code value, description, confidence score (with color-coded ConfidenceBadge per UXR-105), and justification text. The UI supports multiple codes per diagnosis ranked by relevance (AC-4), visual indicators for "uncodable" status, deprecated code warnings with replacement suggestions, and an AI service unavailability fallback banner (UXR-605). All 5 screen states (Default, Loading, Empty, Error, Validation) must be implemented per figma_spec.md SCR-014.

## Dependent Tasks

- task_002_be_icd10_mapping_api.md — API endpoints must exist for data fetching.

## Impacted Components

- **NEW** `Icd10CodeTable` — React component for ICD-10 code table with sorting and ranking
- **NEW** `Icd10CodeRow` — Row component displaying individual code with ConfidenceBadge and justification
- **NEW** `UncodableAlert` — Alert banner for diagnoses with no matching ICD-10 code
- **NEW** `DeprecatedCodeWarning` — Warning indicator for codes flagged as deprecated after library update
- **NEW** `AiUnavailableBanner` — Banner component per UXR-605 for AI service fallback
- **NEW** `useIcd10Codes` — React Query hook for fetching pending ICD-10 codes
- **MODIFY** Medical Coding Review page — Integrate ICD-10 code table into SCR-014 layout

## Implementation Plan

1. **Create `useIcd10Codes` React Query hook**:
   - Fetch from `GET /api/coding/icd10/pending?patientId={id}`.
   - Cache with 5-min TTL via React Query `staleTime` (NFR-030).
   - Handle loading, error, and empty states.
   - Return `data`, `isLoading`, `isError`, `error`, `refetch`.

2. **Create `Icd10CodeTable` component**:
   - MUI `Table` with sortable columns: ICD-10 Code, Description, Confidence, Justification, Status (per SCR-014 component inventory: Table (2), Badge (N), Button (3), TextField (1), Modal (1)).
   - Header: `neutral.100` background, `subtitle1` typography per designsystem.md.
   - Rows: `body2` typography, 52px min height, alternating `neutral.50` stripe.
   - Sortable columns with `aria-sort` attribute per designsystem.md Table spec.
   - Multiple codes per diagnosis grouped and ranked by `relevance_rank` (AC-4).
   - Empty state: Illustration + message "No ICD-10 codes pending review" + CTA button.

3. **Create `Icd10CodeRow` component**:
   - Display: `code_value` (monospace font), `description`, `ConfidenceBadge`, `justification` text.
   - `ConfidenceBadge` per designsystem.md: High (>=80%) `#2E7D32` green, Medium (60-79%) `#ED6C02` amber, Low (<60%) `#D32F2F` red. Pill shape, `radius.full`, `overline` typography, `aria-label="AI confidence: XX%"` (UXR-105).
   - Justification text displayed in expandable row detail or tooltip (max 300px width per designsystem.md Tooltip spec).

4. **Create `UncodableAlert` component**:
   - MUI `Alert` with `severity="warning"` for diagnoses returning "uncodable" status.
   - Display: diagnosis name, "No matching ICD-10 code found — flagged for manual coding".
   - ARIA: `role="alert"` per designsystem.md Alert spec.

5. **Create `DeprecatedCodeWarning` component**:
   - Inline warning badge on rows where `revalidation_status = "deprecated_replaced"`.
   - Display: "Code deprecated — suggested replacement: {replacement_code}".
   - MUI `Chip` with `warning` color and link to replacement code details.

6. **Create `AiUnavailableBanner` component** (UXR-605):
   - Full-width `Alert` banner: "AI coding service unavailable — switch to manual coding".
   - "Switch to Manual" button triggers fallback workflow.
   - Shown when API returns AI service error or circuit breaker open status.

7. **Implement all 5 screen states for ICD-10 section**:
   - **Default**: Table with real data.
   - **Loading**: MUI `Skeleton` placeholders matching table layout (pulse animation, `neutral.200`).
   - **Empty**: Illustration + "No ICD-10 codes pending review" + action CTA.
   - **Error**: Alert banner with retry button.
   - **Validation**: Inline field errors with red borders (for override justification field).

8. **Apply responsive design**:
   - 375px (mobile): Single-column card layout, stacked code details.
   - 768px (tablet): Condensed table with horizontal scroll.
   - 1440px (desktop): Full table with all columns visible.

## Current Project State

```text
[Placeholder — to be updated based on dependent task completion]
app/
├── src/
│   ├── components/
│   │   └── coding/
│   │       ├── Icd10CodeTable.tsx          # New
│   │       ├── Icd10CodeRow.tsx            # New
│   │       ├── UncodableAlert.tsx          # New
│   │       ├── DeprecatedCodeWarning.tsx   # New
│   │       └── AiUnavailableBanner.tsx     # New
│   ├── hooks/
│   │   └── useIcd10Codes.ts               # New
│   └── pages/
│       └── MedicalCodingReview.tsx         # Modify — integrate ICD-10 table
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/hooks/useIcd10Codes.ts | React Query hook for fetching pending ICD-10 codes |
| CREATE | app/src/components/coding/Icd10CodeTable.tsx | Sortable table component for ICD-10 codes with ranking |
| CREATE | app/src/components/coding/Icd10CodeRow.tsx | Row component with ConfidenceBadge, justification, code display |
| CREATE | app/src/components/coding/UncodableAlert.tsx | Warning alert for uncodable diagnoses |
| CREATE | app/src/components/coding/DeprecatedCodeWarning.tsx | Warning chip for deprecated codes with replacement suggestion |
| CREATE | app/src/components/coding/AiUnavailableBanner.tsx | AI unavailability banner with manual fallback (UXR-605) |
| MODIFY | app/src/pages/MedicalCodingReview.tsx | Integrate Icd10CodeTable into SCR-014 page layout |

## External References

- [React 18 documentation](https://react.dev/)
- [MUI 5 Table component documentation](https://mui.com/material-ui/react-table/)
- [MUI 5 Alert component documentation](https://mui.com/material-ui/react-alert/)
- [React Query v4 documentation](https://tanstack.com/query/v4/docs)
- [WCAG 2.1 AA Table accessibility](https://www.w3.org/WAI/tutorials/tables/)

## Build Commands

- `cd app && npm run build`
- `cd app && npm test`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] ICD-10 code table renders with sortable columns (code, description, confidence, justification)
- [ ] ConfidenceBadge displays correct colors: green (>=80%), amber (60-79%), red (<60%)
- [ ] Multiple codes per diagnosis displayed ranked by relevance
- [ ] Uncodable diagnoses show warning alert with manual coding flag
- [ ] Deprecated codes show warning chip with replacement suggestion
- [ ] AI unavailability banner displays with "Switch to Manual" fallback button (UXR-605)
- [ ] All 5 screen states render correctly (Default, Loading, Empty, Error, Validation)
- [ ] Keyboard navigation works for all interactive elements (NFR-049)
- [ ] ARIA attributes present: `aria-sort` on table, `aria-label` on ConfidenceBadge, `role="alert"` on alerts

## Implementation Checklist

- [x] Create `useIcd10Codes` React Query hook with 5-min cache TTL and error/loading state handling
- [x] Create `Icd10CodeTable` with MUI Table, sortable columns, alternating row stripes, and empty state
- [x] Create `Icd10CodeRow` with ConfidenceBadge (color-coded per UXR-105), justification display, and code formatting
- [x] Create `UncodableAlert` component with MUI Alert for uncodable diagnoses
- [x] Create `DeprecatedCodeWarning` chip for deprecated codes with replacement suggestion
- [x] Create `CodingAiUnavailableBanner` with fallback button per UXR-605
- [x] Implement all 5 screen states (Default, Loading/Skeleton, Empty, Error, Validation)
- [x] Apply responsive breakpoints (375px, 768px, 1440px) and validate against wireframe
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
