# Task - TASK_001

## Requirement Reference

- User Story: US_016
- Story Location: .propel/context/tasks/EP-001/us_016/us_016.md
- Acceptance Criteria:
  - AC-1: Given MFA is enabled for a staff user, When they log in with valid credentials, Then the system prompts for a TOTP code before granting access — FE must render a TOTP input step between credential submission and dashboard redirect.
  - AC-2: Given a user fails 5 consecutive login attempts, Then the account is locked for 30 minutes and the user sees "Account locked. Try again in 30 minutes." — FE must display lockout error state on SCR-001.
  - AC-3: Given the lockout period has passed, When the user attempts to log in with correct credentials, Then login succeeds — FE clears lockout state and proceeds normally.
  - AC-4: Given a user successfully logs in, When the dashboard loads, Then it displays "Last login: [timestamp] from [IP address/location]." — FE must render last-login info on the dashboard.
- Edge Cases:
  - MFA device lost: admin can reset MFA; FE must reflect MFA-disabled state after admin action.
  - Login during lockout: each attempt returns same lockout message without resetting timer; FE displays remaining time.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-001-login.html |
| **Screen Spec** | figma_spec.md#SCR-001 |
| **UXR Requirements** | UXR-201, UXR-601 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement**

- **MUST** open and reference wireframe at `.propel/context/wireframes/Hi-Fi/wireframe-SCR-001-login.html` during implementation
- **MUST** match layout, spacing, typography, and colors from the wireframe for the login error states
- **MUST** implement MFA TOTP step consistent with the MFA Setup modal defined in figma_spec.md (P1 modal on SCR-001)
- **MUST** validate implementation against wireframe at breakpoints: 375px, 768px, 1440px
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

Implement three frontend features related to enhanced authentication security. **(1) MFA TOTP Step**: After staff/admin users submit valid credentials, render a TOTP code input step (6-digit field) before granting access; include MFA setup modal for first-time enrollment showing QR code and backup codes. **(2) Account Lockout UI**: Display lockout error on SCR-001 login page with a dynamic countdown showing remaining lock time when the backend returns 423 Locked (AC-2); show the same message for repeated attempts during lockout. **(3) Last Login Display**: After successful login, show "Last login: [formatted timestamp] from [IP/location]" on the role-specific dashboard (SCR-005/SCR-010/SCR-015) using data returned in the login response (AC-4).

## Dependent Tasks

- US_005 — Authentication scaffold (provides auth context, login flow, routing)
- US_013 task_001 — Role-based dashboard router (provides dashboard pages where last-login info is displayed)
- task_002_be_mfa_lockout_audit — Backend MFA, lockout, and login-tracking endpoints must exist; FE can develop in parallel with mocked responses

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `MfaTotpStep` | app (Frontend) | CREATE — 6-digit TOTP input UI shown after credential validation |
| `MfaSetupModal` | app (Frontend) | CREATE — QR code display & backup codes for first-time MFA enrollment |
| `AccountLockoutAlert` | app (Frontend) | CREATE — lockout error banner with dynamic countdown timer |
| `LastLoginBanner` | app (Frontend) | CREATE — info banner displaying last login timestamp and location |
| `useLogin` hook | app (Frontend) | MODIFY — handle MFA-required response, lockout response, last-login data |
| `useMfaSetup` hook | app (Frontend) | CREATE — React Query mutation for MFA enable/verify endpoints |
| `LoginPage` | app (Frontend) | MODIFY — integrate MfaTotpStep and AccountLockoutAlert into login flow |
| Dashboard pages | app (Frontend) | MODIFY — render LastLoginBanner on SCR-005, SCR-010, SCR-015 |

## Implementation Plan

