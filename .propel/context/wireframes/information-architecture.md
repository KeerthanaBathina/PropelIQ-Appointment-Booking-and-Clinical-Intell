# Information Architecture - Unified Patient Access & Clinical Intelligence Platform

## 1. Wireframe Specification

**Fidelity Level**: High
**Screen Type**: Web (Responsive)
**Viewport**: 1440x900px (primary), 768x1024px (tablet), 375x812px (mobile)

## 2. System Overview

The Unified Patient Access & Clinical Intelligence Platform is a HIPAA-compliant healthcare web application that combines appointment scheduling, AI-driven patient intake, clinical document aggregation, and medical coding assistance. It serves three user personas (Patient, Staff, Admin) through role-based portals built with React 18, TypeScript, and Material-UI 5.

## 3. Wireframe References

### Generated Wireframes

**HTML Wireframes**:

| Screen/Feature | File Path | Description | Fidelity | Date Created |
|---------------|-----------|-------------|----------|--------------|
| SCR-001 Login | [./Hi-Fi/wireframe-SCR-001-login.html](./Hi-Fi/wireframe-SCR-001-login.html) | Authentication with email/password, MFA, registration link | High | 2026-04-15 |
| SCR-002 Dashboard Router | [./Hi-Fi/wireframe-SCR-002-dashboard-router.html](./Hi-Fi/wireframe-SCR-002-dashboard-router.html) | Role-based routing to persona-specific dashboard | High | 2026-04-15 |
| SCR-003 Registration | [./Hi-Fi/wireframe-SCR-003-registration.html](./Hi-Fi/wireframe-SCR-003-registration.html) | Patient self-registration with email verification | High | 2026-04-15 |
| SCR-004 Password Reset | [./Hi-Fi/wireframe-SCR-004-password-reset.html](./Hi-Fi/wireframe-SCR-004-password-reset.html) | Email-based password reset flow | High | 2026-04-15 |
| SCR-005 Patient Dashboard | [./Hi-Fi/wireframe-SCR-005-patient-dashboard.html](./Hi-Fi/wireframe-SCR-005-patient-dashboard.html) | Upcoming appointments, intake status, history summary | High | 2026-04-15 |
| SCR-006 Appointment Booking | [./Hi-Fi/wireframe-SCR-006-appointment-booking.html](./Hi-Fi/wireframe-SCR-006-appointment-booking.html) | Calendar date picker, slot grid, provider filter, booking confirmation | High | 2026-04-15 |
| SCR-007 Appointment History | [./Hi-Fi/wireframe-SCR-007-appointment-history.html](./Hi-Fi/wireframe-SCR-007-appointment-history.html) | Paginated appointment history with status badges | High | 2026-04-15 |
| SCR-008 AI Intake | [./Hi-Fi/wireframe-SCR-008-ai-intake.html](./Hi-Fi/wireframe-SCR-008-ai-intake.html) | Conversational AI chat interface with auto-save | High | 2026-04-15 |
| SCR-009 Manual Intake | [./Hi-Fi/wireframe-SCR-009-manual-intake.html](./Hi-Fi/wireframe-SCR-009-manual-intake.html) | Traditional form-based intake with pre-filled data | High | 2026-04-15 |
| SCR-010 Staff Dashboard | [./Hi-Fi/wireframe-SCR-010-staff-dashboard.html](./Hi-Fi/wireframe-SCR-010-staff-dashboard.html) | Daily schedule, queue summary, pending tasks | High | 2026-04-15 |
| SCR-011 Arrival Queue | [./Hi-Fi/wireframe-SCR-011-arrival-queue.html](./Hi-Fi/wireframe-SCR-011-arrival-queue.html) | Real-time queue with wait times, status, priority | High | 2026-04-15 |
| SCR-012 Document Upload | [./Hi-Fi/wireframe-SCR-012-document-upload.html](./Hi-Fi/wireframe-SCR-012-document-upload.html) | Drag-and-drop upload, parsing status, file list | High | 2026-04-15 |
| SCR-013 Patient Profile 360 | [./Hi-Fi/wireframe-SCR-013-patient-profile-360.html](./Hi-Fi/wireframe-SCR-013-patient-profile-360.html) | Consolidated clinical view with tabs and conflict resolution | High | 2026-04-15 |
| SCR-014 Medical Coding | [./Hi-Fi/wireframe-SCR-014-medical-coding.html](./Hi-Fi/wireframe-SCR-014-medical-coding.html) | AI-suggested ICD-10/CPT codes with approval workflow | High | 2026-04-15 |
| SCR-015 Admin Dashboard | [./Hi-Fi/wireframe-SCR-015-admin-dashboard.html](./Hi-Fi/wireframe-SCR-015-admin-dashboard.html) | Configuration tabs for slots, notifications, hours, users | High | 2026-04-15 |
| SCR-016 Patient Search | [./Hi-Fi/wireframe-SCR-016-patient-search.html](./Hi-Fi/wireframe-SCR-016-patient-search.html) | Search input with results table and pagination | High | 2026-04-15 |

