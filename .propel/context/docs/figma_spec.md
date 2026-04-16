# Figma Design Specification - Unified Patient Access & Clinical Intelligence Platform

## 1. Figma Specification

**Platform**: Responsive Web (320px - 2560px)

---

## 2. Source References

### Primary Source

| Document | Path | Purpose |
|----------|------|---------|
| Requirements | `.propel/context/docs/spec.md` | Personas, use cases, FR-XXX |
| Architecture | `.propel/context/docs/design.md` | NFR, TR, DR, AIR requirements |
| Epics | `.propel/context/docs/epics.md` | Epic decomposition with UI impact flags |
| Models | `.propel/context/docs/models.md` | Sequence diagrams for interaction flows |

### Related Documents

| Document | Path | Purpose |
|----------|------|---------|
| Design System | `.propel/context/docs/designsystem.md` | Tokens, branding, component specifications |

---

## 3. UX Requirements

### UXR Requirements Table

| UXR-ID | Category | Requirement | Acceptance Criteria | Screens Affected |
|--------|----------|-------------|---------------------|------------------|
| UXR-001 | Usability | System MUST provide navigation to any feature in max 3 clicks from the landing page | Click-count audit passes for all primary flows | All screens |
| UXR-002 | Usability | System MUST display clear visual hierarchy with primary actions prominently placed | Primary CTA is the most visually dominant element on every screen | All screens |
| UXR-003 | Usability | System MUST provide contextual breadcrumb navigation for nested workflows | Breadcrumb visible on all screens deeper than level 2 | SCR-006, SCR-007, SCR-008, SCR-010, SCR-011, SCR-013, SCR-014, SCR-015 |
| UXR-004 | Usability | System MUST auto-save form progress on intake and configuration screens | Data persists after accidental navigation or session timeout recovery | SCR-008, SCR-009, SCR-015 |
| UXR-005 | Usability | System MUST provide single-click patient search from staff dashboard | Search bar always visible in staff header | SCR-010, SCR-011, SCR-016 |
| UXR-101 | Usability | System MUST display appointment slot availability with visual calendar view | Patients can scan available slots without scrolling excessively | SCR-005, SCR-006 |
| UXR-102 | Usability | System MUST confirm destructive actions (cancel, delete) with confirmation dialog | Confirmation modal appears before any data loss action | SCR-006, SCR-010, SCR-015 |
| UXR-103 | Usability | System MUST display queue position and estimated wait time to staff in real-time | Queue dashboard refreshes within 5 seconds of changes | SCR-011 |
| UXR-104 | Usability | System MUST provide side-by-side comparison view for clinical data conflicts | Staff can view conflicting data from two sources simultaneously | SCR-013 |
| UXR-105 | Usability | System MUST display AI confidence scores with visual indicators (color-coded) | Scores below 80% show amber/red indicator | SCR-012, SCR-013, SCR-014 |
| UXR-201 | Accessibility | System MUST comply with WCAG 2.1 Level AA standards for all patient-facing interfaces | WAVE/axe audit passes with zero critical violations | All screens |
| UXR-202 | Accessibility | System MUST support keyboard navigation for all interactive elements | Tab order is logical; all actions reachable via keyboard | All screens |
| UXR-203 | Accessibility | System MUST provide ARIA labels for all form inputs, buttons, and interactive elements | Screen reader announces element purpose correctly | All screens |
| UXR-204 | Accessibility | System MUST maintain minimum 4.5:1 contrast ratio for normal text and 3:1 for large text | Automated contrast check passes for all text elements | All screens |
| UXR-205 | Accessibility | System MUST provide visible focus indicators on all interactive elements | Focus ring visible on keyboard navigation | All screens |
| UXR-206 | Accessibility | System MUST support screen reader announcements for dynamic content updates | Live regions announce queue changes, notifications, and AI responses | SCR-008, SCR-011, SCR-012 |
| UXR-301 | Responsiveness | System MUST adapt layout to mobile (320px), tablet (768px), and desktop (1024px+) viewports | All screens render correctly at each breakpoint | All screens |
| UXR-302 | Responsiveness | System MUST collapse sidebar navigation to hamburger menu on mobile viewports | Sidebar hidden on <768px; hamburger icon shown | All screens with sidebar |
| UXR-303 | Responsiveness | System MUST stack form fields vertically on mobile and allow 2-column on desktop | Form layouts adapt based on viewport width | SCR-003, SCR-008, SCR-009, SCR-015 |
| UXR-304 | Responsiveness | System MUST provide touch-friendly tap targets (minimum 44x44px) on mobile | All interactive elements meet minimum size | All screens (mobile) |
| UXR-401 | Visual Design | System MUST use consistent color coding for appointment statuses across all views | Scheduled=Blue, Completed=Green, Cancelled=Gray, No-show=Red | SCR-005, SCR-006, SCR-010, SCR-011 |
| UXR-402 | Visual Design | System MUST use consistent iconography from Material Icons set | All icons from MUI icon library | All screens |
| UXR-403 | Visual Design | System MUST display patient-facing and staff-facing interfaces with distinct visual treatment | Staff screens use secondary color accent; patient screens use primary | All screens |
| UXR-404 | Visual Design | System MUST display clinical data categories with distinct visual indicators | Each document type (lab, prescription, note, imaging) has unique icon/color | SCR-012, SCR-013 |
| UXR-501 | Interaction | System MUST provide inline validation feedback within 200ms of user input | Field validation triggers on blur/change with <200ms response | SCR-003, SCR-004, SCR-008, SCR-009, SCR-015 |
| UXR-502 | Interaction | System MUST display skeleton loading screens during data fetches | Skeleton placeholders shown for >300ms load times | All data-driven screens |
| UXR-503 | Interaction | System MUST provide optimistic UI updates for booking confirmation | Slot visually reserved immediately, rolled back on conflict | SCR-006 |
| UXR-504 | Interaction | System MUST animate AI conversational intake responses with typing indicator | Typing dots shown while AI generates response | SCR-008 |
| UXR-505 | Interaction | System MUST provide toast notifications for background operations (upload, parsing) | Toast appears on document upload completion or parsing status change | SCR-012, SCR-013 |
| UXR-506 | Interaction | System MUST support drag-and-drop file upload for clinical documents | Drop zone highlighted on drag-over | SCR-012 |
| UXR-601 | Error Handling | System MUST display actionable error messages with recovery instructions | Error messages include specific guidance and retry button | All screens |
| UXR-602 | Error Handling | System MUST display booking conflict errors with alternative slot suggestions | On 409 conflict, show next 3 available slots | SCR-006 |
| UXR-603 | Error Handling | System MUST display session timeout warning 2 minutes before expiry | Modal countdown with "Extend Session" button | All authenticated screens |
| UXR-604 | Error Handling | System MUST show offline/disconnected state with retry mechanism | Banner shows connectivity status with auto-retry | All screens |
| UXR-605 | Error Handling | System MUST display AI service unavailability with manual workflow fallback option | Banner with "AI unavailable — switch to manual" button | SCR-008, SCR-012, SCR-014 |
| UXR-606 | Error Handling | System MUST display document upload errors with specific format guidance | Error lists supported formats and size limits | SCR-012 |

