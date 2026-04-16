# Task - TASK_001

## Requirement Reference

- User Story: US_012
- Story Location: .propel/context/tasks/EP-001/us_012/us_012.md
- Acceptance Criteria:
  - AC-1: Given valid name, email, phone, DOB, and password submitted, Then account created with status "pending verification" and verification email sent within 2 minutes.
  - AC-2: Given verification link clicked, Then account status changes to "active" and user redirected to login page with success message.
  - AC-3: Given expired verification link (1-hour expiry) clicked, Then system displays "Link expired" and offers resend.
  - AC-4: Given registration with already-registered email, Then system displays "An account with this email already exists" without revealing verification status.
  - AC-5: Given password does not meet complexity (8+ chars, 1 uppercase, 1 number, 1 special char), Then inline validation displays specific missing criteria within 200ms.
- Edge Cases:
  - Rate limiting: max 3 resend requests per 5 minutes per email address (display resend cooldown in UI).
  - Disposable email domains: allow in Phase 1.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-003-registration.html |
| **Screen Spec** | figma_spec.md#SCR-003 |
| **UXR Requirements** | UXR-201, UXR-202, UXR-203, UXR-204, UXR-205, UXR-501 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement**

- **MUST** open and reference wireframe at `.propel/context/wireframes/Hi-Fi/wireframe-SCR-003-registration.html` during implementation
- **MUST** match layout, spacing, typography, and colors from the wireframe
- **MUST** implement all states: Default, Loading, Empty, Error, Validation (per figma_spec.md SCR-003)
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

Implement the patient registration page (SCR-003) as a React component using MUI 5. The page includes a registration form with fields for first name, last name, email, phone, date of birth, and password. It provides real-time inline validation (within 200ms per UXR-501), a password strength indicator, email uniqueness feedback on blur, terms & conditions checkbox, and screen states (Default, Validation, Loading, Success/Error). After successful submission the user sees a "check your email" success message. On email-exists error, a generic message is shown without revealing verification status. The page also handles the email verification link landing — displaying success redirect to login, expired-link message with resend option, and resend rate-limit feedback.

## Dependent Tasks

- US_005 — Authentication scaffold (provides routing, auth context, API client configuration)
- task_003_db_registration_schema (DB schema must exist before API calls succeed end-to-end, but FE can be developed in parallel using mocked API)

## Impacted Components

| Component | Project | Action |
|-----------|---------|--------|
| `RegistrationPage` | app (Frontend) | CREATE — new page component |
| `RegistrationForm` | app (Frontend) | CREATE — form with fields and validation |
| `PasswordStrengthIndicator` | app (Frontend) | CREATE — visual strength bar with criteria checklist |
| `EmailVerificationPage` | app (Frontend) | CREATE — handles verification link landing |
| `useRegistration` hook | app (Frontend) | CREATE — React Query mutation for registration API |
| `useEmailVerification` hook | app (Frontend) | CREATE — React Query mutation for verification/resend |
| `registrationValidation` | app (Frontend) | CREATE — validation schema (Yup/Zod) |
| App routing | app (Frontend) | MODIFY — add `/register` and `/verify-email` routes |

## Implementation Plan

1. **Create validation schema** — Define Zod or Yup schema enforcing: required first name, last name; email format; phone format; DOB (must be past date); password complexity (8+ chars, 1 uppercase, 1 number, 1 special char). Each rule maps to a specific inline error message.

2. **Build RegistrationForm component** — MUI TextField (5 fields), Button (2: submit + back-to-login), Checkbox (terms). Use `react-hook-form` with Zod/Yup resolver. Inline validation triggers on blur/change within 200ms (UXR-501). Two-column layout on desktop (first/last name row), single column on mobile (UXR-303).

3. **Build PasswordStrengthIndicator** — Linear progress bar (MUI LinearProgress) with 4-level strength (weak/fair/strong/excellent). Criteria checklist below: 8+ chars, uppercase, number, special char. Each criterion shows check/cross icon in real-time as user types. Use `warning.main` for medium, `success.main` for strong, `error.main` for weak per designsystem tokens.

4. **Build RegistrationPage** — Wraps RegistrationForm in centered card layout matching wireframe (max-width 520px, background neutral-100, card neutral-0 with shadow). Manages screen states: Default (form visible), Validation (inline errors shown), Loading (button disabled + spinner), Success (check-email message), Error (alert banner for duplicate email or server error).

