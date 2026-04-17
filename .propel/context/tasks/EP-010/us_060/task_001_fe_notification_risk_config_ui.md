# Task - TASK_001

## Requirement Reference

- User Story: us_060
- Story Location: .propel/context/tasks/EP-010/us_060/us_060.md
- Acceptance Criteria:
    - AC-1: Given the admin opens notification template configuration, When they select a template (booking confirmation, 24h reminder, 2h reminder), Then they can edit subject line, body text, and variable placeholders (patient name, date, time, provider).
    - AC-2: Given the admin edits a notification template, When they save changes, Then all future notifications use the updated template without affecting already-sent messages.
    - AC-3: Given the admin opens risk configuration, When they adjust the no-show risk threshold, Then the system recalculates risk scoring display using the new threshold values.
    - AC-4: Given the admin modifies scoring parameters, When they save changes, Then the system logs the parameter change with admin attribution and timestamp.
- Edge Cases:
    - Invalid variable placeholders: System validates template syntax on save and rejects templates with unrecognized variables. Display inline validation errors identifying the unrecognized variable.
    - Risk threshold changes on active appointments: Risk scores for existing appointments are recalculated in the next batch run (not retroactively applied immediately). Show informational banner explaining deferred recalculation.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | .propel/context/wireframes/Hi-Fi/wireframe-SCR-015-admin-dashboard.html |
| **Screen Spec** | figma_spec.md#SCR-015 |
| **UXR Requirements** | UXR-004, UXR-501 |
| **Design Tokens** | designsystem.md#colors, designsystem.md#typography, designsystem.md#spacing, designsystem.md#form-components |

> **Wireframe Status Legend:**
> - **AVAILABLE**: Local file exists at specified path

### **CRITICAL: Wireframe Implementation Requirement (UI Tasks Only)**

**Wireframe Status = AVAILABLE:**
- **MUST** open and reference the wireframe file during UI implementation
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
| Backend | N/A (consumed via API) | - |
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

Implement the Notification Template Editor and Risk Configuration UI panels as React components within the SCR-015 Admin Configuration Dashboard. The Notifications tab provides a template selector (booking confirmation, 24h reminder, 2h reminder) with an editable form for subject line, body text with variable placeholder insertion (`{{patient_name}}`, `{{date}}`, `{{time}}`, `{{provider}}`), and channel toggle (Email/SMS). The Risk Configuration tab provides slider/input controls for no-show risk threshold and scoring parameter weights, with a live preview of how the threshold change affects scoring display. Both panels include auto-save progress (UXR-004), inline validation within 200ms (UXR-501), and audit change confirmation before save.

## Dependent Tasks

- US_058 (EP-010) — Admin dashboard framework with tab navigation (SCR-015 shell, config tab structure)
- US_008 (EP-DATA) — NotificationLog entity and domain model definitions
- task_002_be_notification_risk_config_api (US_060) — Backend API endpoints must be available to consume

## Impacted Components

- **NEW** `app/src/pages/AdminDashboard/components/NotificationTemplateEditor.tsx` — Template selector + editable form for email/SMS templates
- **NEW** `app/src/pages/AdminDashboard/components/TemplatePreview.tsx` — Live preview panel rendering template with sample variable values
- **NEW** `app/src/pages/AdminDashboard/components/VariablePlaceholderToolbar.tsx` — Insertable variable placeholder buttons (patient_name, date, time, provider)
- **NEW** `app/src/pages/AdminDashboard/components/RiskConfigPanel.tsx` — No-show risk threshold slider and scoring parameter weight inputs
- **NEW** `app/src/pages/AdminDashboard/components/RiskScorePreview.tsx` — Live preview showing how threshold/param changes affect scoring display
- **NEW** `app/src/hooks/useNotificationTemplates.ts` — React Query hooks for notification template CRUD operations
- **NEW** `app/src/hooks/useRiskConfig.ts` — React Query hooks for risk configuration CRUD operations
- **NEW** `app/src/types/notificationConfig.ts` — TypeScript interfaces for notification template and risk config API payloads
- **MODIFY** `app/src/pages/AdminDashboard/AdminDashboard.tsx` — Integrate Notifications and Risk Configuration tabs into existing tab panel

## Implementation Plan

