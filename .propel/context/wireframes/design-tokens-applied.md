# Design Tokens Applied — Wireframe Audit

## Overview

This document maps every design token from `designsystem.md` to its application across the 16 high-fidelity HTML wireframes and the shared CSS token file (`shared-tokens.css`).

---

## 1. Color Palette Application

### Primary Colors (#1976D2 family)

| Token | CSS Variable | Applied In |
|---|---|---|
| primary-50 `#E3F2FD` | `--primary-50` | SCR-001 brand panel, SCR-005 sidebar active bg, SCR-006 selected slot bg, SCR-013 patient avatar bg |
| primary-100 `#BBDEFB` | `--primary-100` | SCR-013 profile avatar background |
| primary-500 `#1976D2` | `--primary-500` | All patient-portal buttons, sidebar active border (SCR-005–009), links, progress bars, calendar dots |
| primary-600 `#1565C0` | `--primary-600` | Button hover state |
| primary-700 `#0D47A1` | `--primary-700` | Button active/pressed state, sidebar logo text, SCR-013 avatar text |
| primary-contrast `#FFFFFF` | `--primary-contrast` | Button text on primary bg |

### Secondary Colors (#7B1FA2 family)

| Token | CSS Variable | Applied In |
|---|---|---|
| secondary-50 `#F3E5F5` | `--secondary-50` | Staff sidebar active bg (SCR-010–014, SCR-016), staff avatar bg |
| secondary-500 `#7B1FA2` | `--secondary-500` | Staff sidebar active border, staff portal accent buttons, SCR-014 override button |
| secondary-700 `#4A148C` | `--secondary-700` | Staff avatar text color |

### Semantic Colors

| Token | CSS Variable | Applied In |
|---|---|---|
| success-main `#2E7D32` | `--success-500` | Verified badges, Active badges, Arrived status, confidence-high, agreement meter fill |
| success-surface `#E8F5E9` | `--success-surface` | Alert success bg, SCR-006 available slot bg |
| warning-main `#ED6C02` | `--warning-500` | Waiting status badge, Pending badges, conflict icon, confidence-medium |
| warning-surface `#FFF3E0` | `--warning-surface` | Alert warning bg, SCR-013 conflict row bg |
| error-main `#D32F2F` | `--error-500` | Error badges, No-Show status, Urgent badge, timer-alert, confidence-low |
| error-surface `#FFEBEE` | `--error-surface` | Alert error bg, SCR-011 long-wait row bg, SCR-015 admin sidebar active bg |
| info-main `#0288D1` | `--info-500` | Info alerts, Scheduled status, auto-save indicator |
| info-surface `#E1F5FE` | `--info-surface` | Alert info bg |

### Neutral Colors

| Token | CSS Variable | Applied In |
|---|---|---|
| neutral-0 `#FFFFFF` | `--neutral-0` | Card bg, modal bg, main bg, button text |
| neutral-50 `#FAFAFA` | `--neutral-50` | Page background, table header bg |
| neutral-100 `#F5F5F5` | `--neutral-100` | Sidebar bg, blocked slot bg (SCR-015) |
| neutral-200 `#EEEEEE` | `--neutral-200` | Borders, dividers, table borders, progress track, file row borders |
| neutral-300 `#E0E0E0` | `--neutral-300` | Input borders, dropdown borders |
| neutral-400 `#BDBDBD` | `--neutral-400` | Placeholder text, disabled text, blocked slot text |
| neutral-500 `#9E9E9E` | `--neutral-500` | Caption text, file-size text |
| neutral-600 `#757575` | `--neutral-600` | Secondary labels, meta text, result-count, queue-count |
| neutral-700 `#616161` | `--neutral-700` | Body text |
| neutral-800 `#424242` | `--neutral-800` | Headings, strong text |
| neutral-900 `#212121` | `--neutral-900` | Primary text, h1/h2 |

### Appointment Status Colors

| Token | Applied In |
|---|---|
| scheduled `#1976D2` | SCR-007 badge-default, SCR-011 Scheduled badge |
| confirmed `#0288D1` | SCR-007 Confirmed status |
| arrived `#2E7D32` | SCR-011 Arrived badge (badge-success) |
| in-visit `#7B1FA2` | SCR-011 In Visit action |
| completed `#388E3C` | SCR-007 Completed badge |
| cancelled `#757575` | SCR-007 Cancelled badge |
| no-show `#D32F2F` | SCR-007 No-Show badge, SCR-011 No-Show badge |
| waitlisted `#ED6C02` | SCR-007 Waitlisted badge |

### AI Confidence Colors

| Token | CSS Class | Applied In |
|---|---|---|
| high `#2E7D32` (>=80%) | `.confidence-high` | SCR-013 medications/diagnoses/procedures, SCR-014 ICD/CPT tables |
| medium `#ED6C02` (60-79%) | `.confidence-medium` | SCR-013 pending items, SCR-014 medium-confidence codes |
| low `#D32F2F` (<60%) | `.confidence-low` | SCR-013 conflict medication, SCR-014 overridden codes |

---

## 2. Typography Application

