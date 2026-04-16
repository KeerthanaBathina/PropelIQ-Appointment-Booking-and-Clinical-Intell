# Task - TASK_001

## Requirement Reference

- User Story: US_014
- Story Location: .propel/context/tasks/EP-001/us_014/us_014.md
- Acceptance Criteria:
  - AC-1: Given I am logged in, When I am inactive for 15 minutes, Then the system invalidates my session and redirects to login with message "Session expired due to inactivity."
  - AC-2: Given I am actively using the system, When I make a request within the 15-minute window, Then the session timer resets and I remain authenticated.
  - AC-4: Given a session expiry warning is configured, When 2 minutes remain before expiry, Then a modal countdown appears with an "Extend Session" button.
- Edge Cases:
  - Simultaneous requests from the same session: last-activity timestamp updates atomically; FE timer resets on any user interaction or API call.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-001-login.html |
| **Screen Spec** | figma_spec.md#SCR-001 |
| **UXR Requirements** | UXR-603 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement**

- **MUST** reference wireframe at `.propel/context/wireframes/Hi-Fi/wireframe-SCR-001-login.html` for the login redirect target
- **MUST** implement session timeout warning modal consistent with design system tokens
- **MUST** implement all error flow states from figma_spec.md (Session Timeout → Modal with extend/logout → Re-login → SCR-001)
- **MUST** validate modal at breakpoints: 375px, 768px, 1440px
- Run `/analyze-ux` after implementation to verify alignment

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

Implement the client-side session inactivity tracking, session timeout warning modal, and automatic redirect-to-login flow. The frontend tracks user activity (mouse, keyboard, touch, API calls) and maintains an inactivity timer set to 15 minutes. When 2 minutes remain (at the 13-minute mark), a modal countdown dialog appears with an "Extend Session" button that calls the backend to refresh the session (AC-4). If the user does not interact, the session is invalidated client-side and the user is redirected to SCR-001 login page with the message "Session expired due to inactivity" (AC-1). Any user activity within the 15-minute window resets the timer (AC-2). The modal follows the error handling flow defined in figma_spec.md (Session Timeout → Modal → extend/logout → Re-login).

## Dependent Tasks

- US_005 — Authentication scaffold (provides auth context, JWT token management, API interceptors)
- task_002_be_session_management — Backend session extend and invalidation endpoints must exist; FE can be developed in parallel using mocked responses

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `SessionTimeoutProvider` | app (Frontend) | CREATE — context provider tracking inactivity |
| `SessionTimeoutModal` | app (Frontend) | CREATE — countdown modal with Extend/Logout buttons |
| `useSessionTimeout` hook | app (Frontend) | CREATE — hook managing inactivity timer and event listeners |
| `useAuth` hook | app (Frontend) | MODIFY — add session extend and invalidate methods |
| `App` layout | app (Frontend) | MODIFY — wrap authenticated routes with SessionTimeoutProvider |
| API interceptor | app (Frontend) | MODIFY — reset inactivity timer on successful API responses |

## Implementation Plan

1. **Create useSessionTimeout hook** — Track user inactivity using event listeners for `mousemove`, `keydown`, `mousedown`, `touchstart`, and `scroll`. Maintain a `lastActivityTimestamp` in memory (not state, to avoid re-renders). Use `setInterval` (every 30 seconds) to compare current time against `lastActivityTimestamp`. When elapsed time >= 13 minutes (780,000ms), trigger the warning modal. When elapsed time >= 15 minutes (900,000ms), trigger session invalidation. On any tracked event, reset `lastActivityTimestamp` to `Date.now()` (AC-2). Throttle event listener callbacks to once per second to avoid performance overhead. Clean up all listeners and intervals on unmount.

2. **Create SessionTimeoutModal component** — MUI Dialog (modal) with:
   - Title: "Session Expiring Soon" (h3 typography)
   - Body: "Your session will expire in {countdown} seconds due to inactivity." using real-time countdown from 120 seconds to 0.
   - Primary button: "Extend Session" — calls `POST /api/auth/extend-session`, resets inactivity timer on success, closes modal.
   - Secondary button: "Logout Now" — calls logout flow, redirects to login.
   - Countdown uses `setInterval(1000)` updating a local state counter.
   - Modal is non-dismissible (no backdrop click close) to ensure user makes an explicit choice.
   - When countdown reaches 0, auto-trigger session invalidation without user action.
   - Accessible: focus trap within modal, ARIA role="alertdialog", ARIA live region for countdown.

3. **Create SessionTimeoutProvider** — React context provider wrapping all authenticated routes. Provides `{ showWarning, timeRemaining, extendSession, logout }` to children. Uses `useSessionTimeout` hook internally. Only active when user is authenticated (skip for login, register, public pages). Renders `SessionTimeoutModal` when `showWarning` is true.