1. **Define TypeScript interfaces** for API payloads: `NotificationTemplate` (id, templateType, channel, subject, bodyText, variables[], updatedAt, updatedBy), `RiskConfig` (id, noShowThreshold, scoringParams: Record<string, number>, updatedAt, updatedBy), `TemplateVariable` (name, description, sampleValue).
2. **Create React Query hooks** (`useNotificationTemplates`) calling `GET /api/admin/config/notifications` for list, `GET /api/admin/config/notifications/:id` for single template, `PUT /api/admin/config/notifications/:id` for update. Include `onMutate` optimistic update and `onError` rollback.
3. **Create React Query hooks** (`useRiskConfig`) calling `GET /api/admin/config/risk` and `PUT /api/admin/config/risk` with the same optimistic update pattern.
4. **Build VariablePlaceholderToolbar component** — Row of MUI Chip buttons for each valid variable (`{{patient_name}}`, `{{date}}`, `{{time}}`, `{{provider}}`). Clicking inserts the placeholder at cursor position in the body text field. Tooltip shows sample value.
5. **Build NotificationTemplateEditor component** — MUI Select for template type (Booking Confirmation, 24h Reminder, 2h Reminder), channel toggle (Email/SMS using MUI ToggleButtonGroup), MUI TextField for subject line, MUI TextField (multiline) for body text with VariablePlaceholderToolbar above it. Inline validation on blur/change within 200ms (UXR-501): validate subject not empty, body not empty, all `{{...}}` placeholders match allowed variable list. Show MUI FormHelperText with error.main color for invalid placeholders.
6. **Build TemplatePreview component** — Read-only panel rendering the template body with sample variable values substituted. Updates live as admin edits. Use MUI Paper with neutral.50 background.
7. **Build RiskConfigPanel component** — MUI Slider for no-show risk threshold (0–100 range, step 5), with current value display. MUI TextField inputs for scoring parameter weights (e.g., prior no-shows weight, cancellation history weight, appointment lead time weight). Inline validation within 200ms (UXR-501): threshold must be 0–100, weights must be positive numbers summing to 1.0.
8. **Build RiskScorePreview component** — Display sample patient risk scores calculated with current parameter values. Show before/after comparison when values change. Use MUI Alert (info variant) banner explaining that existing appointments recalculate in next batch run.
9. **Integrate both panels into AdminDashboard tab structure** — Add "Notifications" and "Risk Configuration" as tab panels within the existing SCR-015 tab navigation. Wire auto-save on form progress (UXR-004) using debounced localStorage persistence. Add confirmation dialog (UXR-102) before saving changes that affect future notifications/scoring.
10. **Implement all 5 screen states** — Default (data loaded, forms populated), Loading (MUI Skeleton placeholders for form fields), Empty (no templates configured, show setup prompt), Error (MUI Alert with retry), Validation (inline field errors per UXR-501).

**Focus on how to implement:**
- Use MUI `<Skeleton>` for loading states matching UXR-502
- Debounce form auto-save to localStorage for UXR-004 (save draft every 2 seconds of inactivity)
- Use `contentEditable` or controlled textarea with cursor position tracking for variable insertion
- Validate `{{...}}` patterns with regex: `/\{\{(\w+)\}\}/g` — reject any variable not in allowed list
- Stack form fields vertically on mobile (375px), two-column on desktop (768px+) per UXR-303
- Admin screens use primary color accent (`#1976D2`) per design tokens
- Use MUI Slider with marks for threshold visualization

## Current Project State