| Token | CSS Variable/Class | Applied In |
|---|---|---|
| h1 (34px/300) | `.h1` | Not used (h2 preferred for page titles) |
| h2 (24px/400) | `.h2` | All page titles (SCR-001–016) |
| h3 (20px/500) | `.h3` | Header bar title, card section titles |
| h4 (18px/500) | `.h4` | Sub-headers in cards, modal titles, tab panel headers |
| h5 (implicit) | `.h5` | SCR-015 sub-section headers (Hours, Holidays) |
| subtitle1 (16px/500) | Table headers | All table `<th>` elements |
| subtitle2 (14px/500) | Form labels | `.form-label` across all form screens |
| body1 (16px/400) | Default | Primary body text in all screens |
| body2 (14px/400) | Table cells | All table `<td>`, secondary text |
| caption (12px/400) | `.caption` | Helper text, timestamps, file sizes, pagination info |
| button (14px/500/uppercase) | `.btn` | All button labels globally |
| font-family: Roboto | `--ff-primary` | Applied to `body` via shared-tokens.css |
| font-family: Roboto Mono | `--ff-mono` | Timer component text |

---

## 3. Spacing Application

| Token | CSS Variable | Applied In |
|---|---|---|
| sp-1 (4px) | `--sp-1` | Progress track margin, tight element gaps |
| sp-2 (8px) | `--sp-2` | Table cell padding, compact form groups, badge padding |
| sp-3 (12px) | `--sp-3` | Button padding, input padding, card internal gaps, filter gaps |
| sp-4 (16px) | `--sp-4` | Card padding, form field gap, section margins, stat card padding |
| sp-5 (20px) | `--sp-5` | Sidebar padding-top |
| sp-6 (24px) | `--sp-6` | Section gaps, page margin-desktop, upload area gap, grid gap |
| sp-8 (32px) | `--sp-8` | Page content padding, empty state padding |
| sp-10 (40px) | `--sp-10` | Large section spacing |
| sidebar-width (256px) | `--sidebar-w` | All sidebar widths (SCR-005–016) |
| header-height (64px) | `--header-h` | All header heights |

---

## 4. Border Radius Application

| Token | CSS Variable | Applied In |
|---|---|---|
| rad-sm (4px) | `--rad-sm` | Buttons, inputs, progress bars, slot cells |
| rad-md (8px) | `--rad-md` | Cards, toasts, modals inner elements, holiday cards, slot cells |
| rad-lg (12px) | `--rad-lg` | Modals, large containers |
| rad-xl (16px) | `--rad-xl` | Chips, chat bubbles |
| rad-full (9999px) | `--rad-full` | Avatars, badges, toggles |

---

## 5. Elevation Application

| Token | CSS Variable | Applied In |
|---|---|---|
| elevation-1 | `--shadow-1` | Cards (all screens), dropdowns |
| elevation-2 | `--shadow-2` | Card hover state, sidebar |
| elevation-3 | `--shadow-3` | Modals (SCR-006 confirmation, SCR-014 justification), dropzone hover |
| elevation-4 | `--shadow-4` | Toast notifications |

---

## 6. Component-Level Token Mapping

### Buttons

| Variant | Token(s) | Screens |
|---|---|---|
| Primary | bg: primary-500, text: primary-contrast, hover: primary-600 | All 16 screens |
| Secondary | border: neutral-300, text: neutral-700, hover-bg: neutral-50 | All 16 screens |
| Error | bg: error-500, text: #FFF | SCR-011 (urgent actions) |
| Small (btn-sm) | padding: sp-1/sp-3, font: 0.8125rem | SCR-010–016 header actions, table row actions |

### Form Controls

| Element | Token(s) | Screens |
|---|---|---|
| Input | border: neutral-300, radius: rad-sm, padding: sp-3, focus-border: primary-500 | SCR-001, 003, 004, 009, 012, 015, 016 |
| Select | Same as input + chevron icon | SCR-006, 011, 015, 016 |
| Textarea | Same as input, resize: vertical | SCR-009, 012, 014 |
| Checkbox/Toggle | checked: primary-500, track: neutral-300 | SCR-001, 003, 015 |

### Tables

| Element | Token(s) | Screens |
|---|---|---|
| Header row | bg: neutral-50, text: neutral-800, weight: 500 | SCR-007, 010, 011, 013, 014, 016 |
| Body row | border-bottom: neutral-200, padding: sp-2/sp-3 | Same as above |
| Row hover | bg: neutral-50 | Same as above |
| Sortable header | cursor: pointer, underline indicator | SCR-007, 016 |

### Badges

| Variant | Token(s) | Screens |
|---|---|---|
| badge-success | bg: success-surface, text: success-dark | SCR-007, 010, 011, 013, 014, 015, 016 |
| badge-warning | bg: warning-surface, text: warning-dark | SCR-007, 011, 013, 014, 015 |
| badge-error | bg: error-surface, text: error-dark | SCR-007, 011, 013, 014, 015 |
| badge-default | bg: neutral-100, text: neutral-700 | SCR-007, 011, 012, 013, 016 |

### Specialized Components