### Component Inventory

**Reference**: See [Component Inventory](./component-inventory.md) for detailed component documentation including:

- Complete component specifications
- Component states and variants
- Responsive behavior details
- Reusability analysis
- Implementation priorities

## 4. User Personas & Flows

### Persona 1: Patient

- **Role**: End user / healthcare consumer
- **Goals**: Book appointments, complete intake forms, receive reminders, manage health info
- **Key Screens**: SCR-001, SCR-002, SCR-003, SCR-004, SCR-005, SCR-006, SCR-007, SCR-008, SCR-009
- **Primary Flow**: SCR-001 (Login) -> SCR-002 (Router) -> SCR-005 (Dashboard) -> SCR-006 (Book) -> SCR-008 (Intake)
- **Wireframe References**: wireframe-SCR-001 through wireframe-SCR-009
- **Decision Points**: AI vs Manual intake, Cancel appointment, Slot selection

### Persona 2: Staff

- **Role**: Administrative/clinical staff
- **Goals**: Manage walk-ins, mark arrivals, upload documents, verify data, resolve conflicts, review codes
- **Key Screens**: SCR-001, SCR-002, SCR-010, SCR-011, SCR-012, SCR-013, SCR-014, SCR-016
- **Primary Flow**: SCR-001 (Login) -> SCR-002 (Router) -> SCR-010 (Dashboard) -> SCR-011 (Queue) -> SCR-013 (Profile 360)
- **Wireframe References**: wireframe-SCR-010 through wireframe-SCR-014, wireframe-SCR-016
- **Decision Points**: Walk-in registration, Override AI codes, Resolve conflicts

### Persona 3: Admin

- **Role**: System administrator
- **Goals**: Configure settings, manage users, maintain templates, monitor system health
- **Key Screens**: SCR-001, SCR-002, SCR-015, SCR-016
- **Primary Flow**: SCR-001 (Login) -> SCR-002 (Router) -> SCR-015 (Admin Dashboard)
- **Wireframe References**: wireframe-SCR-015, wireframe-SCR-016
- **Decision Points**: User activation/deactivation, Configuration changes

### User Flow Diagrams

- **FL-001**: Patient Authentication - wireframe-SCR-001 -> wireframe-SCR-002
- **FL-002**: Patient Registration - wireframe-SCR-001 -> wireframe-SCR-003
- **FL-003**: Patient Books Appointment - wireframe-SCR-005 -> wireframe-SCR-006
- **FL-004**: Patient Completes AI Intake - wireframe-SCR-005 -> wireframe-SCR-008 -> wireframe-SCR-009
- **FL-005**: Staff Registers Walk-in - wireframe-SCR-010 -> wireframe-SCR-006 -> wireframe-SCR-011
- **FL-006**: Staff Manages Arrival Queue - wireframe-SCR-011
- **FL-007**: Staff Uploads & Reviews Documents - wireframe-SCR-013 -> wireframe-SCR-012
- **FL-008**: Medical Coding Review - wireframe-SCR-014
- **FL-009**: Admin Configures System - wireframe-SCR-015
- **FL-010**: Error Recovery - Any Screen -> wireframe-SCR-001

## 5. Screen Hierarchy

### Level 1: Public (Unauthenticated)

- **SCR-001 Login** (P0 - Critical) - [wireframe-SCR-001-login.html](./Hi-Fi/wireframe-SCR-001-login.html)
  - Description: Authentication entry point for all personas
  - User Entry Point: Yes
  - Key Components: TextField, Button, Link, Alert

- **SCR-003 Patient Registration** (P0 - Critical) - [wireframe-SCR-003-registration.html](./Hi-Fi/wireframe-SCR-003-registration.html)
  - Description: Patient self-registration form
  - Parent Screen: SCR-001
  - Key Components: TextField, Button, Checkbox, Alert

