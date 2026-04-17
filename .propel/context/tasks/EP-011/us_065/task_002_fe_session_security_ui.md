# Task - TASK_002

## Requirement Reference

- User Story: US_065
- Story Location: .propel/context/tasks/EP-011/us_065/us_065.md
- Acceptance Criteria:
  - AC-1: Given a user is authenticated, When 15 minutes of inactivity pass, Then the session is automatically invalidated and the user is redirected to the login page.
  - AC-2: Given a user is logged in on Device A, When they attempt to log in on Device B, Then the session on Device A is terminated and the user is notified of concurrent session prevention.
  - AC-3: Given a user account exists, When 5 consecutive failed login attempts occur, Then the account is locked for 30 minutes and an audit log entry records the lockout event — frontend displays lockout feedback on login form.
  - AC-4: Given a session timeout is approaching, When 2 minutes remain, Then a modal countdown with "Extend Session" button is displayed to the user.
- Edge Cases:
  - Session extension request fails due to network issues: System invalidates the session on timeout; next page load redirects to login with "Session expired" message.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | UXR-603 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **N/A**: No wireframe specified for this story (Session Security Hardening — non-screen-specific)

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

Implement the frontend session security UI components for session timeout warning, concurrent session termination notification, and account lockout feedback. This task provides the client-side enforcement of FR-094 (automatic session timeout after 15 minutes of inactivity), NFR-014 (session timeout user experience), NFR-015 (concurrent session prevention notification), and NFR-016 (account lockout user feedback after 5 failed attempts). It extends the existing `SessionTimeoutProvider` and `SessionTimeoutModal` from US_014/task_001_fe_session_timeout_ui to handle concurrent session termination detection (Device A notification via 440 response), display account lockout error state on the login form, and synchronize the session timeout warning countdown with the backend `GET /api/session/time-remaining` endpoint instead of relying solely on client-side timers. Handles the network error edge case where session extension fails gracefully.

## Dependent Tasks

- US_005 — Authentication scaffold (provides auth context, JWT token management, API interceptors)
- US_014 / task_001_fe_session_timeout_ui — SessionTimeoutProvider, SessionTimeoutModal, useSessionTimeout hook (base components to extend)
- US_065 / task_001_be_session_security_hardening — Backend endpoints: `GET /api/session/time-remaining`, 440 response for session termination, 423 response for account lockout

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `SessionTimeoutProvider` | app (Frontend) | MODIFY — add server-side time sync via `GET /api/session/time-remaining`, handle 440 termination responses |
| `SessionTimeoutModal` | app (Frontend) | MODIFY — add network error fallback state, sync countdown with server TTL |
| `SessionTerminatedAlert` | app (Frontend) | CREATE — notification component shown when session is terminated by concurrent login |
| `useSessionTimeout` hook | app (Frontend) | MODIFY — integrate server time-remaining polling, handle 440 and 423 responses |
| `LoginForm` | app (Frontend) | MODIFY — add account lockout error state with remaining lockout duration display |
| API interceptor | app (Frontend) | MODIFY — intercept 440 (SESSION_TERMINATED) and 423 (Locked) responses globally |

## Implementation Plan

1. **Modify API interceptor for 440 and 423 responses**: Update the Axios/fetch response interceptor to handle two new HTTP status codes:
   - **440 (Session Terminated)**: Parse `{ "code": "SESSION_TERMINATED", "reason": "concurrent_login", "message": "..." }` from response body. Clear local auth state (tokens, user context). Redirect to login page with query parameter `?reason=session_terminated`. Store the termination message in session storage for display on the login page.
   - **423 (Locked)**: Parse `{ "message": "Account locked...", "lockoutEnd": "{utc_timestamp}" }` from response body. Do NOT clear auth state (user is not authenticated). Pass the lockout info to the login form error state.
   - **401 (Unauthorized — existing)**: No change; existing behavior redirects to login with "Session expired" message.