| Component | Token(s) | Screens |
|---|---|---|
| ChatBubble (AI) | bg: neutral-100, radius: rad-xl | SCR-008 |
| ChatBubble (User) | bg: primary-50, radius: rad-xl | SCR-008 |
| TypingIndicator | dot-color: neutral-400, animation: pulse | SCR-008 |
| ConfidenceBadge | high: success, medium: warning, low: error | SCR-013, SCR-014 |
| Timer | normal: neutral-700, alert: error-500, font: monospace | SCR-011 |
| DropZone | border: neutral-300 dashed, hover: primary-500 dashed, bg: primary-50 | SCR-012 |
| ProgressBar | track: neutral-200, fill: primary-500 | SCR-008, SCR-012 |
| SlotCell | available: success-surface, blocked: neutral-100 | SCR-006, SCR-015 |

---

## 7. Responsive Breakpoints Applied

| Breakpoint | CSS Media Query | Behavior |
|---|---|---|
| Mobile (≤768px) | `max-width: 768px` | Sidebar collapses, single-column layout, cards stack |
| Tablet (≤1024px) | `max-width: 1024px` | Sidebar narrows (icon-only), 2-column grid |
| Desktop (≥1025px) | Default | Full sidebar, multi-column layout, 1440px primary viewport |

---

## 8. Screen-Token Summary Matrix

| Screen | Primary | Secondary | Semantic | Neutral | Typography | Spacing | Radius | Elevation |
|---|---|---|---|---|---|---|---|---|
| SCR-001 Login | Yes | — | Error | Yes | h2, body1, button | sp-3–sp-8 | sm, md, full | 1, 2 |
| SCR-002 Router | Yes | Yes | — | Yes | h2, h3 | sp-4–sp-6 | md | 1 |
| SCR-003 Register | Yes | — | Error, Success | Yes | h2, body1, caption | sp-3–sp-6 | sm, md | 1 |
| SCR-004 Reset | Yes | — | Info | Yes | h2, body1 | sp-4–sp-8 | sm, md | 1 |
| SCR-005 Patient Dash | Yes | — | Success, Warning, Info | Yes | h2, h3, body2, caption | sp-2–sp-6 | sm, md, full | 1, 2 |
| SCR-006 Booking | Yes | — | Success, Warning | Yes | h2, h3, body2 | sp-2–sp-6 | sm, md, lg | 1, 3 |
| SCR-007 History | Yes | — | All 4 semantic | Yes | h2, body2, caption | sp-2–sp-4 | sm, md, full | 1 |
| SCR-008 AI Intake | Yes | — | Info, Warning | Yes | h2, body1, caption | sp-2–sp-6 | sm, md, xl | 1, 2 |
| SCR-009 Manual Intake | Yes | — | Info, Success | Yes | h2, h3, body1 | sp-3–sp-6 | sm, md | 1 |
| SCR-010 Staff Dash | Yes | Yes | Success, Warning | Yes | h2, h3, body2, caption | sp-2–sp-6 | sm, md, full | 1, 2 |
| SCR-011 Queue | Yes | Yes | Error, Warning, Success | Yes | h2, body2, caption | sp-2–sp-4 | sm, md, full | 1 |
| SCR-012 Upload | Yes | Yes | Info, Error, Success, Warning | Yes | h2, h3, body2, caption | sp-2–sp-6 | sm, md | 1, 3 |
| SCR-013 Profile 360 | Yes | Yes | All 4 semantic | Yes | h2, h4, body2, caption | sp-2–sp-4 | sm, md, full | 1 |
| SCR-014 Coding | Yes | Yes | All 4 semantic | Yes | h2, h4, body2, caption | sp-2–sp-6 | sm, md, lg, full | 1, 3 |
| SCR-015 Admin Dash | — | — | Error, Success, Warning | Yes | h2, h3, h4, h5, body2 | sp-1–sp-6 | sm, md, full | 1 |
| SCR-016 Search | Yes | Yes | Success | Yes | h2, body2, caption | sp-2–sp-6 | sm, md, full | 1 |

---

## 9. Token Coverage Summary

| Category | Total Tokens | Applied | Coverage |
|---|---|---|---|
| Primary Colors | 10 shades + contrast | 6 used | 60% (unused mid-tones on standby) |
| Secondary Colors | 10 shades + contrast | 3 used | 30% (staff-specific accent) |
| Semantic Colors | 4 × 5 variants (20) | 12 used | 60% (surface + main most frequent) |
| Neutral Colors | 11 steps | 11 used | 100% |
| Typography | 11 scale entries | 10 used | 91% (overline available but not prominent) |
| Spacing | 12 values + 6 component | 10 + 3 used | 72% |
| Border Radius | 6 values | 5 used | 83% (none not used) |
| Elevation | 5 levels | 4 used | 80% |
| Appointment Status | 8 colors | 8 used | 100% |
| Confidence | 3 tiers | 3 used | 100% |

**Overall Token Utilization**: All token categories are represented across the wireframes. Unused individual shades (e.g., primary-200, secondary-300) remain available for interactive states and micro-interactions in implementation.