- **SCR-004 Password Reset** (P1 - High) - [wireframe-SCR-004-password-reset.html](./Hi-Fi/wireframe-SCR-004-password-reset.html)
  - Description: Email-based password recovery
  - Parent Screen: SCR-001
  - Key Components: TextField, Button, Alert, Link

### Level 2: Shared (Authenticated)

- **SCR-002 Dashboard Router** (P0 - Critical) - [wireframe-SCR-002-dashboard-router.html](./Hi-Fi/wireframe-SCR-002-dashboard-router.html)
  - Description: Role-based redirect to persona dashboard
  - Parent Screen: SCR-001
  - Key Components: Card, Header, Sidebar

### Level 3: Patient Portal

- **SCR-005 Patient Dashboard** (P0 - Critical) - [wireframe-SCR-005-patient-dashboard.html](./Hi-Fi/wireframe-SCR-005-patient-dashboard.html)
  - Description: Upcoming appointments, intake status, history
  - Parent Screen: SCR-002
  - Key Components: Card, Table, Header, Button, Badge

- **SCR-006 Appointment Booking** (P0 - Critical) - [wireframe-SCR-006-appointment-booking.html](./Hi-Fi/wireframe-SCR-006-appointment-booking.html)
  - Description: Calendar slot picker and booking confirmation
  - Parent Screen: SCR-005
  - Key Components: Calendar, TimeSlotGrid, Button, Select, Modal

- **SCR-007 Appointment History** (P1 - High) - [wireframe-SCR-007-appointment-history.html](./Hi-Fi/wireframe-SCR-007-appointment-history.html)
  - Description: Past appointment list with status filtering
  - Parent Screen: SCR-005
  - Key Components: Table, Badge, Button, Pagination

- **SCR-008 AI Conversational Intake** (P0 - Critical) - [wireframe-SCR-008-ai-intake.html](./Hi-Fi/wireframe-SCR-008-ai-intake.html)
  - Description: AI-powered chat-based patient intake
  - Parent Screen: SCR-005
  - Key Components: ChatBubble, TextField, Button, TypingIndicator

- **SCR-009 Manual Intake Form** (P0 - Critical) - [wireframe-SCR-009-manual-intake.html](./Hi-Fi/wireframe-SCR-009-manual-intake.html)
  - Description: Traditional form intake (AI fallback)
  - Parent Screen: SCR-008
  - Key Components: TextField, Select, DatePicker, Button, Checkbox

### Level 4: Staff Portal

- **SCR-010 Staff Dashboard** (P0 - Critical) - [wireframe-SCR-010-staff-dashboard.html](./Hi-Fi/wireframe-SCR-010-staff-dashboard.html)
  - Description: Daily schedule, queue summary, pending tasks
  - Parent Screen: SCR-002
  - Key Components: Card, Table, Header, Button, Badge

- **SCR-011 Arrival Queue** (P0 - Critical) - [wireframe-SCR-011-arrival-queue.html](./Hi-Fi/wireframe-SCR-011-arrival-queue.html)
  - Description: Real-time queue management
  - Parent Screen: SCR-010
  - Key Components: Table, Badge, Button, Timer, Select, Alert

- **SCR-012 Document Upload & Parsing** (P0 - Critical) - [wireframe-SCR-012-document-upload.html](./Hi-Fi/wireframe-SCR-012-document-upload.html)
  - Description: Clinical document upload with AI parsing status
  - Parent Screen: SCR-013
  - Key Components: DropZone, FileList, ProgressBar, Select, Button, Badge

- **SCR-013 Patient Profile 360** (P0 - Critical) - [wireframe-SCR-013-patient-profile-360.html](./Hi-Fi/wireframe-SCR-013-patient-profile-360.html)
  - Description: Consolidated clinical data with conflict resolution
  - Parent Screen: SCR-010
  - Key Components: Card, Table, Badge, Tab, Alert, Button

- **SCR-014 Medical Coding Review** (P0 - Critical) - [wireframe-SCR-014-medical-coding.html](./Hi-Fi/wireframe-SCR-014-medical-coding.html)
  - Description: AI-suggested code review and approval
  - Parent Screen: SCR-013
  - Key Components: Table, Badge, Button, TextField, Modal

- **SCR-016 Patient Search** (P1 - High) - [wireframe-SCR-016-patient-search.html](./Hi-Fi/wireframe-SCR-016-patient-search.html)
  - Description: Patient search with results table
  - Parent Screen: SCR-010, SCR-015
  - Key Components: TextField, Table, Button, Pagination

