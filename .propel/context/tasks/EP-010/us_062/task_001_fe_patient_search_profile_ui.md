# Task - task_001_fe_patient_search_profile_ui

## Requirement Reference

- User Story: US_062
- Story Location: .propel/context/tasks/EP-010/us_062/us_062.md
- Acceptance Criteria:
    - AC-1: **Given** the staff member opens the patient search, **When** they enter a name, DOB, or phone number, **Then** the system returns matching results within 1 second with relevance ranking.
    - AC-2: **Given** search results are displayed, **When** the staff member selects a patient, **Then** the system navigates to the complete patient profile view.
    - AC-3: **Given** the patient profile is displayed, **When** it loads, **Then** it shows sections for demographics, appointment history, intake data, uploaded documents, extracted data, and medical codes.
    - AC-4: **Given** the staff member views the patient profile, **When** they click on any section, **Then** the section expands to show detailed information with links to source screens.
- Edge Cases:
    - No results: System displays "No patients found" with a suggestion to verify search criteria or create a new patient record.
    - Partial name search: System supports partial matching (e.g., "Joh" matches "John," "Johnston") with highlighting of matched characters.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-016-patient-search.html |
| **Screen Spec** | figma_spec.md#SCR-016 |
| **UXR Requirements** | UXR-005, UXR-502 |
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
| Backend | N/A | - |
| Database | N/A | - |
| AI/ML | N/A | - |
| Vector Store | N/A | - |
| AI Gateway | N/A | - |
| Mobile | N/A | - |

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

Implement the Patient Search page (SCR-016) and Patient Profile view component as a React SPA feature. The search page provides staff and admin users with the ability to search patients by name, date of birth, or phone number, displaying results in a sortable, paginated table with relevance ranking. Selecting a patient navigates to a comprehensive profile view with expandable sections covering demographics, appointment history, intake data, uploaded documents, extracted clinical data, and medical codes. All 5 screen states (Default, Loading, Empty, Error, Validation) must be implemented per figma_spec.md. The implementation must follow the Hi-Fi wireframe layout for SCR-016 and conform to the project design system tokens.

## Dependent Tasks

- US_008 - Foundational - Requires all domain entities for patient data (entity models must exist)
- US_004 - Foundational - Requires Redis caching for search performance (cache infrastructure must exist)

## Impacted Components

- **NEW** `PatientSearchPage` component — Main search page (SCR-016) with search form, results table, pagination
- **NEW** `PatientProfileView` component — Complete profile view with expandable sections
- **NEW** `PatientSearchBar` component — Reusable search input with debounce (used in staff header per UXR-005)
- **NEW** `PatientResultsTable` component — Sortable results table with highlighted partial matches
- **NEW** `ProfileSection` component — Reusable expandable section with link to source screen
- **NEW** `usePatientSearch` hook — React Query hook for search API integration
- **NEW** `usePatientProfile` hook — React Query hook for profile data aggregation
- **MODIFY** Staff sidebar navigation — Add active "Patients" nav item linking to SCR-016
- **MODIFY** App router — Add routes for `/staff/patients/search` and `/staff/patients/:id/profile`

## Implementation Plan

1. **Route and Navigation Setup**: Register `/staff/patients/search` and `/staff/patients/:id/profile` routes in the app router. Add "Patients" navigation item to the staff sidebar with search icon, matching the wireframe active state styling (secondary-500 border and color).

2. **PatientSearchBar Component**: Build a reusable MUI `TextField` with `type="search"` and 300ms debounce using `useDeferredValue` or lodash debounce. Support placeholder text "Name, MRN, DOB, phone, or email…" per wireframe. Wire to the search API via React Query.

3. **PatientSearchPage (SCR-016)**: Implement the full search page matching the wireframe layout:
   - Search card with search input, provider dropdown (`Select` with "All Providers"), status dropdown ("All", "Active", "Inactive"), and "Search" button
   - Result count display ("Showing N results for 'query'")
   - Results table with columns: Name (clickable link), MRN, DOB, Phone, Provider, Last Visit, Status (badge)
   - Pagination controls (Previous/Next with page info)
   - All 5 states: Default (empty search), Loading (skeleton rows), Empty ("No patients found" card with guidance), Error (error alert with retry), Validation (inline field errors)

4. **Patient Results Table**: Use MUI `Table` with sortable columns (Name, DOB) per wireframe `.sortable` class. Implement partial match character highlighting in patient name cells. Inactive patients rendered with reduced opacity (0.7) per wireframe. Status shown as MUI `Chip` badges (Active=success, Inactive=default).