2. **Create SessionTerminatedAlert component**: MUI Alert (severity: warning) displayed at the top of the login page when `?reason=session_terminated` query parameter is present:
   - Title: "Session Terminated" (h6 typography, bold).
   - Body: "Your session was ended because your account signed in from another device. If this wasn't you, please change your password immediately."
   - Include a "Dismiss" icon button (MUI IconButton with CloseIcon) that removes the alert and clears the query parameter.
   - Auto-dismiss after 30 seconds.
   - Accessible: `role="alert"`, `aria-live="assertive"`.
   - Responsive: full-width on mobile (375px), max-width 480px on desktop (1440px), centered.

3. **Modify useSessionTimeout hook — Server Time Sync**: Enhance the existing client-side inactivity timer with periodic server synchronization:
   - Every 60 seconds (configurable), call `GET /api/session/time-remaining` to get the authoritative remaining seconds from Redis TTL.
   - If server reports `remainingSeconds <= 120` (warning threshold), trigger the warning modal immediately (even if client timer hasn't reached 13 minutes yet). This corrects for client-server clock drift.
   - If server reports `remainingSeconds === null` or returns 401, the session has expired server-side — trigger immediate redirect to login (AC-1).
   - If the server call fails (network error), log the failure and fall back to client-side timer only. Do NOT invalidate the session based on a network failure alone.
   - Use React Query's `useQuery` with `refetchInterval: 60000` and `retry: 1` for the polling. Disable polling when the warning modal is already visible (switch to the 1-second countdown).

4. **Modify SessionTimeoutModal — Network Error Handling**: Add a fallback state when the `POST /api/session/extend` call fails:
   - If the extend call returns a network error (timeout, connection refused, DNS failure):
     - Display inline error text below the "Extend Session" button: "Unable to extend session. Please check your network connection." (MUI Typography, color: error).
     - Keep the countdown running. If countdown reaches 0, invalidate session and redirect to login regardless of network status (AC-1 and edge case).
     - Disable the "Extend Session" button for 5 seconds after failure, then re-enable for retry.
   - If the extend call returns 401 (session already expired server-side):
     - Close the modal immediately and redirect to login with "Session expired" message.
   - If the extend call succeeds:
     - Reset the countdown timer, close the modal, and resume normal activity tracking (no change from US_014).

5. **Modify LoginForm — Account Lockout Error State**: Add a lockout-specific error display to the login form:
   - When login API returns 423 Locked:
     - Parse `lockoutEnd` from response body.
     - Calculate remaining lockout minutes: `Math.ceil((lockoutEndDate - now) / 60000)`.
     - Display an MUI Alert (severity: error) above the login form:
       - Text: "Your account has been locked due to 5 failed login attempts. Please try again in {N} minutes."
       - Include a countdown that updates every minute (not every second — avoid unnecessary re-renders).
     - Disable the "Sign In" button while the lockout is active.
     - When the lockout expires (countdown reaches 0), re-enable the button and change the alert to: "Account unlocked. You may try again." (severity: info, auto-dismiss after 10 seconds).
   - Accessible: `role="alert"`, `aria-live="polite"`.

6. **Handle concurrent session termination in SessionTimeoutProvider**: Add a listener for 440 responses within the provider:
   - When any API call receives a 440 response (intercepted by the global interceptor), the provider should:
     - Immediately stop the inactivity timer and polling.
     - Clear auth state (tokens removed from memory and cookies).
     - Navigate to login page with `?reason=session_terminated` (React Router `useNavigate`).
   - This ensures the termination is handled even if the 440 occurs during a background API call (e.g., React Query refetch).

## Current Project State

```text
app/                                   # Frontend React 18 application
├── src/
│   ├── components/
│   │   ├── auth/
│   │   │   ├── LoginForm.tsx
│   │   │   ├── SessionTimeoutModal.tsx
│   │   │   └── SessionTerminatedAlert.tsx   # NEW
│   │   └── common/
│   ├── context/
│   │   └── SessionTimeoutProvider.tsx
│   ├── hooks/
│   │   ├── useAuth.ts
│   │   └── useSessionTimeout.ts
│   ├── services/
│   │   └── api/
│   │       └── interceptors.ts
│   └── App.tsx
├── package.json
└── tsconfig.json
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | app/src/services/api/interceptors.ts | Add global handling for 440 (SESSION_TERMINATED) and 423 (Locked) HTTP responses |
| CREATE | app/src/components/auth/SessionTerminatedAlert.tsx | MUI Alert component displayed on login page when session was terminated by concurrent login |
| MODIFY | app/src/hooks/useSessionTimeout.ts | Add server time-remaining polling (60s interval), sync countdown with server TTL, handle 440/401 responses |
| MODIFY | app/src/components/auth/SessionTimeoutModal.tsx | Add network error fallback state for failed session extend calls |
| MODIFY | app/src/components/auth/LoginForm.tsx | Add account lockout error state with remaining minutes countdown and button disabling |
| MODIFY | app/src/context/SessionTimeoutProvider.tsx | Add 440 response listener for concurrent session termination, stop timers, navigate to login |

## External References

- [MUI Dialog — Modal Component](https://mui.com/material-ui/react-dialog/)
- [MUI Alert — Feedback Component](https://mui.com/material-ui/react-alert/)
- [React Query — Polling with refetchInterval](https://tanstack.com/query/v4/docs/react/guides/window-focus-refetching)
- [React Router v6 — useNavigate](https://reactrouter.com/en/main/hooks/use-navigate)
- [WCAG 2.1 AA — Alert Role](https://www.w3.org/WAI/WCAG21/Understanding/status-messages.html)
- [OWASP — Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html)
- [Axios — Interceptors](https://axios-http.com/docs/interceptors)

## Build Commands

```powershell
# Install dependencies
cd app
npm install

# Development server
npm run dev

# Production build
npm run build

# Run tests
npm run test
```

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against design system tokens at 375px, 768px, 1440px
- [ ] Session timeout warning modal appears at 2 minutes remaining (AC-4)
- [ ] Warning modal countdown syncs with server `GET /api/session/time-remaining` response
- [ ] "Extend Session" button resets timer and closes modal on success
- [ ] "Extend Session" failure shows inline error message and allows retry after 5 seconds
- [ ] Session expired redirect to login page with "Session expired" message on countdown reaching 0 (AC-1)
- [ ] 440 response triggers redirect to login with `?reason=session_terminated` (AC-2)
- [ ] SessionTerminatedAlert displays warning on login page when `?reason=session_terminated` present
- [ ] SessionTerminatedAlert auto-dismisses after 30 seconds
- [ ] 423 response shows lockout error on login form with remaining minutes (AC-3)
- [ ] Login button disabled during active lockout
- [ ] Lockout countdown updates every minute, re-enables button on expiry
- [ ] All alerts use `role="alert"` and `aria-live` attributes (WCAG 2.1 AA)
- [ ] Keyboard navigation works for all interactive elements (NFR-049)
- [ ] Input sanitization on all form inputs (NFR-018)

## Implementation Checklist

- [ ] Modify API interceptor to handle 440 (SESSION_TERMINATED) and 423 (Locked) HTTP responses globally
- [ ] Create `SessionTerminatedAlert` component with auto-dismiss, accessibility attributes, and responsive layout
- [ ] Modify `useSessionTimeout` hook to poll `GET /api/session/time-remaining` every 60 seconds and sync countdown with server TTL
- [ ] Modify `SessionTimeoutModal` to show inline network error on extend failure with retry logic
- [ ] Modify `LoginForm` to display account lockout error with remaining minutes countdown and disable sign-in button
- [ ] Modify `SessionTimeoutProvider` to listen for 440 responses, clear auth state, and navigate to login with termination reason