1. **Modify login flow to handle MFA and lockout responses** — Extend the `useLogin` hook to detect backend response codes:
   - **200 with `mfaRequired: true`**: Credential validation passed but MFA is enabled. Store temporary auth token. Transition to TOTP input step (do NOT redirect to dashboard yet).
   - **200 with `lastLogin` object**: Login fully successful. Store `lastLogin: { timestamp, ipAddress }` in Zustand auth state for dashboard display.
   - **423 Locked with `lockedUntil` timestamp**: Account locked. Display lockout alert with dynamic countdown.
   - **401 with `remainingAttempts` count**: Invalid credentials. Show error with "X attempts remaining before lockout."
   - **401 without remaining attempts**: Generic "Invalid email or password" (per existing wireframe error state).

2. **Build MfaTotpStep component** — Displayed on the login page after credential validation succeeds for MFA-enabled users. Layout:
   - Centered card (reuse login form layout from wireframe).
   - Heading: "Two-Factor Authentication" (h2).
   - Description: "Enter the 6-digit code from your authenticator app."
   - 6-digit input field using MUI TextField with `inputMode="numeric"`, `maxLength={6}`, autoFocus. Auto-submit when 6 digits entered.
   - "Verify" primary button (full width).
   - "Use backup code" link switching to a backup code input.
   - "Cancel" link returning to credential input.
   - States: Default, Loading (verifying code), Error ("Invalid code. Please try again." with remaining attempts info), Success (redirect to dashboard).
   - Call `POST /api/auth/mfa/verify` with the temporary token + TOTP code.

3. **Build MfaSetupModal component** — MUI Dialog for first-time MFA enrollment (triggered from staff/admin settings or prompted by admin policy). Content:
   - Step 1: Display QR code image (from `GET /api/auth/mfa/setup` returning `otpAuthUrl`). Show manual entry key below. "Next" button.
   - Step 2: Verify setup by entering a TOTP code from the authenticator app. Call `POST /api/auth/mfa/verify-setup`.
   - Step 3: Display backup codes (8 codes, one-time use). "Download" and "Copy" buttons. Checkbox: "I have saved my backup codes." "Done" button.
   - Non-dismissible until setup completes (if admin-enforced).
   - Accessible: focus trap, ARIA labels on code inputs, screen reader announcements.

4. **Build AccountLockoutAlert component** — Rendered on the login page when lockout is active. MUI Alert (severity="error"):
   - Text: "Account locked. Try again in {minutes}:{seconds}."
   - Dynamic countdown using `setInterval(1000)` comparing current time to `lockedUntil` timestamp.
   - When countdown reaches 0, clear the alert and re-enable the login form.
   - During lockout, login button remains enabled but submissions return the same lockout message (edge case — no timer reset).
   - Actionable error with "Contact support if you need immediate access." link (UXR-601).

5. **Build LastLoginBanner component** — MUI Alert (severity="info") or subtle info bar rendered at the top of dashboard pages (SCR-005, SCR-010, SCR-015):
   - Text: "Last login: {formattedTimestamp} from {ipAddress}"
   - Timestamp formatted using user's locale (e.g., "Apr 16, 2026, 3:45 PM").
   - IP address displayed as-is (location lookup is server-side, display city/country if provided).
   - Auto-dismissible after 10 seconds or on user interaction.
   - Read `lastLogin` data from Zustand auth state (populated during login response).
   - Skip display if `lastLogin` data is null (first-ever login).

