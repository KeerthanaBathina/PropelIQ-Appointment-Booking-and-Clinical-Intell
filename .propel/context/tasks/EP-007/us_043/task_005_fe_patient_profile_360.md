# Task - task_005_fe_patient_profile_360

## Requirement Reference

- User Story: US_043
- Story Location: .propel/context/tasks/EP-007/us_043/us_043.md
- Acceptance Criteria:
  - AC-1: Given multiple documents have been parsed for a patient, When consolidation runs, Then the system merges extracted medications, diagnoses, procedures, and allergies into a unified patient profile. (display merged profile)
  - AC-3: Given a staff member views the patient profile, When they select any data point, Then the source document citation is displayed linking the data to the original document section.
- Edge Case:
  - N/A (UI layer delegates edge case handling to backend services)

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-013-patient-profile-360.html |
| **Screen Spec** | figma_spec.md#SCR-013 |
| **UXR Requirements** | UXR-105, UXR-404, UXR-104, UXR-003, UXR-505 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

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

Implement the Patient Profile 360 screen (SCR-013) that displays the consolidated clinical data in a tabbed interface with medications, diagnoses, procedures, and allergies categories. The view includes AI confidence score badges (color-coded per UXR-105), clinical data category icons (per UXR-404), source document citation display on data point selection, conflict alert indicators, version history panel, and breadcrumb navigation (UXR-003). Supports all 5 required states: Default, Loading, Empty, Error, and Validation. Must match the Hi-Fi wireframe at 375px, 768px, and 1440px breakpoints.

## Dependent Tasks

- task_003_be_profile_api - Requires API endpoints for profile data, version history, and source citations
- EP-TECH - Requires React 18 + MUI 5 frontend scaffold

## Impacted Components

| Action | Component | Project |
|--------|-----------|---------|
| NEW | `PatientProfile360Page` page component | app (Pages) |
| NEW | `ProfileHeader` component | app (Components) |
| NEW | `ClinicalDataTabs` component | app (Components) |
| NEW | `DataPointTable` component | app (Components) |
| NEW | `ConfidenceBadge` component | app (Components) |
| NEW | `SourceCitationPanel` component | app (Components) |
| NEW | `VersionHistoryPanel` component | app (Components) |
| NEW | `ConflictAlertBanner` component | app (Components) |
| NEW | React Query hooks: `usePatientProfile`, `useVersionHistory`, `useSourceCitation` | app (Hooks) |

## Implementation Plan

1. Create `PatientProfile360Page` as the route component for `/patients/:patientId/profile` with breadcrumb navigation (UXR-003)
2. Implement `ProfileHeader` displaying patient name, DOB, avatar (primary-100 bg, primary-700 text per design tokens), document count, and current version number
3. Implement `ClinicalDataTabs` with 4 MUI Tabs: Medications, Diagnoses, Procedures, Allergies — each tab showing a `DataPointTable` for its category with distinct category icons/colors (UXR-404: lab=blue, prescription=green, note=purple, imaging=orange)
4. Implement `DataPointTable` using MUI DataGrid displaying data points with columns: name/description, details (dosage/code/date), confidence score badge, source document link, review status chip. Click handler opens `SourceCitationPanel` drawer (AC-3)
5. Implement `ConfidenceBadge` component with color-coded indicators: green (>=80%), amber (60-79%), red (<60%) per UXR-105 and design tokens (confidence.high=#2E7D32, medium=#ED6C02, low=#D32F2F)
6. Implement `SourceCitationPanel` as MUI Drawer displaying document name, category, upload date, and extraction section reference when a data point is clicked
7. Implement `ConflictAlertBanner` using MUI Alert (warning severity, warning-surface #FFF3E0 background) showing conflict count with link to conflict resolution modal. Implement `VersionHistoryPanel` as collapsible sidebar listing version entries
8. Implement all 5 screen states (Default with data, Loading skeleton, Empty state with upload prompt, Error with retry, Validation for conflict resolution) and add toast notifications for background operations (UXR-505)

## Current Project State

```text
[Placeholder - update based on dependent task completion state]
app/
  src/
    pages/
    components/
    hooks/
    services/
    theme/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/pages/PatientProfile360Page.tsx | Route page with breadcrumb, profile header, tabs, state management |
| CREATE | app/src/components/profile/ProfileHeader.tsx | Patient info header with avatar, name, DOB, document/version counts |
| CREATE | app/src/components/profile/ClinicalDataTabs.tsx | MUI Tabs for 4 clinical data categories with category icons |
| CREATE | app/src/components/profile/DataPointTable.tsx | MUI DataGrid for data points with confidence badges, source links |
| CREATE | app/src/components/profile/ConfidenceBadge.tsx | Color-coded confidence score indicator (green/amber/red) |
| CREATE | app/src/components/profile/SourceCitationPanel.tsx | MUI Drawer showing source document details for selected data point |
| CREATE | app/src/components/profile/VersionHistoryPanel.tsx | Collapsible sidebar listing profile version history |
| CREATE | app/src/components/profile/ConflictAlertBanner.tsx | MUI Alert banner for conflict count with resolution link |
| CREATE | app/src/hooks/usePatientProfile.ts | React Query hook for GET /api/patients/:id/profile |
| CREATE | app/src/hooks/useVersionHistory.ts | React Query hook for GET /api/patients/:id/profile/versions |
| CREATE | app/src/hooks/useSourceCitation.ts | React Query hook for GET /api/patients/:id/profile/data-points/:id/citation |

## External References

- [MUI 5 Tabs](https://mui.com/material-ui/react-tabs/) - Tab component for clinical data categories
- [MUI 5 DataGrid](https://mui.com/x/react-data-grid/) - Table component for data point display
- [MUI 5 Drawer](https://mui.com/material-ui/react-drawer/) - Side panel for source citations
- [MUI 5 Alert](https://mui.com/material-ui/react-alert/) - Warning banner for conflicts
- [React Query v4](https://tanstack.com/query/v4/docs/framework/react/overview) - Server state management and caching
- [MUI 5 Breadcrumbs](https://mui.com/material-ui/react-breadcrumbs/) - Navigation breadcrumbs

## Build Commands

- `cd app && npm install`
- `cd app && npm run build`
- `cd app && npm start`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] All 5 screen states render correctly (Default, Loading, Empty, Error, Validation)
- [ ] Confidence badges display correct colors for all 3 thresholds
- [ ] Source citation panel opens on data point click with correct document details
- [ ] Tabs switch between 4 data categories without data loss
- [ ] Breadcrumb navigation renders and navigates correctly

## Implementation Checklist

- [x] Create `PatientProfile360Page` with route setup, breadcrumb navigation (UXR-003), and state management orchestration
- [x] Implement `ProfileHeader` with patient avatar (primary-100 bg), name, DOB, document count, version number display
- [x] Implement `ClinicalDataTabs` with 4 MUI Tabs (Medications, Diagnoses, Procedures, Allergies) using distinct category icons and colors (UXR-404)
- [x] Implement `DataPointTable` with MUI DataGrid columns, click handler for source citation, and review status chips
- [x] Implement `ConfidenceBadge` with color thresholds (>=80% green, 60-79% amber, <60% red) per UXR-105
- [x] Implement `SourceCitationPanel` as MUI Drawer with document name, category, upload date, extraction section (AC-3)
- [x] Implement `ConflictAlertBanner` (warning-surface bg) and `VersionHistoryPanel` (collapsible sidebar with version list)
- [x] Implement all 5 screen states (Default, Loading skeleton, Empty with upload prompt, Error with retry, Validation) and toast notifications (UXR-505)
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
