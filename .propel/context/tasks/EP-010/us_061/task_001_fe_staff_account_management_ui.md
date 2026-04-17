# Task - task_001_fe_staff_account_management_ui

## Requirement Reference

- User Story: us_061
- Story Location: .propel/context/tasks/EP-010/us_061/us_061.md
- Acceptance Criteria:
    - AC-1: **Given** the admin opens user management, **When** they create a new staff account, **Then** the system provisions the account with role assignment (Staff or Admin), temporary password, and email invitation.
    - AC-2: **Given** the admin views staff accounts, **When** the list loads, **Then** it displays name, email, role, status (active/deactivated), last login, and creation date.
    - AC-3: **Given** the admin deactivates a staff account, **When** they confirm deactivation, **Then** the account is disabled (cannot log in) but all historical data (audit logs, actions, verifications) is preserved.
    - AC-4: **Given** a staff account is deactivated, **When** an admin reactivates it, **Then** the account is restored with previous role and permissions intact.
- Edge Cases:
    - EC-1: Admin tries to deactivate their own account → System prevents self-deactivation with error "Cannot deactivate your own account."
    - EC-2: Admin deactivates the last admin account → System prevents it with error "At least one active admin account required."

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-015-admin-dashboard.html` |
| **Screen Spec** | figma_spec.md#SCR-015 |
| **UXR Requirements** | UXR-102, UXR-303, UXR-403, UXR-501 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path
> - **PENDING**: UI-impacting task awaiting wireframe (provide file or URL)
> - **EXTERNAL**: Wireframe provided via external URL
> - **N/A**: Task has no UI impact

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

Implement the Staff Account Management UI within the Users tab of the Admin Configuration Dashboard (SCR-015). This frontend task builds the interactive interface that enables administrators to view, create, deactivate, and reactivate staff accounts. The implementation includes a searchable/filterable data table displaying staff account details, a creation dialog with role assignment and inline validation, and deactivation/reactivation controls with confirmation dialogs. All five screen states (Default, Loading, Empty, Error, Validation) must be implemented per SCR-015 specifications.

**Effort Estimate**: 6 hours

**Traceability**: US_061 AC-1, AC-2, AC-3, AC-4, EC-1, EC-2 | FR-087, FR-088 | UXR-102, UXR-303, UXR-403, UXR-501

## Dependent Tasks

- task_003_db_staff_account_schema — Requires database schema for staff account fields
- task_002_be_staff_account_management_api — Requires API endpoints for staff CRUD operations
- US_058 tasks — Requires admin dashboard framework and tab navigation structure (SCR-015 shell)
- US_005 tasks — Requires authentication scaffold and JWT context for current user identification

## Impacted Components

| Action | Component / Module | Project |
|--------|--------------------|---------|
| CREATE | `StaffAccountList` component — Staff data table with search/filter | Frontend (app) |
| CREATE | `AddStaffDialog` component — Modal form for creating staff accounts | Frontend (app) |
| CREATE | `DeactivateConfirmDialog` component — Confirmation dialog for deactivation | Frontend (app) |
| CREATE | `useStaffAccounts` hook — React Query hooks for staff API integration | Frontend (app) |
| MODIFY | Admin Dashboard Users tab — Integrate staff management components | Frontend (app) |

## Implementation Plan

1. **Create React Query hooks** (`useStaffAccounts`): Define `useQuery` for fetching paginated staff list with search/filter params, `useMutation` for create, deactivate, and reactivate operations. Configure optimistic updates and cache invalidation on mutations.

2. **Build StaffAccountList component**: Use MUI `DataGrid` or `Table` to render staff records with columns: Name, Email, Role (chip), Status (active/deactivated badge), Last Login (formatted date), Creation Date. Add search `TextField` and role/status `Select` filters above the table. Implement pagination via React Query. Handle Loading state with `Skeleton`, Empty state with `EmptyState` illustration, and Error state with retry action.

3. **Build AddStaffDialog component**: MUI `Dialog` with form fields — Full Name (`TextField`, required), Email (`TextField`, required, email validation), Role (`Select` with Staff/Admin options). Inline validation on blur within 200ms (UXR-501). On submit, call create mutation; on success, display success `Toast` and refresh list. On validation failure from API (duplicate email, etc.), display field-level errors.

4. **Build DeactivateConfirmDialog component**: MUI `Dialog` triggered when admin clicks deactivate toggle/button. Display warning message with account name. Disable deactivate button and show error message "Cannot deactivate your own account" when target is current user (EC-1). Handle API error for last admin guard (EC-2) by displaying "At least one active admin account required." Reactivation uses same toggle without confirmation dialog.

5. **Integrate with Admin Dashboard Users tab**: Mount `StaffAccountList` within the existing Users tab of SCR-015. Add "Create Staff Account" `Button` in the tab header that opens `AddStaffDialog`. Wire deactivate/reactivate actions from table rows to the appropriate dialogs/mutations.

6. **Implement responsive layout**: Stack table columns and form fields vertically on mobile (≤768px) per UXR-303. Ensure 2-column form layout on desktop. Apply secondary color accent for admin screens per UXR-403. Add breadcrumb navigation: Admin Dashboard > User Management.

7. **Implement all 5 screen states** per SCR-015: Default (populated table), Loading (skeleton placeholders), Empty (no staff accounts — "No staff accounts found" with create CTA), Error (API failure with retry button), Validation (inline field errors on forms).

## Current Project State

```text
[Placeholder — to be updated based on completion of dependent tasks US_058, US_005]
app/
├── src/
│   ├── components/
│   │   └── admin/          # Admin dashboard components (from US_058)
│   ├── hooks/              # React Query hooks
│   ├── pages/
│   │   └── admin/          # Admin pages (from US_058)
│   ├── services/           # API service layer
│   └── types/              # TypeScript type definitions
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/src/hooks/useStaffAccounts.ts` | React Query hooks for staff list query, create/deactivate/reactivate mutations |
| CREATE | `app/src/components/admin/StaffAccountList.tsx` | Staff account data table with search, filter, pagination, and 5 screen states |
| CREATE | `app/src/components/admin/AddStaffDialog.tsx` | Modal dialog for creating new staff account with role selection and inline validation |
| CREATE | `app/src/components/admin/DeactivateConfirmDialog.tsx` | Confirmation dialog for staff deactivation with self-deactivation and last-admin guards |
| CREATE | `app/src/types/staff.ts` | TypeScript interfaces for StaffAccount, CreateStaffRequest, StaffListFilter |
| CREATE | `app/src/services/staffApi.ts` | API service functions for staff management endpoints |
| MODIFY | `app/src/pages/admin/AdminDashboard.tsx` | Integrate StaffAccountList into Users tab, add "Create Staff" button |

## External References

- [MUI DataGrid documentation (v5)](https://mui.com/x/react-data-grid/) — Table component reference
- [MUI Dialog documentation (v5)](https://mui.com/material-ui/react-dialog/) — Modal dialog patterns
- [React Query v4 mutations](https://tanstack.com/query/v4/docs/framework/react/guides/mutations) — Optimistic updates and cache invalidation
- [WCAG 2.1 AA - Forms](https://www.w3.org/WAI/WCAG21/Understanding/labels-or-instructions.html) — Form accessibility requirements

## Build Commands

```bash
# Frontend build
cd app
npm install
npm run build

