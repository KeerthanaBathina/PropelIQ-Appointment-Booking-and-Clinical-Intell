# Task - TASK_001

## Requirement Reference

- User Story: US_015
- Story Location: .propel/context/tasks/EP-001/us_015/us_015.md
- Acceptance Criteria:
  - AC-1: Given I am on the login page and click "Forgot Password", When I enter my registered email address, Then the system sends a password reset link within 2 minutes — FE must provide the email input form, loading state, and success confirmation.
  - AC-2: Given I receive the reset email, When I click the reset link within 1 hour, Then I am directed to a secure page where I can set a new compliant password — FE must implement the new-password form with password complexity validation.
  - AC-3: Given the reset link has expired (1-hour window), When I click it, Then the system displays "Link expired" and offers to resend a new reset link — FE must render the expired state with resend option.
  - AC-4: Given I successfully reset my password, When the reset completes, Then I see a success message and am directed to login — FE must show success confirmation and redirect to SCR-001.
- Edge Cases:
  - Non-registered email submitted: FE always displays "If an account exists, a reset link was sent" regardless of backend response (prevent email enumeration).
  - Multiple reset requests: FE does not track token validity client-side; relies on backend to invalidate prior tokens.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-004-password-reset.html |
| **Screen Spec** | figma_spec.md#SCR-004 |
| **UXR Requirements** | UXR-201, UXR-501, UXR-601 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement**

- **MUST** open and reference wireframe at `.propel/context/wireframes/Hi-Fi/wireframe-SCR-004-password-reset.html` during implementation
- **MUST** match layout, spacing, typography, and colors from the wireframe
- **MUST** implement all states: Default, Loading, Empty, Error, Validation (per figma_spec.md SCR-004)
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

Implement the password reset flow across two views on the SCR-004 screen using React and MUI 5. **View 1 — Request Reset**: centered card with email input, "Send Reset Link" button, and "Back to Sign In" link (matching the wireframe). After submission, show a generic success message regardless of whether the email exists (anti-enumeration). **View 2 — Set New Password**: reached via the email link containing a reset token; presents a new-password form with password complexity validation (8+ chars, 1 uppercase, 1 number, 1 special char), password strength indicator, and confirm-password field. Handles expired tokens with a "Link expired" message and resend option (AC-3). On successful reset, displays success confirmation and redirects to login (SCR-001). All views implement inline validation within 200ms (UXR-501), actionable error messages (UXR-601), and WCAG 2.1 AA accessibility (UXR-201).

## Dependent Tasks

- US_005 — Authentication scaffold (provides routing, auth context, API client configuration)
- task_002_be_password_reset_api — Backend reset endpoints must exist; FE can be developed in parallel using mocked API responses

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `ForgotPasswordPage` | app (Frontend) | CREATE — email input form (View 1 of SCR-004) |
| `ResetPasswordPage` | app (Frontend) | CREATE — new-password form (View 2 of SCR-004) |
| `PasswordStrengthIndicator` | app (Frontend) | REUSE — from US_012 task_001 (shared component) |
| `useForgotPassword` hook | app (Frontend) | CREATE — React Query mutation for forgot-password API |
| `useResetPassword` hook | app (Frontend) | CREATE — React Query mutation for reset-password API |
| `resetPasswordSchema` | app (Frontend) | CREATE — Zod/Yup validation schema for new password |
| App routing | app (Frontend) | MODIFY — add `/forgot-password` and `/reset-password` routes |

## Implementation Plan

1. **Create ForgotPasswordPage component (View 1)** — Centered card matching wireframe (max-width 440px, background neutral-0 on neutral-100 shell, shadow-1, padding sp-8). Lock icon at top (primary-500), "Reset your password" heading (h2), description text (body2, neutral-600), email input with label, "Send Reset Link" primary button (full width), "← Back to Sign In" link. Screen states:
   - **Default**: Form visible, button enabled.
   - **Validation**: Inline error on invalid email format, triggered on blur within 200ms (UXR-501).
   - **Loading**: Button disabled with spinner, "Send Reset Link" becomes "Sending...".
   - **Success**: Replace form with success message: "If an account exists with that email, a password reset link has been sent. Please check your inbox." Include "← Back to Sign In" link. Same message for both registered and non-registered emails (anti-enumeration edge case).
   - **Error**: MUI Alert (severity="error") for server/network errors with retry guidance (UXR-601).

2. **Create ResetPasswordPage component (View 2)** — Reached via URL `/reset-password?token={token}&email={email}`. On mount, validate the token presence in URL (if missing, redirect to forgot-password). Present a centered card with:
   - "Set your new password" heading.
   - New password field with `PasswordStrengthIndicator` (reuse from US_012).
   - Confirm password field with match validation.
   - "Reset Password" primary button (full width).
   - Screen states:
     - **Default**: Form visible, fields empty.
     - **Validation**: Inline errors for password complexity (8+ chars, 1 uppercase, 1 number, 1 special char) and confirm-password mismatch, within 200ms (UXR-501).
     - **Loading**: Button disabled with spinner.
     - **Success**: Replace form with "Password reset successfully! You can now sign in with your new password." Include "Sign In" button linking to SCR-001 login.
     - **Error (expired token)**: Display "Link expired — this reset link is no longer valid." with "Request New Reset Link" button linking to `/forgot-password` (AC-3).
     - **Error (invalid token)**: Display "Invalid reset link" with "Request New Reset Link" button.
     - **Error (server)**: MUI Alert with retry guidance (UXR-601).