---

## 4. Personas Summary

| Persona | Role | Primary Goals | Key Screens |
|---------|------|---------------|-------------|
| Patient | End user | Book appointments, complete intake, receive reminders, manage health info | SCR-001, SCR-002, SCR-003, SCR-004, SCR-005, SCR-006, SCR-007, SCR-008, SCR-009 |
| Staff | Administrative/clinical | Manage walk-ins, mark arrivals, upload documents, verify data, resolve conflicts, review codes | SCR-001, SCR-002, SCR-010, SCR-011, SCR-012, SCR-013, SCR-014, SCR-016 |
| Admin | System administrator | Configure settings, manage users, maintain templates, monitor system health | SCR-001, SCR-002, SCR-015, SCR-016 |

---

## 5. Information Architecture

### Site Map

```text
Unified Patient Access Platform
+-- Public
|   +-- Login (SCR-001)
|   +-- Registration (SCR-003)
|   +-- Password Reset (SCR-004)
+-- Patient Portal
|   +-- Patient Dashboard (SCR-005)
|   +-- Appointment Booking (SCR-006)
|   +-- Appointment History (SCR-007)
|   +-- Patient Intake (SCR-008)
|   +-- Patient Intake - Manual Form (SCR-009)
+-- Staff Portal
|   +-- Staff Dashboard (SCR-010)
|   +-- Arrival Queue (SCR-011)
|   +-- Document Upload & Parsing (SCR-012)
|   +-- Patient Profile 360 (SCR-013)
|   +-- Medical Coding Review (SCR-014)
|   +-- Patient Search (SCR-016)
+-- Admin Portal
|   +-- Admin Dashboard (SCR-015)
|   +-- System Configuration (part of SCR-015)
|   +-- User Management (part of SCR-015)
+-- Shared
    +-- Role-Based Home Router (SCR-002)
```

### Navigation Patterns

| Pattern | Type | Platform Behavior |
|---------|------|-------------------|
| Primary Nav | Sidebar | Desktop: Persistent left sidebar / Mobile: Hamburger menu overlay |
| Secondary Nav | Tabs | Used within dashboard screens for sub-sections |
| Utility Nav | User menu | Top-right: Avatar dropdown (Profile, Settings, Logout) |
| Breadcrumb | Breadcrumb | Shown on nested screens (depth > 2) |