6. **Integrate components into login page and dashboards** — Login page (SCR-001): Render `AccountLockoutAlert` above the form when lockout is active. After credential success for MFA users, swap form content to `MfaTotpStep`. Dashboard pages: Insert `LastLoginBanner` below the header/navigation. MFA setup modal: callable from staff/admin settings page or auto-triggered on first login when MFA policy is enforced.

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
app/                          # Frontend React application
├── src/
│   ├── pages/                # Page-level components (LoginPage, Dashboards)
│   ├── components/           # Shared components
│   │   ├── auth/             # Auth-specific components
│   │   │   ├── MfaTotpStep.tsx
│   │   │   ├── MfaSetupModal.tsx
│   │   │   ├── AccountLockoutAlert.tsx
│   │   │   └── LastLoginBanner.tsx
│   ├── hooks/                # Custom React hooks
│   └── routes/               # Route definitions
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/components/auth/MfaTotpStep.tsx | 6-digit TOTP input with verify/cancel/backup-code actions |
| CREATE | app/src/components/auth/MfaSetupModal.tsx | QR code display, verify setup, backup codes (3-step modal) |
| CREATE | app/src/components/auth/AccountLockoutAlert.tsx | Lockout error with dynamic countdown timer |
| CREATE | app/src/components/auth/LastLoginBanner.tsx | Info banner with last login timestamp and IP |
| CREATE | app/src/hooks/useMfaSetup.ts | React Query hooks for MFA setup and verify-setup endpoints |
| MODIFY | app/src/hooks/useLogin.ts | Handle mfaRequired, lockedUntil, remainingAttempts, lastLogin responses |
| MODIFY | app/src/pages/LoginPage.tsx | Integrate MfaTotpStep and AccountLockoutAlert into login flow |
| MODIFY | app/src/pages/PatientDashboard.tsx | Render LastLoginBanner (SCR-005) |
| MODIFY | app/src/pages/StaffDashboard.tsx | Render LastLoginBanner (SCR-010) |
| MODIFY | app/src/pages/AdminDashboard.tsx | Render LastLoginBanner (SCR-015) |

## External References

- [MUI 5 — Dialog Component (MFA modal)](https://mui.com/material-ui/react-dialog/)
- [MUI 5 — Alert Component](https://mui.com/material-ui/react-alert/)
- [React — Controlled Input for Numeric Code](https://react.dev/reference/react-dom/components/input)
- [TOTP RFC 6238 — Time-Based One-Time Password](https://datatracker.ietf.org/doc/html/rfc6238)
- [WCAG 2.1 AA — Error Identification (3.3.1)](https://www.w3.org/WAI/WCAG21/Understanding/error-identification.html)
- [QR Code Generation — qrcode.react Library](https://github.com/zpao/qrcode.react)

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
- [ ] MFA TOTP step appears after valid credentials for MFA-enabled user
- [ ] 6-digit input auto-submits on complete entry
- [ ] Invalid TOTP code shows error with retry
- [ ] "Use backup code" link switches to backup code input
- [ ] MFA setup modal displays QR code and manual entry key
- [ ] Backup codes displayed and downloadable/copyable
- [ ] Lockout alert shows "Account locked. Try again in XX:XX" with live countdown (AC-2)
- [ ] Countdown reaches 0 and clears lockout state, re-enabling login (AC-3)
- [ ] Login attempts during lockout show same message without timer reset
- [ ] Last login banner shows "Last login: [timestamp] from [IP]" on dashboard (AC-4)
- [ ] Last login banner auto-dismisses after 10 seconds
- [ ] First-ever login skips last-login display gracefully
- [ ] Keyboard navigation works for TOTP input, modal, and alerts (UXR-201)
- [ ] Error messages include actionable recovery instructions (UXR-601)
- [ ] Focus trap in MFA setup modal

## Implementation Checklist

- [X] Modify `useLogin` hook to handle MFA-required (200 + mfaRequired), lockout (423 + lockedUntil), and lastLogin responses
- [X] Build `MfaTotpStep` with 6-digit numeric input, auto-submit, verify/cancel actions, backup code fallback
- [X] Build `MfaSetupModal` with QR code display, verify-setup step, and backup codes (3-step flow)
- [X] Build `AccountLockoutAlert` with dynamic countdown timer and actionable error message
- [X] Build `LastLoginBanner` reading last-login data from auth state, auto-dismiss after 10 seconds
- [X] Integrate MfaTotpStep and AccountLockoutAlert into LoginPage (SCR-001)
- [X] Integrate LastLoginBanner into PatientDashboard, StaffDashboard, AdminDashboard
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
