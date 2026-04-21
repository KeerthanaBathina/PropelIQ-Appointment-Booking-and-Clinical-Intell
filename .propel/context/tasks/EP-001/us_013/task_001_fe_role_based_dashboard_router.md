# Task - TASK_001

## Requirement Reference

- User Story: US_013
- Story Location: .propel/context/tasks/EP-001/us_013/us_013.md
- Acceptance Criteria:
  - AC-1: Given RBAC is implemented, When a Patient user attempts to access a staff-only endpoint, Then the system returns 403 Forbidden — FE must display an "Access Denied" state and prevent navigation to unauthorized routes.
  - AC-2: Given a Staff user is authenticated, When they access staff dashboard endpoints, Then the system grants access — FE must route Staff users to SCR-010 Staff Dashboard.
  - AC-3: Given an Admin user is authenticated, When they access admin configuration endpoints, Then full system configuration is accessible — FE must route Admin users to SCR-015 Admin Dashboard.
  - AC-4: Given a JWT token contains a role claim, When the authorization middleware processes the request, Then it validates the role — FE must read the role claim from the JWT to determine routing.
- Edge Cases:
  - Role changed during active session: existing JWT remains valid until expiry; new role applies on next token refresh. FE must handle token refresh and re-evaluate routing.
  - Tampered role claims: JWT signature validation rejects the token with 401; FE must redirect to login on 401.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-002-dashboard-router.html |
| **Screen Spec** | figma_spec.md#SCR-002 |
| **UXR Requirements** | UXR-403 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement**

- **MUST** open and reference wireframe at `.propel/context/wireframes/Hi-Fi/wireframe-SCR-002-dashboard-router.html` during implementation
- **MUST** match layout, spacing, typography, and colors from the wireframe
- **MUST** implement all states: Default, Loading, Empty, Error, Validation (per figma_spec.md SCR-002)
- **MUST** validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify pixel-perfect alignment

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | React | 18.x |
| UI Component Library | Material-UI (MUI) | 5.x |
| State Management | React Query + Zustand | 4.x / 4.x |
| Language | TypeScript | 5.x |
| Backend | N/A (consumed via API) | - |
| Database | N/A | - |
| AI/ML | N/A | - |
| Mobile | N/A | - |

**Note**: All code and libraries MUST be compatible with versions above.

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

Implement the Role-Based Dashboard Router page (SCR-002) as a React component using MUI 5. After successful login, the user lands on this page which reads the role claim from the decoded JWT token and auto-redirects to the appropriate role-specific dashboard within 3 seconds: Patient → SCR-005 Patient Dashboard, Staff → SCR-010 Staff Dashboard, Admin → SCR-015 Admin Dashboard. The page also implements client-side route guards that prevent users from navigating to routes outside their role. Patient-facing screens use the primary color accent while staff-facing screens use the secondary color accent per UXR-403. An "Access Denied" page is rendered when a user attempts to reach an unauthorized route, and 401 responses trigger redirect to login.

## Dependent Tasks

- US_005 — Authentication scaffold (provides JWT decode utility, auth context, routing base)
- task_002_be_rbac_authorization — RBAC middleware must be in place for API-level enforcement; FE can be developed in parallel using mocked role claims

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `DashboardRouterPage` | app (Frontend) | CREATE — role-based router page (SCR-002) |
| `RoleGuard` | app (Frontend) | CREATE — route guard HOC/component checking role claim |
| `AccessDeniedPage` | app (Frontend) | CREATE — 403 Forbidden display page |
| `useAuth` hook | app (Frontend) | MODIFY — add role extraction from JWT and role-check helpers |
| `ProtectedRoute` | app (Frontend) | CREATE — wrapper component enforcing role-based access |
| App routing | app (Frontend) | MODIFY — wrap role-specific routes with ProtectedRoute guards |

## Implementation Plan

1. **Extend useAuth hook with role utilities** — Decode the JWT access token to extract the role claim (`Patient`, `Staff`, `Admin`). Add helper methods: `getUserRole()`, `hasRole(role: string)`, `isPatient()`, `isStaff()`, `isAdmin()`. On 401 API response, clear auth state and redirect to login. On token refresh, re-evaluate the role for session role-change handling (edge case).

2. **Build DashboardRouterPage component** — Centered layout matching wireframe (max-width 800px, background neutral-100). Display 3 role cards in a 3-column grid (single column on mobile < 768px per wireframe media query). Patient card uses `primary-500` icon color, Staff card uses `secondary-500`, Admin card uses `neutral-700` per UXR-403 and wireframe. Show "Welcome back" heading and "Auto-redirecting based on your role in 3 seconds..." message. On mount, read role from auth context and auto-redirect after 3-second delay using `setTimeout` + `useNavigate()`. Screen states: Default (cards visible with countdown), Loading (skeleton while decoding JWT), Error (role not found — show fallback message).