---

## 6. Screen Inventory

### Screen List

| Screen ID | Screen Name | Derived From | Personas Covered | Epic | Priority | States Required |
|-----------|-------------|--------------|------------------|------|----------|-----------------|
| SCR-001 | Login | UC-001, UC-003, UC-008, UC-009 | All | EP-001 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-002 | Role-Based Dashboard Router | UC-001, UC-003, UC-009 | All | EP-001 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-003 | Patient Registration | UC-001 | Patient | EP-001 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-004 | Password Reset | UC-001 | All | EP-001 | P1 | Default, Loading, Empty, Error, Validation |
| SCR-005 | Patient Dashboard | UC-001, UC-002, UC-004 | Patient | EP-002 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-006 | Appointment Booking | UC-001, UC-010 | Patient, Staff | EP-002 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-007 | Appointment History | UC-001, UC-010 | Patient | EP-003 | P1 | Default, Loading, Empty, Error, Validation |
| SCR-008 | AI Conversational Intake | UC-002 | Patient | EP-004 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-009 | Manual Intake Form | UC-002 | Patient | EP-004 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-010 | Staff Dashboard | UC-003, UC-008 | Staff | EP-010 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-011 | Arrival Queue Dashboard | UC-008, UC-003 | Staff | EP-009 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-012 | Document Upload & Parsing | UC-005, UC-006 | Staff | EP-006 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-013 | Patient Profile 360 | UC-006, UC-005 | Staff | EP-007 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-014 | Medical Coding Review | UC-007 | Staff | EP-008 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-015 | Admin Configuration Dashboard | UC-009 | Admin | EP-010 | P0 | Default, Loading, Empty, Error, Validation |
| SCR-016 | Patient Search Results | UC-003, UC-008 | Staff, Admin | EP-010 | P1 | Default, Loading, Empty, Error, Validation |

### Priority Legend

- **P0**: Critical path (must-have for MVP)
- **P1**: Core functionality (high priority)
- **P2**: Important features (medium priority)

### Screen-to-Persona Coverage Matrix

| Screen | Patient | Staff | Admin | Notes |
|--------|---------|-------|-------|-------|
| SCR-001 Login | Primary | Primary | Primary | Shared entry point |
| SCR-002 Dashboard Router | Primary | Primary | Primary | Routes to role-specific dashboard |
| SCR-003 Registration | Primary | - | - | Patient self-registration |
| SCR-004 Password Reset | Primary | Primary | Primary | Shared utility |
| SCR-005 Patient Dashboard | Primary | - | - | Appointments, intake, history |
| SCR-006 Appointment Booking | Primary | Secondary | - | Staff books walk-ins |
| SCR-007 Appointment History | Primary | - | - | Patient-only view |
| SCR-008 AI Intake | Primary | - | - | Patient conversational flow |
| SCR-009 Manual Intake | Primary | - | - | Fallback for AI intake |
| SCR-010 Staff Dashboard | - | Primary | - | Daily schedule + queue + tasks |
| SCR-011 Arrival Queue | - | Primary | - | Real-time queue management |
| SCR-012 Document Upload | - | Primary | - | Upload and parsing status |
| SCR-013 Patient Profile 360 | - | Primary | - | Consolidated clinical view |
| SCR-014 Medical Coding | - | Primary | - | Code review and approval |
| SCR-015 Admin Dashboard | - | - | Primary | Configuration and monitoring |
| SCR-016 Patient Search | - | Primary | Primary | Shared search functionality |

### Modal/Overlay Inventory

| Name | Type | Trigger | Parent Screen(s) | Priority |
|------|------|---------|-----------------|----------|
| Booking Confirmation | Modal | Confirm appointment | SCR-006 | P0 |
| Cancel Appointment | Dialog | Click cancel on appointment | SCR-005, SCR-006, SCR-007 | P0 |
| Session Timeout Warning | Modal | 2 min before 15-min timeout | All authenticated | P0 |
| Slot Conflict Error | Modal | Concurrent booking detected | SCR-006 | P0 |
| Document Upload Progress | Drawer | Upload initiated | SCR-012 | P0 |
| Conflict Resolution | Modal | Click flagged conflict | SCR-013 | P0 |
| Code Override Justification | Dialog | Staff overrides AI code | SCR-014 | P0 |
| Walk-in Registration | Modal | Click Walk-in from staff dashboard | SCR-010 | P0 |
| Delete User Confirmation | Dialog | Admin deactivates account | SCR-015 | P1 |
| MFA Setup | Modal | Staff/Admin enables MFA | SCR-001 | P1 |
| Notification Preferences | Drawer | Patient edits SMS opt-out | SCR-005 | P1 |
| PDF Confirmation Preview | Modal | View booking PDF/QR | SCR-005, SCR-006 | P1 |