5. **Implement email-on-blur uniqueness check** — Debounced API call (300ms) to `POST /api/auth/check-email` on email field blur. Shows helper text "Checking availability..." then clears or shows error. Must not reveal whether email is verified (AC-4).

6. **Build EmailVerificationPage** — Reads token from URL query param. Calls `POST /api/auth/verify-email` on mount. Shows: success message + redirect to login (AC-2), expired message + resend button (AC-3), or invalid token error. Resend button calls `POST /api/auth/resend-verification` with rate-limit feedback (max 3 per 5 min).

7. **Create React Query hooks** — `useRegistration` (POST /api/auth/register), `useVerifyEmail` (POST /api/auth/verify-email), `useResendVerification` (POST /api/auth/resend-verification), `useCheckEmail` (POST /api/auth/check-email). Configure error handling for 409 (duplicate), 429 (rate limit), 400 (validation).

8. **Add routes and navigation** — Add `/register` route rendering RegistrationPage, `/verify-email` route rendering EmailVerificationPage. Add "Create Account" link from login page to `/register`. Add "Sign in" link from registration to login.

## Current Project State

- Project structure placeholder — to be updated based on US_005 authentication scaffold completion.

```
app/                          # Frontend React application
├── src/
│   ├── pages/                # Page-level components
│   ├── components/           # Shared components
│   ├── hooks/                # Custom React hooks
│   ├── validation/           # Validation schemas
│   ├── services/             # API service layer
│   └── routes/               # Route definitions
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/pages/RegistrationPage.tsx | Registration page with form, states, and card layout |
| CREATE | app/src/components/RegistrationForm.tsx | Form component with 5 fields, validation, and submit |
| CREATE | app/src/components/PasswordStrengthIndicator.tsx | Password strength bar with criteria checklist |
| CREATE | app/src/pages/EmailVerificationPage.tsx | Verification link landing page with success/expired/resend |
| CREATE | app/src/hooks/useRegistration.ts | React Query mutation for registration API |
| CREATE | app/src/hooks/useEmailVerification.ts | React Query hooks for verify and resend APIs |
| CREATE | app/src/validation/registrationSchema.ts | Zod/Yup schema with password complexity rules |
| MODIFY | app/src/routes/index.tsx | Add /register and /verify-email routes |

## External References

- [React Hook Form v7 — MUI Integration](https://react-hook-form.com/get-started#IntegratingwithUIlibraries)
- [MUI 5 TextField API](https://mui.com/material-ui/api/text-field/)
- [MUI 5 LinearProgress](https://mui.com/material-ui/api/linear-progress/)
- [Zod — Schema Validation](https://zod.dev/)
- [React Query useMutation](https://tanstack.com/query/v4/docs/react/reference/useMutation)
- [WCAG 2.1 AA — Forms](https://www.w3.org/WAI/WCAG21/Understanding/input-purpose.html)

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
- [ ] Form submits valid data to registration API endpoint
- [ ] Inline validation fires within 200ms on blur/change (UXR-501)
- [ ] Password strength indicator updates in real-time
- [ ] Email uniqueness check fires on blur with debounce
- [ ] Duplicate email shows generic error without revealing verification status (AC-4)
- [ ] Loading state disables submit button and shows spinner
- [ ] Success state shows "check your email" message
- [ ] Email verification page handles valid token, expired token, and invalid token
- [ ] Resend verification respects rate limit (max 3 per 5 min) with UI feedback
- [ ] Keyboard navigation works for all interactive elements (UXR-202)
- [ ] ARIA labels present on all form inputs (UXR-203)
- [ ] Contrast ratio >= 4.5:1 for all text (UXR-204)
- [ ] Focus indicators visible on all interactive elements (UXR-205)

## Implementation Checklist

- [ ] Create `registrationSchema.ts` with Zod/Yup validation rules (password: 8+ chars, 1 uppercase, 1 number, 1 special char; email format; required fields)
- [ ] Build `PasswordStrengthIndicator` component with MUI LinearProgress and criteria checklist
- [ ] Build `RegistrationForm` component with react-hook-form + MUI TextFields, terms checkbox, submit button
- [ ] Build `RegistrationPage` with centered card layout, screen state management (Default/Loading/Success/Error)
- [ ] Implement email-on-blur uniqueness check with 300ms debounce
- [ ] Build `EmailVerificationPage` handling token verification, expired link, and resend flow
- [ ] Create React Query hooks (`useRegistration`, `useVerifyEmail`, `useResendVerification`, `useCheckEmail`)
- [ ] Add `/register` and `/verify-email` routes; wire navigation links from login page
- **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