3. **Create validation schema for new password** — Zod or Yup schema enforcing: required new password with complexity rules (8+ chars, 1 uppercase, 1 number, 1 special char), required confirm password matching new password. Each criterion maps to a specific inline error message. Reuse the same complexity rules as US_012 registration schema via shared validation utility.

4. **Create React Query hooks** — `useForgotPassword`: POST `/api/auth/forgot-password` with email body. On 200 (regardless of email existence), show success state. On 429 (rate limited), show "Too many requests, please wait" error. On 5xx, show server error with retry. `useResetPassword`: POST `/api/auth/reset-password` with token, email, newPassword. On 200, show success state. On 400 (invalid token), show invalid-token error. On 410 (expired token), show expired-link state with resend option. On 422 (password complexity failure), show validation errors.

5. **Add routes and navigation** — Add `/forgot-password` route rendering `ForgotPasswordPage`. Add `/reset-password` route rendering `ResetPasswordPage`. Login page (SCR-001) already has "Forgot Password?" link targeting `/forgot-password` (per wireframe navigation map). ForgotPasswordPage "Back to Sign In" links to `/login`. ResetPasswordPage success state "Sign In" links to `/login`.

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
app/                          # Frontend React application
├── src/
│   ├── pages/                # Page-level components
│   ├── components/           # Shared components (PasswordStrengthIndicator from US_012)
│   ├── hooks/                # Custom React hooks
│   ├── validation/           # Validation schemas
│   ├── services/             # API service layer
│   └── routes/               # Route definitions
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/pages/ForgotPasswordPage.tsx | Email input form with success/error states (View 1 of SCR-004) |
| CREATE | app/src/pages/ResetPasswordPage.tsx | New-password form with token validation, expired/success states (View 2) |
| CREATE | app/src/hooks/useForgotPassword.ts | React Query mutation for POST /api/auth/forgot-password |
| CREATE | app/src/hooks/useResetPassword.ts | React Query mutation for POST /api/auth/reset-password |
| CREATE | app/src/validation/resetPasswordSchema.ts | Zod/Yup schema for new password + confirm password |
| MODIFY | app/src/routes/index.tsx | Add /forgot-password and /reset-password routes |

## External References

- [React Hook Form v7 — MUI Integration](https://react-hook-form.com/get-started#IntegratingwithUIlibraries)
- [MUI 5 — TextField API](https://mui.com/material-ui/api/text-field/)
- [MUI 5 — Alert Component](https://mui.com/material-ui/react-alert/)
- [Zod — Schema Validation](https://zod.dev/)
- [React Query useMutation](https://tanstack.com/query/v4/docs/react/reference/useMutation)
- [WCAG 2.1 AA — Error Identification (3.3.1)](https://www.w3.org/WAI/WCAG21/Understanding/error-identification.html)
- [OWASP — Forgot Password Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html)

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
- [ ] ForgotPasswordPage displays generic success message regardless of email existence (anti-enumeration)
- [ ] ForgotPasswordPage inline validation triggers within 200ms on blur (UXR-501)
- [ ] ForgotPasswordPage loading state disables button and shows spinner
- [ ] ResetPasswordPage reads token and email from URL query params
- [ ] ResetPasswordPage password complexity validation shows specific missing criteria (8+ chars, uppercase, number, special)
- [ ] ResetPasswordPage confirm-password mismatch shows inline error
- [ ] PasswordStrengthIndicator updates in real-time (reused from US_012)
- [ ] Expired token (410) shows "Link expired" with resend option (AC-3)
- [ ] Invalid token (400) shows error with "Request New Reset Link" button
- [ ] Successful reset shows confirmation and "Sign In" link to SCR-001 (AC-4)
- [ ] Error messages include actionable recovery instructions (UXR-601)
- [ ] Keyboard navigation works for all interactive elements (UXR-201/WCAG)
- [ ] ARIA labels present on all form inputs
- [ ] Focus indicators visible on all interactive elements

## Implementation Checklist

- [ ] Build `ForgotPasswordPage` with email input, states (Default/Validation/Loading/Success/Error), anti-enumeration message
- [ ] Build `ResetPasswordPage` with new-password form, confirm field, token validation, expired/invalid/success states
- [ ] Create `resetPasswordSchema` with password complexity rules and confirm-password match (reuse shared validation)
- [ ] Create React Query hooks (`useForgotPassword`, `useResetPassword`) handling 200/400/410/422/429/5xx responses
- [ ] Add `/forgot-password` and `/reset-password` routes; wire navigation from login and between views
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