---

## 7. Content & Tone

### Voice & Tone

- **Overall Tone**: Professional, empathetic, and trustworthy (healthcare context)
- **Error Messages**: Helpful, non-blaming, actionable (e.g., "That slot was just booked. Here are 3 similar options.")
- **Empty States**: Encouraging with clear CTA (e.g., "No appointments yet. Book your first appointment.")
- **Success Messages**: Brief and reassuring (e.g., "Appointment confirmed! Check your email for details.")
- **AI Responses**: Conversational, clear, with medical terminology explained in plain language

### Content Guidelines

- **Headings**: Sentence case
- **CTAs**: Action-oriented verbs ("Book Appointment", "Upload Document", "Approve Code")
- **Labels**: Concise, descriptive, no abbreviations in patient-facing screens
- **Placeholder Text**: Helpful examples ("e.g., john@email.com", "MM/DD/YYYY")
- **Date Format**: MM/DD/YYYY for display, ISO 8601 for API
- **Time Format**: 12-hour with AM/PM for patient-facing, 24-hour for staff dashboards

---

## 8. Data & Edge Cases

### Data Scenarios

| Scenario | Description | Handling |
|----------|-------------|----------|
| No Data | Patient has no appointments or intake | Empty state with primary CTA |
| First Use | New patient, no history | Onboarding flow with intake prompt |
| Large Data | Patient with 100+ appointments | Pagination (20 per page) with filters |
| Slow Connection | >3s load time | Skeleton loading screens |
| Offline | No network connectivity | Offline banner with retry on reconnect |
| Long Patient Name | 50+ character names | Truncate with ellipsis, full name on hover |
| Multiple Documents | 20+ uploaded clinical documents | Categorized list with pagination |
| High Queue Volume | 50+ patients in queue | Virtualized list with scroll |
| AI Unavailable | LLM provider outage | Banner with manual workflow option |

### Edge Cases

| Case | Screen(s) Affected | Solution |
|------|-------------------|----------|
| Long text overflow | All with text content | Truncation with tooltip on hover |
| Missing patient photo | SCR-013 Patient Profile | Default avatar placeholder |
| Concurrent booking race | SCR-006 | 409 modal with alternative slots |
| Session timeout during intake | SCR-008, SCR-009 | Auto-save restores progress on re-login |
| Document parsing failure | SCR-012 | Error state with retry and manual entry options |
| Low confidence AI extraction | SCR-013 | Amber highlight with "Needs Review" badge |
| Multiple active queue alerts | SCR-011 | Stacked notification badges, expandable list |
| Admin config validation error | SCR-015 | Inline field errors with specific guidance |

---

## 9. Branding & Visual Direction

*See `designsystem.md` for all design tokens (colors, typography, spacing, shadows, etc.)*

### Branding Assets

- **Logo**: Healthcare platform wordmark (primary blue on white, white on primary blue)
- **Icon Style**: Outlined (Material Icons, MUI 5 icon library)
- **Illustration Style**: Flat, minimal healthcare illustrations for empty states
- **Photography Style**: Not applicable (no stock photos in MVP)

---

## 10. Component Specifications

### Component Library Reference

**Source**: `.propel/context/docs/designsystem.md` (Component Specifications section)

### Required Components per Screen

| Screen ID | Components Required | Notes |
|-----------|---------------------|-------|
| SCR-001 | TextField (2), Button (2), Link (2), Alert (1) | Email, password, login, register link, forgot password |
| SCR-002 | Card (3), Header (1), Sidebar (1) | Role-based routing cards |
| SCR-003 | TextField (5), Button (2), Checkbox (1), Alert (1) | Registration form fields |
| SCR-004 | TextField (1), Button (1), Alert (1), Link (1) | Email input for reset |
| SCR-005 | Card (4), Table (1), Header (1), Button (2), Badge (N) | Upcoming appointments, intake status, history summary |
| SCR-006 | Calendar (1), TimeSlotGrid (1), Button (2), Select (2), Modal (2) | Date picker, slot grid, provider filter |
| SCR-007 | Table (1), Badge (N), Button (1), Pagination (1) | Appointment history with status badges |
| SCR-008 | ChatBubble (N), TextField (1), Button (2), TypingIndicator (1) | Conversational AI interface |
| SCR-009 | TextField (8), Select (3), DatePicker (1), Button (2), Checkbox (2) | Manual intake form |
| SCR-010 | Card (4), Table (2), Header (1), Button (3), Badge (N) | Schedule, queue summary, pending tasks |
| SCR-011 | Table (1), Badge (N), Button (4), Timer (N), Select (1), Alert (1) | Queue table with status, priority, wait time |
| SCR-012 | DropZone (1), FileList (1), ProgressBar (N), Select (1), Button (2), Badge (N) | Upload area, processing status |
| SCR-013 | Card (N), Table (4), Badge (N), Tab (4), Alert (N), Button (3) | Meds, diagnoses, procedures, allergies tabs |
| SCR-014 | Table (2), Badge (N), Button (3), TextField (1), Modal (1) | ICD-10/CPT code tables, justification, override |
| SCR-015 | Tab (4), TextField (N), Select (N), Table (2), Button (4), Toggle (N) | Settings tabs: slots, notifications, hours, users |
| SCR-016 | TextField (1), Table (1), Button (1), Pagination (1) | Search input with results table |