# Frontend dev server
npm run dev

# Type checking
npx tsc --noEmit

# Lint
npm run lint
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Staff list renders correctly with all 6 columns (name, email, role, status, last login, creation date)
- [ ] Search and filter functionality works for name, email, role, and status
- [ ] Create staff dialog validates email format and required fields within 200ms
- [ ] Deactivation confirmation dialog displays before account deactivation
- [ ] Self-deactivation attempt shows "Cannot deactivate your own account" error
- [ ] Last admin deactivation attempt shows "At least one active admin account required" error
- [ ] Reactivation restores account to active status in the UI
- [ ] All 5 screen states (Default, Loading, Empty, Error, Validation) render correctly
- [ ] Responsive layout verified at 375px, 768px, and 1440px breakpoints
- [ ] Keyboard navigation works for all interactive elements (UXR-501, NFR-049)

## Implementation Checklist

- [ ] Create TypeScript interfaces in `staff.ts` for StaffAccount, CreateStaffRequest, StaffListFilter, StaffListResponse
- [ ] Create API service functions in `staffApi.ts` for GET /api/admin/users, POST /api/admin/users, PUT /api/admin/users/{id}/deactivate, PUT /api/admin/users/{id}/reactivate
- [ ] Create `useStaffAccounts` React Query hook with paginated list query, create mutation, deactivate mutation, reactivate mutation, and cache invalidation
- [ ] Build `StaffAccountList` component with MUI Table/DataGrid, search TextField, role/status Select filters, pagination, and all 5 screen states (Default, Loading, Empty, Error, Validation)
- [ ] Build `AddStaffDialog` component with MUI Dialog, form fields (name, email, role), inline validation on blur (<200ms per UXR-501), and success/error feedback
- [ ] Build `DeactivateConfirmDialog` component with MUI Dialog, self-deactivation guard (EC-1), last-admin API error handling (EC-2), and destructive action confirmation (UXR-102)
- [ ] Integrate staff management components into Admin Dashboard Users tab with "Create Staff Account" button and responsive layout (UXR-303, UXR-403)
- [ ] Add breadcrumb navigation (Admin Dashboard > User Management) and verify WCAG 2.1 AA accessibility (NFR-046, NFR-049)
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