### Level 5: Admin Portal

- **SCR-015 Admin Configuration Dashboard** (P0 - Critical) - [wireframe-SCR-015-admin-dashboard.html](./Hi-Fi/wireframe-SCR-015-admin-dashboard.html)
  - Description: System configuration with tabs (Slots, Notifications, Hours, Users)
  - Parent Screen: SCR-002
  - Key Components: Tab, TextField, Select, Table, Button, Toggle

### Screen Priority Legend

- **P0**: Critical path screens (must-have for MVP)
- **P1**: High-priority screens (core functionality)
- **P2**: Medium-priority screens (important features)

### Modal/Dialog/Overlay Inventory

| Modal/Dialog Name | Type | Trigger Context | Parent Screen | Wireframe Reference | Priority |
|------------------|------|----------------|---------------|-------------------|----------|
| Booking Confirmation | Modal | Confirm appointment slot | SCR-006 | Embedded in wireframe-SCR-006 | P0 |
| Cancel Appointment | Dialog | Click cancel on appointment card | SCR-005, SCR-006, SCR-007 | Embedded in wireframe-SCR-005 | P0 |
| Session Timeout Warning | Modal | 2 min before 15-min session expiry | All authenticated screens | Global component | P0 |
| Slot Conflict Error | Modal | Concurrent booking detected (409) | SCR-006 | Embedded in wireframe-SCR-006 | P0 |
| Document Upload Progress | Drawer | Upload initiated | SCR-012 | Embedded in wireframe-SCR-012 | P0 |
| Conflict Resolution | Modal | Click flagged data conflict | SCR-013 | Embedded in wireframe-SCR-013 | P0 |
| Code Override Justification | Dialog | Staff overrides AI-suggested code | SCR-014 | Embedded in wireframe-SCR-014 | P0 |
| Walk-in Registration | Modal | Click Walk-in from staff dashboard | SCR-010 | Embedded in wireframe-SCR-010 | P0 |
| Delete User Confirmation | Dialog | Admin deactivates user account | SCR-015 | Embedded in wireframe-SCR-015 | P1 |
| MFA Setup | Modal | Staff/Admin enables MFA | SCR-001 | Embedded in wireframe-SCR-001 | P1 |
| Notification Preferences | Drawer | Patient edits SMS opt-out | SCR-005 | Embedded in wireframe-SCR-005 | P1 |
| PDF Confirmation Preview | Modal | View booking PDF/QR code | SCR-005, SCR-006 | Embedded in wireframe-SCR-005 | P1 |

**Modal Behavior Notes:**

- **Responsive Behavior:** Desktop modals (400-800px width); Mobile full-screen transformation
- **Dismissal Actions:** Close button (X), overlay click, ESC key, successful form submit
- **Focus Management:** Focus trap within modal, return focus to trigger on close
- **Accessibility:** `role="dialog"`, `aria-modal="true"`, `aria-labelledby`

## 6. Navigation Architecture

```text
UPACIP Navigation Tree
+-- SCR-001 Login (wireframe-SCR-001-login.html)
|   +-- SCR-003 Registration (wireframe-SCR-003-registration.html)
|   +-- SCR-004 Password Reset (wireframe-SCR-004-password-reset.html)
|   +-- SCR-002 Dashboard Router (wireframe-SCR-002-dashboard-router.html)
|       +-- [Patient] SCR-005 Patient Dashboard (wireframe-SCR-005-patient-dashboard.html)
|       |   +-- SCR-006 Appointment Booking (wireframe-SCR-006-appointment-booking.html)
|       |   +-- SCR-007 Appointment History (wireframe-SCR-007-appointment-history.html)
|       |   +-- SCR-008 AI Intake (wireframe-SCR-008-ai-intake.html)
|       |       +-- SCR-009 Manual Intake (wireframe-SCR-009-manual-intake.html)
|       +-- [Staff] SCR-010 Staff Dashboard (wireframe-SCR-010-staff-dashboard.html)
|       |   +-- SCR-011 Arrival Queue (wireframe-SCR-011-arrival-queue.html)
|       |   +-- SCR-012 Document Upload (wireframe-SCR-012-document-upload.html)
|       |   +-- SCR-013 Patient Profile 360 (wireframe-SCR-013-patient-profile-360.html)
|       |       +-- SCR-014 Medical Coding (wireframe-SCR-014-medical-coding.html)
|       |   +-- SCR-016 Patient Search (wireframe-SCR-016-patient-search.html)
|       +-- [Admin] SCR-015 Admin Dashboard (wireframe-SCR-015-admin-dashboard.html)
|           +-- SCR-016 Patient Search (wireframe-SCR-016-patient-search.html)
```