### Component Summary

| Category | Components | Variants |
|----------|------------|----------|
| Actions | Button, Link, IconButton, FAB | Primary, Secondary, Tertiary, Ghost x S/M/L x States |
| Inputs | TextField, Select, Checkbox, Radio, Toggle, DatePicker, TimePicker | States (Default, Focused, Error, Disabled) x Sizes |
| Navigation | Header, Sidebar, Tabs, Breadcrumb, BottomNav, Pagination | Platform variants (desktop/mobile) |
| Content | Card, ListItem, Table, Badge, Avatar, Chip, Tooltip | Content variants |
| Feedback | Modal, Dialog, Drawer, Toast, Alert, Skeleton, ProgressBar, Spinner | Types (Info, Success, Warning, Error) x States |
| Specialized | ChatBubble, TypingIndicator, Calendar, TimeSlotGrid, DropZone, FileList, Timer, ConfidenceBadge | Domain-specific healthcare components |

### Component Constraints

- Use only components from designsystem.md
- No custom components without approval
- All components must support states: Default, Hover, Focus, Active, Disabled, Loading
- Follow naming convention: `C/<Category>/<Name>`

---

## 11. Prototype Flows

### Flow: FL-001 - Patient Authentication

**Flow ID**: FL-001
**Derived From**: UC-001
**Personas Covered**: Patient, Staff, Admin
**Description**: User logs in with email/password, gets routed to role-specific dashboard.

#### Flow Sequence

```text
1. Entry: SCR-001 Login / Default
   - Trigger: User navigates to application URL
   |
   v
2. Step: SCR-001 Login / Validation
   - Action: User enters credentials, inline validation on blur
   |
   v
3. Step: SCR-001 Login / Loading
   - Action: System authenticates user (JWT issued)
   |
   v
4. Decision Point:
   +-- Success -> SCR-002 Dashboard Router / Default -> Role-specific dashboard
   +-- Error: Invalid credentials -> SCR-001 Login / Error (error banner)
   +-- Error: Account locked -> SCR-001 Login / Error (lockout message, 30 min)
```

#### Required Interactions

- Inline email format validation on blur
- Password visibility toggle
- "Remember me" checkbox
- "Forgot Password" link to SCR-004
- "Create Account" link to SCR-003

---

### Flow: FL-002 - Patient Registration

**Flow ID**: FL-002
**Derived From**: UC-001
**Personas Covered**: Patient
**Description**: New patient creates account with email verification.

#### Flow Sequence

```text
1. Entry: SCR-003 Registration / Default
   - Trigger: Click "Create Account" from SCR-001
   |
   v
2. Step: SCR-003 Registration / Validation
   - Action: Fill name, email, password, DOB, phone; inline validation
   |
   v
3. Step: SCR-003 Registration / Loading
   - Action: System creates account, sends verification email
   |
   v
4. Decision Point:
   +-- Success -> SCR-003 Registration / Success (check email message)
   +-- Error: Email exists -> SCR-003 Registration / Error
```

#### Required Interactions

- Password strength indicator
- Email uniqueness check on blur
- Terms & conditions checkbox

---

### Flow: FL-003 - Patient Books Appointment

**Flow ID**: FL-003
**Derived From**: UC-001, UC-010
**Personas Covered**: Patient
**Description**: Patient searches for available slots, selects and confirms a booking.

#### Flow Sequence

