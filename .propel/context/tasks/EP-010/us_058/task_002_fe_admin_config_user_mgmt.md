# Task - TASK_002

## Requirement Reference

- User Story: US_058
- Story Location: .propel/context/tasks/EP-010/us_058/us_058.md
- Acceptance Criteria:
    - AC-3: **Given** the admin dashboard is loaded, **When** the admin navigates to user management, **Then** they see a list of all staff and admin accounts with status, last login, and role.
    - AC-4: **Given** the admin clicks configuration, **When** the configuration panel opens, **Then** it provides tabs for appointment templates, business hours, notification templates, and risk thresholds.
- Edge Case:
    - How does the system handle the admin dashboard on mobile screens? Dashboard collapses metrics into collapsible cards and stacks configuration sections vertically.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-015-admin-dashboard.html` |
| **Screen Spec** | figma_spec.md#SCR-015 |
| **UXR Requirements** | UXR-003, UXR-004, UXR-102, UXR-303, UXR-403, UXR-501, UXR-502 |
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

Implement the configuration and user management panels of the admin dashboard (SCR-015). This task covers the tabbed configuration interface with four panels (Slot Templates, Notifications, Hours & Holidays, Risk Thresholds) and the user management panel displaying all staff and admin accounts. Includes inline validation (<200ms per UXR-501), auto-save for configuration fields (UXR-004), destructive action confirmation dialogs (UXR-102), responsive stacked layout on mobile (UXR-303), and skeleton loading states (UXR-502). Matches the Hi-Fi wireframe layout with Tab (4), TextField (N), Select (N), Table (2), Button (4), Toggle (N) component inventory from figma_spec.md.

## Dependent Tasks

- US_001 — Foundational — Requires backend API scaffold (React project setup, routing, auth context)
- US_008 — Foundational — Requires all domain entities for configuration
- task_001_fe_admin_metrics_dashboard — Requires AdminDashboardPage shell component and admin route
- task_005_db_admin_metrics_config_schema — Requires configuration and user management schema
- task_004_be_admin_config_user_mgmt_api — Requires configuration CRUD and user management API endpoints

## Impacted Components

- **NEW** `app/src/components/admin/ConfigTabs.tsx` — Tabbed navigation container for configuration panels
- **NEW** `app/src/components/admin/SlotTemplatesPanel.tsx` — Appointment slot template grid with provider selector
- **NEW** `app/src/components/admin/NotificationTemplatesPanel.tsx` — Notification template table with edit capability
- **NEW** `app/src/components/admin/BusinessHoursPanel.tsx` — Operating hours table and holiday management
- **NEW** `app/src/components/admin/RiskThresholdsPanel.tsx` — No-show risk threshold configuration fields
- **NEW** `app/src/components/admin/UserManagementPanel.tsx` — Staff/admin account list with status toggle
- **NEW** `app/src/hooks/useAdminConfig.ts` — React Query hooks for configuration CRUD operations
- **NEW** `app/src/hooks/useAdminUsers.ts` — React Query hooks for user management operations
- **NEW** `app/src/services/adminConfigService.ts` — API service layer for configuration endpoints
- **NEW** `app/src/services/adminUserService.ts` — API service layer for user management endpoints
- **MODIFY** `app/src/pages/AdminDashboard/AdminDashboardPage.tsx` — Integrate ConfigTabs and UserManagement below metrics section

## Implementation Plan

1. **Build ConfigTabs container** — Create a tabbed interface using MUI `Tabs` and `TabPanel` pattern with 4 tabs: Slot Templates, Notifications, Hours & Holidays, Risk Thresholds. Match wireframe tab order and styling. Apply `role="tablist"` and `aria-controls` for accessibility. On mobile (<600px), tabs switch to a scrollable `variant="scrollable"` mode (UXR-303).

2. **Implement SlotTemplatesPanel** — Build the weekly slot template grid matching the wireframe layout: provider selector (`Select` dropdown), 5-day × 5-timeslot grid with clickable cells toggling Available/Blocked states. Cells use `success.surface` for available and `neutral-100` for blocked per wireframe CSS. Include "Save Template" button. Apply auto-save on toggle (UXR-004).

3. **Implement NotificationTemplatesPanel** — Create a table (MUI `Table`) listing notification templates with columns: Template Name, Channel, Trigger, Status (badge), Actions (Edit button). Match wireframe table structure. Include "+ Add Template" button. Edit action opens inline editing or a modal for template content with variable placeholders.

4. **Implement BusinessHoursPanel** — Build a two-column layout (hours table + holidays list) matching wireframe. Hours section: table with day-of-week rows and operating hours per day with "Edit Hours" button. Holidays section: stacked holiday cards with name, date, and status badge (Closed/Half Day). Include "+ Add Holiday" button. On mobile, stack columns vertically (UXR-303).

5. **Implement RiskThresholdsPanel** — Create configuration form for no-show risk threshold and scoring parameters. Include fields for: risk score threshold (numeric input), weighting factors (slider or numeric inputs), and alert configuration. Apply inline validation within 200ms (UXR-501).

6. **Implement UserManagementPanel** — Build the user list matching wireframe: rows with avatar, user name, role subtitle, status badge (Active/Inactive), and activate/deactivate toggle. Include "+ Invite User" button. Deactivation triggers a confirmation dialog (UXR-102) per `Delete User Confirmation` dialog spec from figma_spec.md. Inactive users render at 60% opacity per wireframe.

7. **Create API hooks and services** — Implement `useAdminConfig` React Query hooks for GET/PUT configuration endpoints by category (slots, notifications, hours, risk-thresholds). Implement `useAdminUsers` for GET users list and PUT user activation/deactivation. Use mutation hooks with optimistic updates for toggle actions.

8. **Integrate into AdminDashboardPage** — Add ConfigTabs and UserManagementPanel sections below the metrics overview in AdminDashboardPage. Ensure vertical stacking on mobile viewports. Apply skeleton loading states for each panel during initial data fetch (UXR-502).

## Current Project State

- Project is in planning phase. No `app/` or `Server/` folders exist yet.
- AdminDashboardPage shell will be established by task_001.
- Placeholder to be updated during task execution based on dependent task completion.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `app/src/components/admin/ConfigTabs.tsx` | Tabbed container with 4 configuration panels |
| CREATE | `app/src/components/admin/SlotTemplatesPanel.tsx` | Slot template grid with provider selector and availability toggles |
| CREATE | `app/src/components/admin/NotificationTemplatesPanel.tsx` | Notification template table with CRUD actions |
| CREATE | `app/src/components/admin/BusinessHoursPanel.tsx` | Operating hours table and holiday card management |
| CREATE | `app/src/components/admin/RiskThresholdsPanel.tsx` | Risk threshold and scoring parameter configuration form |
| CREATE | `app/src/components/admin/UserManagementPanel.tsx` | Staff/admin account list with toggle and invite actions |
| CREATE | `app/src/hooks/useAdminConfig.ts` | React Query hooks for configuration CRUD endpoints |
| CREATE | `app/src/hooks/useAdminUsers.ts` | React Query hooks for user management endpoints |
| CREATE | `app/src/services/adminConfigService.ts` | API service for configuration endpoints |
| CREATE | `app/src/services/adminUserService.ts` | API service for user management endpoints |
| MODIFY | `app/src/pages/AdminDashboard/AdminDashboardPage.tsx` | Add ConfigTabs and UserManagementPanel sections |

## External References

- [MUI 5 Tabs Component](https://mui.com/material-ui/react-tabs/)
- [MUI 5 Table Component](https://mui.com/material-ui/react-table/)
- [MUI 5 Toggle/Switch Component](https://mui.com/material-ui/react-switch/)
- [MUI 5 Dialog Component](https://mui.com/material-ui/react-dialog/)
- [MUI 5 Select Component](https://mui.com/material-ui/react-select/)
- [MUI 5 Badge Component](https://mui.com/material-ui/react-badge/)
- [React Query v4 Mutations](https://tanstack.com/query/v4/docs/react/guides/mutations)
- [WCAG 2.1 AA Tab Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/tabs/)

## Build Commands

- Refer to applicable technology stack specific build commands in `.propel/build/`

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] 4 configuration tabs render and switch correctly with ARIA attributes
- [ ] Slot template grid toggles cells between Available/Blocked states
- [ ] Notification templates table displays all columns per wireframe
- [ ] Business hours and holidays render in two-column layout (stacked on mobile)
- [ ] User list displays status, last login, role with active/inactive toggle
- [ ] Deactivation confirmation dialog appears before toggle action (UXR-102)
- [ ] Inline validation triggers within 200ms on configuration fields (UXR-501)
- [ ] Skeleton loading states render for all panels during data fetch (UXR-502)

## Implementation Checklist

- [ ] Build `ConfigTabs` container with 4 tabs (Slot Templates, Notifications, Hours & Holidays, Risk Thresholds)
- [ ] Implement `SlotTemplatesPanel` with provider selector and weekly availability grid
- [ ] Implement `NotificationTemplatesPanel` with template table and edit/add actions
- [ ] Implement `BusinessHoursPanel` with hours table and holiday card management
- [ ] Implement `RiskThresholdsPanel` with threshold fields and inline validation (UXR-501)
- [ ] Implement `UserManagementPanel` with user list, status toggle, and deactivation confirmation (UXR-102)
- [ ] Create React Query hooks and API services for configuration CRUD and user management
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