### Navigation Patterns

- **Primary Navigation**: Persistent left sidebar (256px) with role-based menu items; collapses to hamburger on mobile (<768px)
- **Secondary Navigation**: Tabs within dashboards (SCR-013, SCR-015) for sub-sections
- **Utility Navigation**: Top-right avatar dropdown with Profile, Settings, Logout
- **Mobile Navigation**: Full-screen overlay sidebar triggered by hamburger icon

## 7. Interaction Patterns

### Pattern 1: Appointment Booking (FL-003)

- **Trigger**: Patient clicks "Book Appointment" from SCR-005
- **Flow**: SCR-005 -> SCR-006 (select date) -> SCR-006 (select slot) -> Modal (confirm) -> SCR-005 (confirmation toast)
- **Screens Involved**: wireframe-SCR-005, wireframe-SCR-006
- **Feedback**: Optimistic slot reservation, success toast, PDF/QR generation
- **Components Used**: Calendar, TimeSlotGrid, Button, Modal, Toast

### Pattern 2: AI Conversational Intake (FL-004)

- **Trigger**: Patient clicks "Complete Intake" from SCR-005
- **Flow**: SCR-005 -> SCR-008 (AI chat loop) -> SCR-008 (summary review) -> SCR-005 (badge update)
- **Screens Involved**: wireframe-SCR-005, wireframe-SCR-008, wireframe-SCR-009
- **Feedback**: Typing indicator, auto-save progress, mode switch option
- **Components Used**: ChatBubble, TypingIndicator, TextField, Button, ProgressBar

### Pattern 3: Clinical Document Upload (FL-007)

- **Trigger**: Staff clicks "Upload Documents" tab from SCR-013
- **Flow**: SCR-013 -> SCR-012 (drag-drop) -> SCR-012 (processing) -> SCR-013 (review extracted data)
- **Screens Involved**: wireframe-SCR-012, wireframe-SCR-013
- **Feedback**: Upload progress bar, parsing status badges, confidence indicators
- **Components Used**: DropZone, FileList, ProgressBar, ConfidenceBadge, Toast

### Pattern 4: Medical Coding Approval (FL-008)

- **Trigger**: Staff navigates to coding review from notification or SCR-013
- **Flow**: SCR-014 (review codes) -> Approve/Override -> Modal (justification) -> SCR-014 (updated rates)
- **Screens Involved**: wireframe-SCR-014
- **Feedback**: Code validation badges, agreement rate meter, payer rule warnings
- **Components Used**: Table, Badge, Button, Modal, TextField

### Pattern 5: Form Validation (Cross-screen)

- **Trigger**: User interacts with form fields (blur/submit)
- **Flow**: Field input -> Blur -> Inline validation (<200ms) -> Error/success indicator
- **Screens Involved**: wireframe-SCR-001, wireframe-SCR-003, wireframe-SCR-004, wireframe-SCR-009, wireframe-SCR-015
- **Feedback**: Red border + helper text on error, green check on valid, scroll to first error on submit
- **Components Used**: TextField (error state), Alert (summary), Button (disabled until valid)

## 8. Error Handling

### Error Scenario 1: Network Error

- **Trigger**: Loss of connectivity during any operation
- **Error Screen/State**: Offline banner appears at top of current screen
- **User Action**: Wait for reconnection or manually retry
- **Recovery Flow**: Auto-retry on reconnect; banner dismisses on success

### Error Scenario 2: Session Timeout

- **Trigger**: 13 minutes of inactivity (warning at 2 min before 15-min expiry)
- **Error Screen/State**: Session Timeout Warning modal with countdown timer
- **User Action**: Click "Extend Session" or allow logout
- **Recovery Flow**: Token refresh on extend; redirect to SCR-001 on timeout

### Error Scenario 3: Booking Conflict (409)

- **Trigger**: Concurrent booking of same slot by another patient
- **Error Screen/State**: Slot Conflict modal on SCR-006
- **User Action**: Select from 3 alternative suggested slots
- **Recovery Flow**: New slot selection -> confirmation -> SCR-005

### Error Scenario 4: AI Service Unavailable