```text
1. Entry: SCR-005 Patient Dashboard / Default
   - Trigger: Click "Book Appointment"
   |
   v
2. Step: SCR-006 Appointment Booking / Default
   - Action: Select date, time range, provider (optional)
   |
   v
3. Step: SCR-006 Appointment Booking / Loading
   - Action: System queries available slots (cache check -> DB fallback)
   |
   v
4. Step: SCR-006 Appointment Booking / Default
   - Action: Display available slots in calendar/grid view
   |
   v
5. Step: Modal - Booking Confirmation / Default
   - Action: Patient reviews and confirms slot
   |
   v
6. Decision Point:
   +-- Success -> SCR-005 Patient Dashboard / Default (confirmation toast + PDF QR)
   +-- Error: Slot conflict -> Modal - Slot Conflict / Error (3 alternative slots)
   +-- Abandon -> SCR-006 / Default (slot lock released after 1 min)
```

#### Required Interactions

- Calendar date picker with 90-day range
- Time slot grid with visual availability
- Provider filter dropdown
- Optimistic slot reservation indicator
- Waitlist option when no slots available

---

### Flow: FL-004 - Patient Completes AI Intake

**Flow ID**: FL-004
**Derived From**: UC-002
**Personas Covered**: Patient
**Description**: Patient completes intake via AI conversational interface with auto-save.

#### Flow Sequence

```text
1. Entry: SCR-005 Patient Dashboard / Default
   - Trigger: Click "Complete Intake" or prompted after booking
   |
   v
2. Step: SCR-008 AI Intake / Default
   - Action: AI greets and explains process; typing indicator shown
   |
   v
3. Loop: SCR-008 AI Intake / Default
   - Action: AI asks questions (mandatory fields first); patient responds
   - Auto-save triggers every 30 seconds
   |
   v
4. Decision Point - Mode Switch:
   +-- Continue AI -> Loop step 3 (optional fields)
   +-- Switch to Manual -> SCR-009 Manual Form / Default (pre-filled data)
   |
   v
5. Step: SCR-008 AI Intake / Default
   - Action: AI presents summary for review
   |
   v
6. Decision Point:
   +-- Confirm -> SCR-005 Patient Dashboard / Default (intake complete badge)
   +-- Edit -> Loop step 3 (correct specific fields)
```

#### Required Interactions

- Chat bubble UI with typing indicator
- Auto-save progress bar
- "Switch to Manual Form" button always visible
- Summary with editable fields
- Progress indicator (mandatory vs optional completion)

---

### Flow: FL-005 - Staff Registers Walk-in Patient

**Flow ID**: FL-005
**Derived From**: UC-003
**Personas Covered**: Staff
**Description**: Staff searches/creates patient and books same-day walk-in appointment.

#### Flow Sequence

```text
1. Entry: SCR-010 Staff Dashboard / Default
   - Trigger: Click "Walk-in Registration"
   |
   v
2. Step: Modal - Walk-in Registration / Default
   - Action: Search patient by name, DOB, or phone
   |
   v
3. Decision Point:
   +-- Patient found -> Select existing record
   +-- No match -> Enter new patient info (mini registration form)
   |
   v
4. Step: SCR-006 Appointment Booking / Default (staff context, walk-in mode)
   - Action: View same-day slots, select and set urgency
   |
   v
5. Decision Point:
   +-- Slots available -> Book walk-in + add to queue -> SCR-011 Arrival Queue
   +-- No slots -> Offer next available or escalate
```

#### Required Interactions

- Patient search with auto-complete
- Inline new patient mini-form
- Urgency toggle (Normal/Urgent)
- Automatic queue addition on booking

---

### Flow: FL-006 - Staff Manages Arrival Queue

**Flow ID**: FL-006
**Derived From**: UC-008
**Personas Covered**: Staff
**Description**: Staff manages real-time arrival queue with priority adjustment and no-show detection.

#### Flow Sequence

```text
1. Entry: SCR-011 Arrival Queue / Default
   - Trigger: Navigate to Queue from staff dashboard or sidebar
   |
   v
2. Step: SCR-011 / Default
   - Action: View sorted queue (appointment time + priority)
   |
   v
3. Action Options (parallel):
   +-- Mark Arrived -> Update queue entry (waiting)
   +-- Set Urgent -> Move to top of queue
   +-- Mark In-Visit -> Remove from waiting list
   +-- Auto No-Show (15 min) -> System marks no-show
   |
   v
4. Step: SCR-011 / Default (updated)
   - Action: Queue refreshes with updated wait times
```

#### Required Interactions

- Real-time updates (polling every 5s or WebSocket)
- Color-coded status badges (waiting, in-visit, no-show)
- Timer display for wait time per patient
- Queue filtering (provider, type, status)
- 30-minute wait threshold alert

---

### Flow: FL-007 - Staff Uploads & Reviews Clinical Documents

**Flow ID**: FL-007
**Derived From**: UC-005, UC-006
**Personas Covered**: Staff
**Description**: Staff uploads documents, system parses with AI, staff reviews extracted data.