5. **PatientProfileView Component**: Build the profile view with 6 expandable `Accordion` sections using MUI:
   - Demographics (name, DOB, phone, email, emergency contact)
   - Appointment History (table with status, date, provider)
   - Intake Data (mandatory/optional fields, insurance info)
   - Uploaded Documents (document list with category, upload date, status)
   - Extracted Clinical Data (medications, diagnoses, procedures, allergies with confidence scores)
   - Medical Codes (ICD-10/CPT codes with justification, approval status)
   Each section includes a link to its source screen (e.g., appointments link to SCR-007, documents to SCR-012).

6. **React Query Integration**: Create `usePatientSearch` hook calling `GET /api/patients/search` with query parameters. Create `usePatientProfile` hook calling `GET /api/patients/{id}/profile`. Configure staleTime per React Query best practices (30s for search, 60s for profile).

7. **Skeleton Loading (UXR-502)**: Implement skeleton loading placeholders for the results table (skeleton rows) and profile sections (skeleton content blocks) shown during data fetches exceeding 300ms.

8. **Accessibility and Responsiveness**: Ensure WCAG 2.1 AA compliance: ARIA labels on search input, table, and navigation elements. Keyboard-navigable table rows and expandable sections. Focus management on navigation from search to profile. Responsive layout: search form fields stack vertically on mobile (<768px), 2-column desktop per UXR-303. Touch targets ≥44x44px on mobile per UXR-304.

## Current Project State

- [Placeholder — to be updated based on completion of dependent tasks US_008 and US_004]

```text
app/
├── src/
│   ├── components/
│   ├── pages/
│   ├── hooks/
│   ├── services/
│   └── routes/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/pages/staff/PatientSearchPage.tsx | Main search page component (SCR-016) with search form, results, pagination, all 5 states |
| CREATE | app/src/pages/staff/PatientProfileView.tsx | Patient profile view with 6 expandable accordion sections |
| CREATE | app/src/components/staff/PatientSearchBar.tsx | Reusable debounced search input for staff header (UXR-005) |
| CREATE | app/src/components/staff/PatientResultsTable.tsx | Sortable results table with partial match highlighting and pagination |
| CREATE | app/src/components/staff/ProfileSection.tsx | Reusable expandable section with source screen link |
| CREATE | app/src/hooks/usePatientSearch.ts | React Query hook for patient search API |
| CREATE | app/src/hooks/usePatientProfile.ts | React Query hook for patient profile aggregation API |
| MODIFY | app/src/routes/staffRoutes.tsx | Add /staff/patients/search and /staff/patients/:id/profile routes |
| MODIFY | app/src/components/layout/StaffSidebar.tsx | Add "Patients" nav item with search icon |

## External References

- [React 18 Documentation](https://react.dev/)
- [MUI 5 Table Component](https://mui.com/material-ui/react-table/)
- [MUI 5 Accordion Component](https://mui.com/material-ui/react-accordion/)
- [MUI 5 TextField Component](https://mui.com/material-ui/react-text-field/)
- [React Query v4 Documentation](https://tanstack.com/query/v4/docs/overview)
- [WCAG 2.1 AA Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)

## Build Commands

- Refer to applicable technology stack specific build commands at `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Skeleton loading displays for fetches >300ms (UXR-502)
- [ ] "No patients found" empty state renders with guidance text
- [ ] Partial match highlighting displays correctly for name searches
- [ ] All 5 SCR-016 states render correctly (Default, Loading, Empty, Error, Validation)
- [ ] Keyboard navigation works for table rows and accordion sections
- [ ] ARIA labels present on search input, results table, and interactive elements

## Implementation Checklist

- [ ] Register staff patient routes (`/staff/patients/search`, `/staff/patients/:id/profile`) in app router
- [ ] Create `PatientSearchBar` component with debounced MUI `TextField` and search icon
- [ ] Create `PatientSearchPage` with search card (input + provider filter + status filter + search button) matching wireframe layout
- [ ] Create `PatientResultsTable` with sortable columns, partial match highlighting, pagination, and status badges
- [ ] Create `PatientProfileView` with 6 expandable `Accordion` sections (demographics, appointments, intake, documents, extracted data, codes) each with source screen links
- [ ] Implement all 5 SCR-016 states (Default, Loading/Skeleton, Empty, Error with retry, Validation)
- [ ] Create `usePatientSearch` and `usePatientProfile` React Query hooks with appropriate staleTime and error handling
- [ ] Add WCAG 2.1 AA accessibility (ARIA labels, keyboard navigation, focus management) and responsive layout (mobile stacked, desktop side-by-side)
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