```
[Placeholder — to be updated based on dependent task completion]
app/
├── src/
│   ├── pages/
│   │   └── AdminDashboard/          (FROM US_058)
│   │       ├── AdminDashboard.tsx
│   │       └── components/
│   │           ├── NotificationTemplateEditor.tsx  (NEW)
│   │           ├── TemplatePreview.tsx             (NEW)
│   │           ├── VariablePlaceholderToolbar.tsx   (NEW)
│   │           ├── RiskConfigPanel.tsx             (NEW)
│   │           └── RiskScorePreview.tsx            (NEW)
│   ├── hooks/
│   │   ├── useNotificationTemplates.ts             (NEW)
│   │   └── useRiskConfig.ts                        (NEW)
│   ├── types/
│   │   └── notificationConfig.ts                   (NEW)
│   └── routes/
│       └── index.tsx
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/pages/AdminDashboard/components/NotificationTemplateEditor.tsx | Template selector, subject/body editor, channel toggle, variable toolbar, inline validation |
| CREATE | app/src/pages/AdminDashboard/components/TemplatePreview.tsx | Live preview rendering template with sample variable substitution |
| CREATE | app/src/pages/AdminDashboard/components/VariablePlaceholderToolbar.tsx | Clickable variable placeholder chips for insertion into template body |
| CREATE | app/src/pages/AdminDashboard/components/RiskConfigPanel.tsx | No-show risk threshold slider, scoring parameter weight inputs, inline validation |
| CREATE | app/src/pages/AdminDashboard/components/RiskScorePreview.tsx | Live before/after risk score preview with batch recalculation info banner |
| CREATE | app/src/hooks/useNotificationTemplates.ts | React Query hooks for GET/PUT notification template API calls |
| CREATE | app/src/hooks/useRiskConfig.ts | React Query hooks for GET/PUT risk configuration API calls |
| CREATE | app/src/types/notificationConfig.ts | TypeScript interfaces: NotificationTemplate, RiskConfig, TemplateVariable |
| MODIFY | app/src/pages/AdminDashboard/AdminDashboard.tsx | Add Notifications and Risk Configuration tab panels to existing tab structure |

## External References

- [React Query v4 — Mutations](https://tanstack.com/query/v4/docs/react/guides/mutations)
- [React Query v4 — Optimistic Updates](https://tanstack.com/query/v4/docs/react/guides/optimistic-updates)
- [MUI 5 — Tabs component](https://mui.com/material-ui/react-tabs/)
- [MUI 5 — TextField component](https://mui.com/material-ui/react-text-field/)
- [MUI 5 — Slider component](https://mui.com/material-ui/react-slider/)
- [MUI 5 — Chip component](https://mui.com/material-ui/react-chip/)
- [MUI 5 — ToggleButtonGroup](https://mui.com/material-ui/react-toggle-button/)
- [MUI 5 — Alert component](https://mui.com/material-ui/react-alert/)
- [MUI 5 — Skeleton component](https://mui.com/material-ui/react-skeleton/)

## Build Commands

- `cd app && npm install` — Install dependencies
- `cd app && npm run build` — Build production bundle
- `cd app && npm run dev` — Start development server

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] **[UI Tasks]** Visual comparison against wireframe completed at 375px, 768px, 1440px
- [ ] **[UI Tasks]** Run `/analyze-ux` to validate wireframe alignment
- [ ] All 5 screen states render correctly (Default, Loading, Empty, Error, Validation)
- [ ] Template selector loads all 3 template types (booking confirmation, 24h reminder, 2h reminder)
- [ ] Subject line and body text fields editable with inline validation within 200ms (UXR-501)
- [ ] Variable placeholder insertion works at cursor position in body text
- [ ] Invalid `{{...}}` placeholders rejected with specific error message on save
- [ ] Channel toggle switches between Email and SMS views
- [ ] Template preview updates live with sample variable values
- [ ] Risk threshold slider adjusts 0–100 with step 5 and live value display
- [ ] Scoring parameter weights validated (positive numbers, sum to 1.0)
- [ ] Risk score preview shows before/after comparison
- [ ] Info banner displayed explaining batch recalculation for existing appointments
- [ ] Auto-save form progress persists to localStorage (UXR-004)
- [ ] Confirmation dialog shown before saving changes (UXR-102)
- [ ] Responsive layout: vertical stacking at 375px, two-column at 768px+ (UXR-303)
- [ ] WCAG 2.1 AA: keyboard navigation, ARIA labels on form fields, slider, tabs (NFR-046)

## Implementation Checklist

- [ ] Create `notificationConfig.ts` TypeScript interfaces (`NotificationTemplate`, `RiskConfig`, `TemplateVariable`, `ScoringParameter`)
- [ ] Create `useNotificationTemplates.ts` React Query hooks — list, get, update with optimistic updates
- [ ] Create `useRiskConfig.ts` React Query hooks — get, update with optimistic updates
- [ ] Implement `VariablePlaceholderToolbar.tsx` — MUI Chip buttons for `{{patient_name}}`, `{{date}}`, `{{time}}`, `{{provider}}` with cursor insertion
- [ ] Implement `NotificationTemplateEditor.tsx` — Template selector, channel toggle, subject/body fields, inline validation (200ms), variable toolbar
- [ ] Implement `TemplatePreview.tsx` — Live preview with sample variable substitution
- [ ] Implement `RiskConfigPanel.tsx` — Threshold slider (0–100), scoring parameter weight inputs, inline validation (200ms)
- [ ] Implement `RiskScorePreview.tsx` — Before/after risk score display with batch recalculation info banner
- [ ] Integrate Notifications and Risk Configuration tabs into AdminDashboard.tsx tab panel
- [ ] Implement Loading state with MUI Skeleton placeholders for all form fields
- [ ] Implement Empty state (no templates configured, setup prompt)
- [ ] Implement Error state (MUI Alert with retry action)
- [ ] **[UI Tasks - MANDATORY]** Reference wireframe from Design References table during implementation
- [ ] **[UI Tasks - MANDATORY]** Validate UI matches wireframe before marking task complete