#### Flow Sequence

```text
1. Entry: SCR-013 Patient Profile 360 / Default
   - Trigger: Navigate to patient profile
   |
   v
2. Step: SCR-012 Document Upload / Default
   - Trigger: Click "Upload Documents" tab
   - Action: Drag-and-drop or file picker; categorize documents
   |
   v
3. Step: SCR-012 Document Upload / Loading
   - Action: Upload progress bar; async parsing queued
   |
   v
4. Decision Point:
   +-- Valid file -> 202 Accepted; processing indicator
   +-- Invalid file -> SCR-012 / Error (format/size guidance)
   |
   v
5. Step: SCR-013 Patient Profile 360 / Default
   - Action: Review extracted data (medications, diagnoses, procedures, allergies)
   - Confidence badges (green >80%, amber 60-80%, red <60%)
   |
   v
6. Decision Point:
   +-- Conflicts detected -> Modal - Conflict Resolution (side-by-side)
   +-- Low confidence -> Flagged items highlighted for manual review
   +-- All clear -> Profile updated with source attribution
```

#### Required Interactions

- Drag-and-drop file zone
- Document category selector
- Processing status indicator (queued, processing, completed, failed)
- Confidence score visual indicators
- Conflict resolution side-by-side modal
- Source attribution links to original document

---

### Flow: FL-008 - Medical Coding Review

**Flow ID**: FL-008
**Derived From**: UC-007
**Personas Covered**: Staff
**Description**: Staff reviews AI-suggested ICD-10/CPT codes and approves or overrides.

#### Flow Sequence

```text
1. Entry: SCR-014 Medical Coding Review / Default
   - Trigger: Notification or navigate from patient profile
   |
   v
2. Step: SCR-014 / Default
   - Action: View AI-suggested ICD-10 and CPT codes with justifications
   - Confidence scores and code library validation status shown
   |
   v
3. Decision Point:
   +-- Approve all -> Codes finalized, agreement rate updated
   +-- Override code -> Modal - Code Override Justification
   +-- Invalid combination -> Claim denial risk flag shown
   |
   v
4. Step: SCR-014 / Default (updated)
   - Action: AI-human agreement rate displayed and updated
```

#### Required Interactions

- Code table with sortable columns (code, description, confidence, status)
- Approve/Override buttons per code
- Override justification text field (required)
- AI-human agreement rate meter
- Payer rule validation warnings

---

### Flow: FL-009 - Admin Configures System

**Flow ID**: FL-009
**Derived From**: UC-009
**Personas Covered**: Admin
**Description**: Admin configures appointment slots, notifications, business hours, and manages users.

#### Flow Sequence

```text
1. Entry: SCR-015 Admin Dashboard / Default
   - Trigger: Navigate to admin configuration
   |
   v
2. Step: SCR-015 / Default
   - Action: Select configuration tab (Slots, Notifications, Hours, Users)
   |
   v
3. Step: SCR-015 / Validation
   - Action: Edit configuration values; inline validation
   |
   v
4. Decision Point:
   +-- Valid config -> Save; success toast; audit logged
   +-- Invalid config -> SCR-015 / Validation (inline errors)
   +-- User management -> Create/deactivate staff accounts
```

#### Required Interactions

- Tab navigation for config categories
- Inline form validation (<200ms)
- Slot template builder (duration, buffer, provider, day-of-week)
- Notification template editor with variable placeholders
- Holiday calendar widget
- User management table with activate/deactivate toggle

---

### Flow: FL-010 - Error Recovery

**Flow ID**: FL-010
**Derived From**: UC-001, UC-002, UC-004, UC-005
**Personas Covered**: Patient, Staff, Admin
**Description**: User encounters error and recovers through guided actions.

#### Flow Sequence

```text
1. Entry: Any Screen / Error State
   - Trigger: API error, network failure, session timeout
   |
   v
2. Decision Point - Error Type:
   +-- Session Timeout -> Modal - Session Timeout Warning (extend/logout)
   +-- Network Error -> Banner (offline state, auto-retry)
   +-- API Error -> Screen Error State (retry button)
   +-- AI Unavailable -> Banner with manual workflow fallback
   |
   v
3. Recovery:
   +-- Retry -> Previous Screen / Loading -> Default
   +-- Re-login -> SCR-001 Login / Default
   +-- Manual Fallback -> Switch to non-AI workflow
```

#### Required Interactions

- Session timeout countdown modal
- Auto-retry with progress indicator
- Manual fallback navigation
- Error boundary with friendly message

---

## 12. Export Requirements

### Breakpoints