- **Trigger**: LLM provider outage during intake or parsing
- **Error Screen/State**: Banner with manual fallback option on SCR-008, SCR-012, SCR-014
- **User Action**: Switch to manual workflow
- **Recovery Flow**: SCR-008 -> SCR-009 (manual form); SCR-012 -> manual data entry

### Error Scenario 5: Document Upload Failure

- **Trigger**: Invalid file format, size exceeds limit, or server error
- **Error Screen/State**: Error state on SCR-012 with format guidance
- **User Action**: Select valid file and retry
- **Recovery Flow**: Error message lists supported formats (PDF, TIFF, PNG, JPEG) and 10MB limit

## 9. Responsive Strategy

| Breakpoint | Width | Layout Changes | Navigation Changes | Component Adaptations |
|-----------|-------|----------------|-------------------|---------------------|
| Mobile | 375px | Single column, stacked cards | Hamburger menu overlay | Touch targets 44px+, full-width buttons |
| Tablet | 768px | Two-column where appropriate | Collapsed sidebar | Two-column forms, compact tables |
| Desktop | 1440px | Multi-column with persistent sidebar | Expanded sidebar (256px) | Full table columns, side-by-side panels |

### Responsive Wireframe Variants

- All 16 HTML wireframes are responsive and adapt to mobile/tablet/desktop breakpoints
- Primary wireframe viewport: 1440px (desktop)
- Mobile: Sidebar collapses, forms stack, tables scroll horizontally
- Tablet: 2-column layouts, sidebar overlay on demand

## 10. Accessibility

### WCAG Compliance

- **Target Level**: WCAG 2.1 AA
- **Color Contrast**: 4.5:1 for normal text, 3:1 for large text and UI components
- **Keyboard Navigation**: All interactive elements reachable via Tab/Shift+Tab; Enter/Space for actions
- **Screen Reader Support**: ARIA labels on all form inputs, buttons, and interactive elements; live regions for dynamic content

### Accessibility Considerations by Screen

| Screen | Key Accessibility Features | Wireframe Notes |
|--------|---------------------------|----------------|
| SCR-001 Login | Password visibility toggle, ARIA labels on inputs | Focus auto-set to email field |
| SCR-006 Booking | Calendar keyboard navigation, slot grid as radiogroup | `role="radiogroup"` on TimeSlotGrid |
| SCR-008 AI Intake | Live region for AI responses, typing indicator announcements | `aria-live="polite"` on chat container |
| SCR-011 Queue | Live region for queue updates, timer announcements | `aria-live="polite"` on queue table |
| SCR-012 Upload | Drop zone keyboard accessible, progress announcements | `role="progressbar"` on upload |
| SCR-013 Profile 360 | Tab panel navigation, confidence score descriptions | `aria-label="AI confidence: XX%"` |
| SCR-014 Coding | Sortable table headers, override justification required field | `aria-sort` on table columns |

### Focus Order

- Login: Email -> Password -> Remember Me -> Login Button -> Forgot Password -> Create Account
- Dashboard: Sidebar items -> Main content cards -> Action buttons
- Forms: Top-to-bottom, left-to-right field order matching visual layout

## 11. Content Strategy

### Content Hierarchy

- **H1**: Page titles (one per screen, e.g., "Book Appointment")
- **H2**: Section titles (e.g., "Select Date", "Choose Time Slot")
- **H3**: Card titles, subsection headers
- **Body Text**: Primary content at 16px, secondary at 14px
- **Placeholder Content**: Realistic examples used in wireframes (not lorem ipsum for high-fidelity)

### Content Types by Screen

| Screen | Content Types | Wireframe Reference |
|--------|--------------|-------------------|
| SCR-001 Login | Form labels, error messages, links | wireframe-SCR-001 |
| SCR-005 Dashboard | Cards, tables, badges, notifications | wireframe-SCR-005 |
| SCR-006 Booking | Calendar data, slot grid, confirmation text | wireframe-SCR-006 |
| SCR-008 AI Intake | Chat bubbles, AI responses, progress | wireframe-SCR-008 |
| SCR-011 Queue | Real-time table, timers, status badges | wireframe-SCR-011 |
| SCR-013 Profile 360 | Clinical data tables, confidence badges, conflict panels | wireframe-SCR-013 |
| SCR-014 Coding | Code tables, justification text, agreement metrics | wireframe-SCR-014 |
| SCR-015 Admin | Configuration forms, user management table | wireframe-SCR-015 |