4. **Implement session extend flow** — When "Extend Session" is clicked:
   - Call `POST /api/auth/extend-session` (sends current refresh token).
   - On 200: backend returns new JWT + refresh token, reset inactivity timer to 15 minutes, close modal.
   - On 401: session already expired server-side, redirect to login with expiry message.
   - On error: show inline error in modal, allow retry.

5. **Implement session invalidation flow** — When timer reaches 15 minutes or countdown reaches 0:
   - Call `POST /api/auth/logout` to invalidate server-side session (best-effort, don't block on failure).
   - Clear JWT and refresh token from storage (HttpOnly cookies cleared by server Set-Cookie).
   - Clear Zustand auth state.
   - Navigate to `/login` with query param `?expired=true`.
   - Login page reads `expired=true` and displays MUI Alert: "Session expired due to inactivity." (AC-1).

6. **Integrate with API interceptor** — Modify the Axios/fetch interceptor to reset the inactivity timer on every successful API response (2xx status). This ensures that programmatic activity (auto-refresh, polling) also counts as "active" (AC-2). On 401 response (token expired server-side), immediately trigger the invalidation flow without waiting for the client timer.

7. **Display session expiry message on login page** — Modify the login page (`SCR-001`) to read the `?expired=true` query parameter. When present, show a MUI Alert (severity="warning") at the top of the login form: "Session expired due to inactivity. Please sign in again." Auto-dismiss after 10 seconds or on form interaction.

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
app/                          # Frontend React application
├── src/
│   ├── pages/                # Page-level components
│   ├── components/           # Shared components
│   ├── hooks/                # Custom React hooks
│   ├── context/              # React context providers
│   ├── services/             # API service layer
│   └── routes/               # Route definitions
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/hooks/useSessionTimeout.ts | Inactivity timer hook with event listeners and threshold checks |
| CREATE | app/src/components/SessionTimeoutModal.tsx | MUI Dialog with countdown, Extend Session, and Logout buttons |
| CREATE | app/src/context/SessionTimeoutProvider.tsx | Context provider wrapping authenticated routes |
| MODIFY | app/src/hooks/useAuth.ts | Add extendSession() and invalidateSession() methods |
| MODIFY | app/src/App.tsx | Wrap authenticated route layout with SessionTimeoutProvider |
| MODIFY | app/src/services/apiClient.ts | Reset inactivity timer on 2xx responses; trigger invalidation on 401 |
| MODIFY | app/src/pages/LoginPage.tsx | Read ?expired=true query param and display warning alert |

## External References

- [React — useEffect Cleanup for Event Listeners](https://react.dev/reference/react/useEffect#connecting-to-an-external-system)
- [MUI 5 — Dialog Component](https://mui.com/material-ui/react-dialog/)
- [MUI 5 — Alert Component](https://mui.com/material-ui/react-alert/)
- [WCAG 2.1 AA — Timing Adjustable (2.2.1)](https://www.w3.org/WAI/WCAG21/Understanding/timing-adjustable.html)
- [ARIA — alertdialog Role](https://www.w3.org/WAI/ARIA/apd/#alertdialog)

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
- [ ] After 13 minutes of inactivity, session timeout warning modal appears
- [ ] Modal countdown decrements from 120 to 0 in real-time
- [ ] Clicking "Extend Session" calls API, resets timer, and closes modal
- [ ] Clicking "Logout Now" triggers immediate logout and redirect to login
- [ ] Countdown reaching 0 auto-invalidates session and redirects to login
- [ ] Login page displays "Session expired due to inactivity" alert when ?expired=true
- [ ] Any mouse/keyboard/touch activity during the 15-minute window resets the timer (AC-2)
- [ ] API responses (2xx) reset the inactivity timer
- [ ] 401 API response triggers immediate session invalidation
- [ ] Modal is non-dismissible (no backdrop click, no escape key close)
- [ ] Modal has focus trap and ARIA role="alertdialog" (accessibility)
- [ ] Event listeners cleaned up on unmount (no memory leaks)
- [ ] Throttled event handlers (1 per second) — no performance degradation

## Implementation Checklist

- [ ] Create `useSessionTimeout` hook with activity event listeners, throttled callbacks, and 30s interval check
- [ ] Create `SessionTimeoutModal` with MUI Dialog, live countdown, "Extend Session" and "Logout Now" buttons
- [ ] Create `SessionTimeoutProvider` context wrapping authenticated routes
- [ ] Implement session extend flow calling `POST /api/auth/extend-session` with timer reset
- [ ] Implement session invalidation flow: clear tokens, clear state, redirect to `/login?expired=true`
- [ ] Integrate API interceptor to reset timer on 2xx and trigger invalidation on 401
- [ ] Modify login page to display session expiry warning alert from query parameter
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