| Name | Width | Notes |
|------|-------|-------|
| Mobile | 320px - 767px | Single-column, hamburger nav |
| Tablet | 768px - 1023px | Two-column where appropriate |
| Desktop | 1024px - 2560px | Full sidebar, multi-column layouts |

### Export Formats

| Asset Type | Format | Size/Scale | Notes |
|------------|--------|------------|-------|
| Icons | SVG | 24x24 base | Material Icons, MUI 5 |
| Illustrations | SVG | Responsive | Empty state illustrations |
| Logo | SVG + PNG | @1x, @2x | Primary and white variants |

### Handoff Specifications

- Component props and variants documented in designsystem.md
- Spacing uses 8px base grid
- All measurements in pixels (px) for web
- Color values in hex format with opacity notation where needed

---

## 13. Figma File Structure

### Page Organization

```text
UPACIP Figma File
+-- 00_Cover
|   +-- Project info, version, stakeholders
+-- 01_Foundations
|   +-- Color tokens (Primary, Secondary, Semantic, Neutral, Status)
|   +-- Typography scale (Roboto, 10 levels)
|   +-- Spacing scale (8px base)
|   +-- Radius tokens
|   +-- Elevation/shadows (4 levels)
|   +-- Grid definitions (12-column, 3 breakpoints)
+-- 02_Components
|   +-- C/Actions/[Button, IconButton, Link]
|   +-- C/Inputs/[TextField, Select, Checkbox, Radio, Toggle, DatePicker]
|   +-- C/Navigation/[Header, Sidebar, Tabs, Breadcrumb, Pagination]
|   +-- C/Content/[Card, Table, Badge, Chip, Avatar, Tooltip]
|   +-- C/Feedback/[Modal, Dialog, Drawer, Toast, Alert, Skeleton, ProgressBar, Spinner]
|   +-- C/Specialized/[ChatBubble, TypingIndicator, Calendar, TimeSlotGrid, DropZone, FileList, Timer, ConfidenceBadge]
+-- 03_Patterns
|   +-- Auth form pattern (Login, Registration, Password Reset)
|   +-- Search + filter pattern (Patient Search, Queue Filter)
|   +-- Dashboard pattern (Patient, Staff, Admin)
|   +-- Conversational AI pattern (Intake Chat)
|   +-- Document upload pattern (Drag-drop, Progress)
|   +-- Error/Empty/Loading patterns (5 states)
+-- 04_Screens
|   +-- SCR-001_Login/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-002_DashboardRouter/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-003_Registration/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-004_PasswordReset/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-005_PatientDashboard/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-006_AppointmentBooking/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-007_AppointmentHistory/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-008_AIIntake/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-009_ManualIntake/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-010_StaffDashboard/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-011_ArrivalQueue/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-012_DocumentUpload/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-013_PatientProfile360/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-014_MedicalCoding/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-015_AdminDashboard/[Default, Loading, Empty, Error, Validation]
|   +-- SCR-016_PatientSearch/[Default, Loading, Empty, Error, Validation]
+-- 05_Prototype
|   +-- FL-001: Patient Authentication
|   +-- FL-002: Patient Registration
|   +-- FL-003: Patient Books Appointment
|   +-- FL-004: Patient Completes AI Intake
|   +-- FL-005: Staff Registers Walk-in
|   +-- FL-006: Staff Manages Arrival Queue
|   +-- FL-007: Staff Uploads & Reviews Documents
|   +-- FL-008: Medical Coding Review
|   +-- FL-009: Admin Configures System
|   +-- FL-010: Error Recovery
+-- 06_Handoff
    +-- Token usage rules
    +-- Component guidelines
    +-- Responsive specs (320px, 768px, 1024px)
    +-- Edge cases documentation
    +-- Accessibility notes (WCAG 2.1 AA)
```

---

## 14. Quality Checklist

### Pre-Export Validation

- [ ] All 16 screens have 5 required states (Default, Loading, Empty, Error, Validation)
- [ ] All components use design tokens from designsystem.md (no hard-coded values)
- [ ] Color contrast meets WCAG AA (>=4.5:1 text, >=3:1 UI)
- [ ] Focus states defined for all interactive elements
- [ ] Touch targets >= 44x44px on mobile viewports
- [ ] All 10 prototype flows wired and functional
- [ ] Naming conventions followed (SCR-XXX, FL-XXX, UXR-XXX, C/Category/Name)
- [ ] Export manifest complete for all screens and states

### Post-Generation

- [ ] designsystem.md created with all design tokens and component specifications
- [ ] All 36 UXR requirements mapped to screens
- [ ] All 10 UC-XXX use cases traced to screens and flows
- [ ] Persona coverage matrix validated (Patient, Staff, Admin)
- [ ] Handoff documentation complete