3. **Build ProtectedRoute component** — Accepts `allowedRoles: string[]` prop. Reads current user role from auth context. If role is in `allowedRoles`, render children. If role is not in `allowedRoles`, redirect to `/access-denied`. If not authenticated, redirect to `/login`. Wrap in React component for clean JSX composition in route definitions.

4. **Build AccessDeniedPage component** — Simple centered card with MUI Alert (severity="error") displaying "Access Denied — You do not have permission to view this page." Include a "Go to Dashboard" button that routes back to `/dashboard` (SCR-002) for re-routing. Include a "Sign Out" button for switching accounts.

5. **Apply role-based visual treatment** — Per UXR-403, patient-facing routes use MUI ThemeProvider with primary color accent (`primary-500: #1976D2`), staff-facing routes use secondary color accent (`secondary-500: #7B1FA2`). Create a `RoleThemeProvider` component that wraps route groups and applies the appropriate MUI theme overrides based on the active user role.

6. **Wire routes with ProtectedRoute guards** — Update app routing configuration:
   - `/dashboard` → `DashboardRouterPage` (any authenticated user)
   - `/patient/*` → `ProtectedRoute allowedRoles={["Patient"]}` → Patient screens (SCR-005 to SCR-009)
   - `/staff/*` → `ProtectedRoute allowedRoles={["Staff"]}` → Staff screens (SCR-010 to SCR-014, SCR-016)
   - `/admin/*` → `ProtectedRoute allowedRoles={["Admin"]}` → Admin screens (SCR-015)
   - `/access-denied` → `AccessDeniedPage`
   - Handle 401/403 API responses globally via Axios/fetch interceptor — redirect to login on 401, redirect to access-denied on 403.

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
app/                          # Frontend React application
├── src/
│   ├── pages/                # Page-level components
│   ├── components/           # Shared components
│   ├── hooks/                # Custom React hooks
│   ├── guards/               # Route guard components
│   ├── theme/                # MUI theme configuration
│   └── routes/               # Route definitions
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/pages/DashboardRouterPage.tsx | Role-based router page with 3 role cards, auto-redirect |
| CREATE | app/src/pages/AccessDeniedPage.tsx | 403 Forbidden display with navigation back to dashboard |
| CREATE | app/src/guards/ProtectedRoute.tsx | Route guard checking role claim against allowedRoles prop |
| CREATE | app/src/guards/RoleGuard.tsx | HOC wrapping children when role condition is met |
| CREATE | app/src/theme/RoleThemeProvider.tsx | MUI ThemeProvider with role-based color accents (UXR-403) |
| MODIFY | app/src/hooks/useAuth.ts | Add getUserRole(), hasRole(), isPatient(), isStaff(), isAdmin() |
| MODIFY | app/src/routes/index.tsx | Wrap role-specific routes with ProtectedRoute, add /dashboard, /access-denied |

## External References

- [React Router v6 — Protected Routes Pattern](https://reactrouter.com/en/main/start/overview)
- [MUI 5 — Theming (ThemeProvider)](https://mui.com/material-ui/customization/theming/)
- [MUI 5 — Card Component](https://mui.com/material-ui/react-card/)
- [JWT Decode — jwt-decode Library](https://github.com/auth0/jwt-decode)
- [WCAG 2.1 AA — Focus Management](https://www.w3.org/WAI/WCAG21/Understanding/focus-visible.html)

## Build Commands

- `npm run build` — Production build
- `npm run dev` — Development server
- `npm run lint` — ESLint check
- `npm run type-check` — TypeScript compilation check

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] Patient role auto-redirects to SCR-005 Patient Dashboard
- [ ] Staff role auto-redirects to SCR-010 Staff Dashboard
- [ ] Admin role auto-redirects to SCR-015 Admin Dashboard
- [ ] Patient attempting staff/admin route sees AccessDeniedPage
- [ ] Staff attempting admin route sees AccessDeniedPage
- [ ] 401 API response redirects to login page
- [ ] 403 API response redirects to access-denied page
- [ ] Token refresh updates role and re-evaluates routing
- [ ] Patient screens use primary color accent (UXR-403)
- [ ] Staff screens use secondary color accent (UXR-403)
- [ ] 3-column grid on desktop, single column on mobile (< 768px)
- [ ] Keyboard navigation works for all role cards and buttons
- [ ] Focus indicators visible on interactive elements

## Implementation Checklist

- [x] Extend `useAuth` hook with role extraction from JWT (getUserRole, hasRole, isPatient, isStaff, isAdmin)
- [x] Build `DashboardRouterPage` with 3 role cards, auto-redirect, and screen states (Default/Loading/Error)
- [x] Build `ProtectedRoute` component enforcing allowedRoles against user's JWT role claim
- [x] Build `AccessDeniedPage` with error alert, "Go to Dashboard" and "Sign Out" buttons
- [x] Build `RoleThemeProvider` applying primary accent for patient, secondary accent for staff (UXR-403)
- [x] Wire routes: /dashboard, /patient/*, /staff/*, /admin/*, /access-denied with ProtectedRoute guards
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
