# Component Inventory - Unified Patient Access & Clinical Intelligence Platform

## Component Specification

**Fidelity Level**: High
**Screen Type**: Web (Responsive)
**Viewport**: 1440x900px (primary)

## Component Summary

| Component Name | Type | Screens Used | Priority | Implementation Status |
|---------------|------|-------------|----------|---------------------|
| Header (AppBar) | Layout | All authenticated screens | High | Pending |
| Sidebar | Navigation | All authenticated screens | High | Pending |
| Button | Interactive | All screens | High | Pending |
| TextField | Interactive | SCR-001, SCR-003, SCR-004, SCR-008, SCR-009, SCR-014, SCR-015, SCR-016 | High | Pending |
| Card | Content | SCR-002, SCR-005, SCR-010, SCR-013 | High | Pending |
| Table | Content | SCR-005, SCR-007, SCR-010, SCR-011, SCR-013, SCR-014, SCR-015, SCR-016 | High | Pending |
| Badge | Content | SCR-005, SCR-007, SCR-010, SCR-011, SCR-012, SCR-013, SCR-014 | High | Pending |
| Modal | Feedback | SCR-006, SCR-010, SCR-013, SCR-014, SCR-015 | High | Pending |
| Dialog | Feedback | SCR-005, SCR-006, SCR-007, SCR-014, SCR-015 | High | Pending |
| Toast | Feedback | All screens | High | Pending |
| Alert | Feedback | SCR-001, SCR-003, SCR-004, SCR-011, SCR-013 | Medium | Pending |
| Tabs | Navigation | SCR-013, SCR-015 | Medium | Pending |
| Breadcrumb | Navigation | SCR-006, SCR-007, SCR-008, SCR-010, SCR-011, SCR-013, SCR-014, SCR-015 | Medium | Pending |
| Pagination | Navigation | SCR-007, SCR-016 | Medium | Pending |
| Select | Interactive | SCR-006, SCR-009, SCR-011, SCR-012, SCR-015 | Medium | Pending |
| Checkbox | Interactive | SCR-001, SCR-003, SCR-009 | Medium | Pending |
| Toggle | Interactive | SCR-015 | Medium | Pending |
| DatePicker | Interactive | SCR-006, SCR-009 | Medium | Pending |
| Calendar | Specialized | SCR-006 | High | Pending |
| TimeSlotGrid | Specialized | SCR-006 | High | Pending |
| ChatBubble | Specialized | SCR-008 | High | Pending |
| TypingIndicator | Specialized | SCR-008 | High | Pending |
| DropZone | Specialized | SCR-012 | High | Pending |
| FileList | Specialized | SCR-012 | High | Pending |
| ProgressBar | Feedback | SCR-012 | Medium | Pending |
| Timer | Specialized | SCR-011 | Medium | Pending |
| ConfidenceBadge | Specialized | SCR-012, SCR-013, SCR-014 | High | Pending |
| Skeleton | Feedback | All data-driven screens | Medium | Pending |
| Spinner | Feedback | Button loading states | Low | Pending |
| Avatar | Content | SCR-013, Header | Low | Pending |
| Chip | Content | SCR-013, SCR-014 | Low | Pending |
| Tooltip | Content | All screens | Low | Pending |
| Link | Interactive | SCR-001, SCR-004 | Medium | Pending |
| Drawer | Feedback | SCR-005, SCR-012 | Medium | Pending |

## Detailed Component Specifications

### Layout Components

#### Header (AppBar)

- **Type**: Layout
- **Used In Screens**: All authenticated screens (SCR-002 through SCR-016)
- **Wireframe References**: All wireframe files
- **Description**: Top navigation bar with logo, search (staff), and user menu
- **Variants**: Patient (primary accent), Staff (secondary accent), Admin (secondary accent)
- **Interactive States**: Default
- **Responsive Behavior**:
  - Desktop (1440px): Full width, logo left, search center (staff), avatar right, 64px height
  - Tablet (768px): Full width, logo left, avatar right, search collapsed
  - Mobile (375px): Hamburger left, logo center, avatar right, 56px height
- **Implementation Notes**: MUI AppBar with `position="fixed"`, `elevation={2}`, `z-index: 1030`

#### Sidebar

- **Type**: Layout/Navigation
- **Used In Screens**: All authenticated screens
- **Wireframe References**: All wireframe files
- **Description**: Persistent left navigation with role-based menu items
- **Variants**: Expanded (256px), Collapsed (72px), Mobile overlay (100%)
- **Interactive States**: Default, Active item (primary.50 bg + left accent)
- **Responsive Behavior**:
  - Desktop (1440px): Persistent, 256px, expanded with labels
  - Tablet (768px): Collapsed to 72px, icons only, expand on hover
  - Mobile (375px): Hidden; full-screen overlay on hamburger click
- **Implementation Notes**: MUI Drawer with `variant="permanent"` on desktop, `variant="temporary"` on mobile

### Navigation Components

#### Tabs

- **Type**: Navigation
- **Used In Screens**: SCR-013 (Medications, Diagnoses, Procedures, Allergies), SCR-015 (Slots, Notifications, Hours, Users)
- **Wireframe References**: wireframe-SCR-013, wireframe-SCR-015
- **Description**: Horizontal tab bar for sub-section navigation within a screen
- **Variants**: Underlined (primary.500 bottom border on active)
- **Interactive States**: Default (neutral.600), Hover (primary.50 bg), Active (primary.500 + underline), Focused (2px outline)
- **Responsive Behavior**:
  - Desktop (1440px): Full width, all tabs visible
  - Tablet (768px): Scrollable if overflow
  - Mobile (375px): Scrollable, compact labels
- **Implementation Notes**: MUI Tabs with `role="tablist"`, `aria-selected` on active

#### Breadcrumb

- **Type**: Navigation
- **Used In Screens**: SCR-006, SCR-007, SCR-008, SCR-010, SCR-011, SCR-013, SCR-014, SCR-015
- **Wireframe References**: Corresponding wireframes
- **Description**: Hierarchical path indicator for nested screens
- **Variants**: Default (separator: "/", max 3 visible + collapse)
- **Interactive States**: Parent links (primary.500), Current (neutral.900, no link)
- **Responsive Behavior**:
  - Desktop (1440px): Full path shown (up to 3 levels)
  - Tablet/Mobile: Collapse to last 2 levels with "..." prefix
- **Implementation Notes**: MUI Breadcrumbs, `aria-label="breadcrumb"`

#### Pagination

- **Type**: Navigation
- **Used In Screens**: SCR-007 (Appointment History), SCR-016 (Patient Search)
- **Wireframe References**: wireframe-SCR-007, wireframe-SCR-016
- **Description**: Page controls for list navigation (20 items per page)
- **Variants**: Numbered pages with first/last, prev/next
- **Interactive States**: Default, Active (primary.500 bg), Hover, Disabled (first/last on boundaries)
- **Responsive Behavior**:
  - Desktop (1440px): Show 5 visible page numbers
  - Mobile (375px): Prev/Next only with page indicator
- **Implementation Notes**: MUI Pagination, `aria-label="pagination navigation"`

### Content Components

#### Card

- **Type**: Content
- **Used In Screens**: SCR-002, SCR-005, SCR-010, SCR-013
- **Wireframe References**: wireframe-SCR-002, wireframe-SCR-005, wireframe-SCR-010, wireframe-SCR-013
- **Description**: Container for grouped content (appointments, queue summary, stats)
- **Variants**: Default (elevation-1), Flat (1px border), Clickable (elevation-2 on hover)
- **Interactive States**: Default, Hover (elevation-2 if clickable)
- **Responsive Behavior**:
  - Desktop (1440px): Grid layout (2-4 columns)
  - Tablet (768px): 2-column grid
  - Mobile (375px): Single column, full width, stacked
- **Implementation Notes**: MUI Card with 16px padding, 8px radius, `role="article"` or `role="button"` if clickable

#### Table

- **Type**: Content
- **Used In Screens**: SCR-005, SCR-007, SCR-010, SCR-011, SCR-013, SCR-014, SCR-015, SCR-016
- **Wireframe References**: Corresponding wireframes
- **Description**: Data display with sortable columns, row actions
- **Variants**: Default (striped), Compact, Sortable
- **Interactive States**: Row hover (neutral.100), Row selected (primary.50), Sort header click
- **Responsive Behavior**:
  - Desktop (1440px): Full columns visible, sorting enabled
  - Tablet (768px): Hide non-essential columns, horizontal scroll for complex tables
  - Mobile (375px): Card-per-row transformation or horizontal scroll
- **Implementation Notes**: MUI Table with `aria-sort` on sortable headers, 52px min row height

#### Badge

- **Type**: Content
- **Used In Screens**: SCR-005, SCR-007, SCR-010, SCR-011, SCR-012, SCR-013, SCR-014
- **Wireframe References**: Corresponding wireframes
- **Description**: Status indicators for appointments, queue, confidence, processing
- **Variants**: Default (neutral.200), Primary, Success, Warning, Error, Info
- **Interactive States**: Static (no interaction)
- **Responsive Behavior**: Consistent size across breakpoints (pill shape, 2px 8px padding)
- **Implementation Notes**: MUI Chip variant="filled", `overline` typography, radius.full

#### Avatar

- **Type**: Content
- **Used In Screens**: Header (user menu), SCR-013 (patient profile)
- **Wireframe References**: Header component, wireframe-SCR-013
- **Description**: User photo or initials fallback
- **Variants**: Small (32px), Medium (40px), Large (56px)
- **Interactive States**: Default, Clickable (header dropdown)
- **Responsive Behavior**: Consistent across breakpoints
- **Implementation Notes**: MUI Avatar with initials fallback on primary.100 bg

#### Chip

- **Type**: Content
- **Used In Screens**: SCR-013, SCR-014
- **Wireframe References**: wireframe-SCR-013, wireframe-SCR-014
- **Description**: Compact display of tags, categories, or removable items
- **Variants**: Outlined (unselected), Filled (selected), Deletable
- **Interactive States**: Default, Selected, Hover, Delete hover
- **Responsive Behavior**: Wrap to next line on small viewports
- **Implementation Notes**: MUI Chip, 32px height, 16px radius

### Interactive Components

#### Button

- **Type**: Interactive
- **Used In Screens**: All screens
- **Wireframe References**: All wireframe files
- **Description**: Primary interaction element for actions
- **Variants**: Primary (Contained), Secondary (Outlined), Tertiary (Text), Error (Contained), Disabled
- **Interactive States**: Default, Hover (+8% opacity overlay), Active (+16%), Focus (2px outline), Disabled (40% opacity), Loading (spinner replaces label)
- **Responsive Behavior**:
  - Desktop (1440px): Inline with content, min-width 64px
  - Mobile (375px): Full-width for primary CTAs, 44px min height
- **Implementation Notes**: MUI Button, `role="button"`, `aria-disabled` when disabled, `aria-busy` when loading

#### TextField

- **Type**: Interactive
- **Used In Screens**: SCR-001, SCR-003, SCR-004, SCR-008, SCR-009, SCR-014, SCR-015, SCR-016
- **Wireframe References**: Corresponding wireframes
- **Description**: Text input with label, helper text, validation states
- **Variants**: Standard (outlined), Password (with visibility toggle), Search (with icon)
- **Interactive States**: Default, Hover, Focused (primary.500 border), Error (error.main border), Disabled, Read-only
- **Responsive Behavior**:
  - Desktop (1440px): Inline in forms, 48px height
  - Mobile (375px): Full-width, stacked labels
- **Implementation Notes**: MUI TextField with `aria-invalid`, `aria-describedby`, `aria-required`

#### Select

- **Type**: Interactive
- **Used In Screens**: SCR-006, SCR-009, SCR-011, SCR-012, SCR-015
- **Wireframe References**: Corresponding wireframes
- **Description**: Dropdown selection with search/filter capability
- **Variants**: Standard, Multi-select, Searchable
- **Interactive States**: Same as TextField + Dropdown open/closed
- **Responsive Behavior**: Dropdown panel max-height 300px, scrollable
- **Implementation Notes**: MUI Select, `role="combobox"`, `aria-expanded`, `aria-activedescendant`

#### Checkbox

- **Type**: Interactive
- **Used In Screens**: SCR-001 (Remember Me), SCR-003 (Terms), SCR-009 (intake options)
- **Wireframe References**: wireframe-SCR-001, wireframe-SCR-003, wireframe-SCR-009
- **Description**: Binary choice with label
- **Variants**: Unchecked, Checked, Indeterminate, Disabled
- **Interactive States**: Default, Hover, Focus (2px outline), Checked (primary.500)
- **Responsive Behavior**: 44px touch target on mobile
- **Implementation Notes**: MUI Checkbox, 24px icon size, native `<input type="checkbox">`

#### Toggle (Switch)

- **Type**: Interactive
- **Used In Screens**: SCR-015 (user activate/deactivate, feature toggles)
- **Wireframe References**: wireframe-SCR-015
- **Description**: On/off toggle for binary settings
- **Variants**: On (primary.500 track), Off (neutral.300 track), Disabled
- **Interactive States**: Default, Hover, Focus, On, Off, Disabled
- **Responsive Behavior**: Consistent 34x20px across breakpoints, 44px touch target
- **Implementation Notes**: MUI Switch, `role="switch"`, `aria-checked`

#### DatePicker

- **Type**: Interactive
- **Used In Screens**: SCR-006 (booking date), SCR-009 (DOB)
- **Wireframe References**: wireframe-SCR-006, wireframe-SCR-009
- **Description**: Calendar popup for date selection
- **Variants**: Standard (MM/DD/YYYY), Range-limited (90 days for booking)
- **Interactive States**: Default, Open (calendar popup), Selected, Error
- **Responsive Behavior**: Calendar popup positions above/below based on viewport space
- **Implementation Notes**: MUI DatePicker, elevation-3 popup

#### Link

- **Type**: Interactive
- **Used In Screens**: SCR-001 (Forgot Password, Create Account), SCR-004 (Back to Login)
- **Wireframe References**: wireframe-SCR-001, wireframe-SCR-004
- **Description**: Inline text navigation element
- **Variants**: Default (primary.500), Visited (primary.700)
- **Interactive States**: Default, Hover (underline), Focus (2px outline), Visited
- **Responsive Behavior**: Consistent across breakpoints
- **Implementation Notes**: HTML `<a>` semantics, `text-decoration: underline` on hover

### Feedback Components

#### Modal

- **Type**: Feedback
- **Used In Screens**: SCR-006, SCR-010, SCR-013, SCR-014, SCR-015
- **Wireframe References**: Embedded in parent wireframes
- **Description**: Centered overlay dialog for confirmations, forms, and data entry
- **Variants**: Small (400px), Medium (600px), Large (800px)
- **Interactive States**: Open (with backdrop), Closing (transition)
- **Responsive Behavior**:
  - Desktop (1440px): Centered modal with backdrop
  - Mobile (375px): Full-screen transformation
- **Implementation Notes**: MUI Dialog, `role="dialog"`, `aria-modal="true"`, focus trap, ESC to close

#### Dialog

- **Type**: Feedback
- **Used In Screens**: SCR-005, SCR-006, SCR-007, SCR-014, SCR-015
- **Wireframe References**: Embedded in parent wireframes
- **Description**: Compact confirmation prompt with 2-3 actions
- **Variants**: Confirmation (2 buttons), Destructive (error button)
- **Interactive States**: Open, Closing
- **Responsive Behavior**: Same as Modal but smaller (max 400px)
- **Implementation Notes**: MUI Dialog variant, `aria-labelledby` pointing to title

#### Drawer

- **Type**: Feedback
- **Used In Screens**: SCR-005 (Notification Preferences), SCR-012 (Upload Progress)
- **Wireframe References**: wireframe-SCR-005, wireframe-SCR-012
- **Description**: Side panel overlay for secondary content
- **Variants**: Right-side (400px desktop, 100% mobile)
- **Interactive States**: Open, Closing
- **Responsive Behavior**:
  - Desktop (1440px): Right panel, 400px width
  - Mobile (375px): Full-screen overlay
- **Implementation Notes**: MUI Drawer with `anchor="right"`, elevation-2

#### Toast (Snackbar)

- **Type**: Feedback
- **Used In Screens**: All screens (post-action feedback)
- **Wireframe References**: Global component in all wireframes
- **Description**: Auto-dismissing notification for action results
- **Variants**: Success (green), Error (red), Warning (amber), Info (blue)
- **Interactive States**: Entering, Visible (5s default), Exiting
- **Responsive Behavior**:
  - Desktop (1440px): Bottom-right corner
  - Mobile (375px): Bottom-center, full width
- **Implementation Notes**: MUI Snackbar, `role="alert"`, `aria-live="polite"` or `"assertive"` for errors

#### Alert (Banner)

- **Type**: Feedback
- **Used In Screens**: SCR-001, SCR-003, SCR-004, SCR-011, SCR-013
- **Wireframe References**: Corresponding wireframes
- **Description**: Full-width in-content notification banner
- **Variants**: Info, Success, Warning, Error
- **Interactive States**: Default, Dismissable (with close X)
- **Responsive Behavior**: Full-width within content area across all breakpoints
- **Implementation Notes**: MUI Alert, `role="alert"`, semantic surface color backgrounds

#### Skeleton

- **Type**: Feedback
- **Used In Screens**: All data-driven screens
- **Wireframe References**: Loading state in all wireframes
- **Description**: Loading placeholder matching target component shape
- **Variants**: Rectangle, Circle, Text line
- **Interactive States**: Pulse animation (opacity 0.4 -> 1.0, 1.5s)
- **Responsive Behavior**: Match target component dimensions at each breakpoint
- **Implementation Notes**: MUI Skeleton, shown when data fetch exceeds 300ms

#### ProgressBar

- **Type**: Feedback
- **Used In Screens**: SCR-012 (upload progress)
- **Wireframe References**: wireframe-SCR-012
- **Description**: Linear progress indicator for file uploads
- **Variants**: Determinate (with %), Indeterminate
- **Interactive States**: Active (animating), Complete
- **Responsive Behavior**: Full-width within parent container
- **Implementation Notes**: MUI LinearProgress, `role="progressbar"`, `aria-valuenow`

### Specialized Healthcare Components

#### Calendar (Booking)

- **Type**: Specialized
- **Used In Screens**: SCR-006
- **Wireframe References**: wireframe-SCR-006
- **Description**: Date picker calendar for appointment booking (90-day range)
- **Variants**: Month view with availability dots
- **Interactive States**: Available date (primary.500 dot), Selected (primary.500 fill), Today (outlined), Unavailable (neutral.300, disabled)
- **Responsive Behavior**:
  - Desktop (1440px): Inline calendar with slot grid side-by-side
  - Mobile (375px): Full-width calendar, slots below
- **Implementation Notes**: MUI DateCalendar, keyboard navigable, `aria-label="Select appointment date"`

#### TimeSlotGrid

- **Type**: Specialized
- **Used In Screens**: SCR-006
- **Wireframe References**: wireframe-SCR-006
- **Description**: Grid of available appointment time slots
- **Variants**: Available (outlined), Selected (filled primary), Unavailable (filled neutral.200)
- **Interactive States**: Default, Hover, Selected, Disabled
- **Responsive Behavior**:
  - Desktop (1440px): 4-column grid, 80x40px slots
  - Tablet (768px): 3-column grid
  - Mobile (375px): 2-column grid
- **Implementation Notes**: Custom component, `role="radiogroup"`, each slot `role="radio"`, `aria-checked`

#### ChatBubble

- **Type**: Specialized
- **Used In Screens**: SCR-008
- **Wireframe References**: wireframe-SCR-008
- **Description**: Conversational message bubble for AI intake chat
- **Variants**: User (primary.100, right-aligned), AI (neutral.100, left-aligned)
- **Interactive States**: Default, New (fade-in animation)
- **Responsive Behavior**: Max-width 80% of container across all breakpoints
- **Implementation Notes**: Custom component, 12px 16px padding, 12px radius, timestamp below

#### TypingIndicator

- **Type**: Specialized
- **Used In Screens**: SCR-008
- **Wireframe References**: wireframe-SCR-008
- **Description**: Three bouncing dots indicating AI is generating response
- **Variants**: Default (inside AI-style bubble)
- **Interactive States**: Animating (bounce loop)
- **Responsive Behavior**: Consistent across breakpoints
- **Implementation Notes**: CSS keyframe animation, neutral.500 dots, `aria-label="AI is typing"`

#### DropZone

- **Type**: Specialized
- **Used In Screens**: SCR-012
- **Wireframe References**: wireframe-SCR-012
- **Description**: Drag-and-drop file upload area
- **Variants**: Default (dashed border), Drag-over (primary border + bg), Error
- **Interactive States**: Default, Drag-over (highlight), Error (invalid file)
- **Responsive Behavior**:
  - Desktop (1440px): 200px height, full content area width
  - Mobile (375px): 150px height, "Tap to upload" label
- **Implementation Notes**: 2px dashed border, accepts PDF/TIFF/PNG/JPEG, `<input type="file">` fallback

#### FileList

- **Type**: Specialized
- **Used In Screens**: SCR-012
- **Wireframe References**: wireframe-SCR-012
- **Description**: List of uploaded files with status and actions
- **Variants**: Default (56px item height)
- **Interactive States**: Processing (progress bar below), Complete (success badge), Failed (error badge)
- **Responsive Behavior**: Full-width list across all breakpoints
- **Implementation Notes**: MUI List, file type icon left, name+size center, status+action right

#### Timer (Wait Time)

- **Type**: Specialized
- **Used In Screens**: SCR-011
- **Wireframe References**: wireframe-SCR-011
- **Description**: Real-time wait duration display per queue entry
- **Variants**: Normal (<15min, success.main), Warning (15-30min, warning.main), Alert (>30min, error.main)
- **Interactive States**: Counting (updates every second)
- **Responsive Behavior**: Consistent h4 typography across breakpoints
- **Implementation Notes**: Format MM:SS, color-coded by threshold, `aria-label="Wait time: X minutes"`

#### ConfidenceBadge

- **Type**: Specialized
- **Used In Screens**: SCR-012, SCR-013, SCR-014
- **Wireframe References**: wireframe-SCR-012, wireframe-SCR-013, wireframe-SCR-014
- **Description**: AI confidence score indicator (pill-shaped percentage display)
- **Variants**: High (>=80%, success.main), Medium (60-79%, warning.main), Low (<60%, error.main)
- **Interactive States**: Static (no interaction), Tooltip on hover with explanation
- **Responsive Behavior**: Consistent pill shape across breakpoints
- **Implementation Notes**: radius.full, white text on colored bg, `aria-label="AI confidence: XX%"`

## Component Relationships

```text
Header (AppBar)
+-- Logo
+-- Search Bar (Staff/Admin only)
|   +-- TextField (search variant)
|   +-- IconButton (search icon)
+-- User Menu
    +-- Avatar
    +-- Dropdown Menu (Profile, Settings, Logout)

Sidebar
+-- Nav Items (role-based)
    +-- Icon + Label (expanded)
    +-- Icon only (collapsed)
    +-- Active indicator (left accent bar)

Page Layout
+-- Breadcrumb (depth > 2)
+-- Page Title (H1)
+-- Content Area
    +-- Cards / Tables / Forms
    +-- Action Buttons
+-- Floating Elements
    +-- Toast (bottom-right/bottom-center)
    +-- Modal (centered overlay)
    +-- Drawer (right panel)
```

## Component States Matrix

| Component | Default | Hover | Active | Focus | Disabled | Error | Loading | Empty |
|-----------|---------|-------|--------|-------|----------|-------|---------|-------|
| Button | x | x | x | x | x | - | x | - |
| TextField | x | x | x | x | x | x | - | x |
| Select | x | x | x | x | x | x | x | x |
| Card | x | x | - | - | - | - | x | x |
| Table | x | x | - | - | - | - | x | x |
| Badge | x | - | - | - | - | - | - | - |
| Modal | x | - | - | x | - | - | - | - |
| Toast | x | - | - | - | - | - | - | - |
| Alert | x | - | - | - | - | - | - | - |
| Tabs | x | x | x | x | x | - | - | - |
| Calendar | x | x | x | x | x | - | x | - |
| TimeSlotGrid | x | x | x | x | x | - | x | x |
| ChatBubble | x | - | - | - | - | - | - | - |
| DropZone | x | x | - | x | - | x | x | - |
| Checkbox | x | x | x | x | x | - | - | - |
| Toggle | x | x | x | x | x | - | - | - |
| Skeleton | x | - | - | - | - | - | - | - |

## Reusability Analysis

| Component | Reuse Count | Screens | Recommendation |
|-----------|-------------|---------|----------------|
| Button | 16 screens | All | Create as shared component with 5 variants |
| TextField | 8 screens | SCR-001,003,004,008,009,014,015,016 | Shared component with validation states |
| Table | 8 screens | SCR-005,007,010,011,013,014,015,016 | Shared with sortable/paginated variants |
| Badge | 7 screens | SCR-005,007,010,011,012,013,014 | Shared with 6 color variants |
| Card | 4 screens | SCR-002,005,010,013 | Shared with flat/elevated/clickable variants |
| Header | 16 screens | All authenticated | Single shared instance, 3 role variants |
| Sidebar | 16 screens | All authenticated | Single shared instance, 3 role menus |
| Toast | 16 screens | All | Global singleton, 4 type variants |
| ChatBubble | 1 screen | SCR-008 only | Screen-specific component |
| DropZone | 1 screen | SCR-012 only | Screen-specific component |
| Timer | 1 screen | SCR-011 only | Screen-specific component |

## Responsive Breakpoints Summary

| Breakpoint | Width | Components Affected | Key Adaptations |
|-----------|-------|-------------------|-----------------|
| Mobile | 375px | Sidebar, Table, Card, Button, Header, Forms | Sidebar: overlay; Tables: card-per-row or scroll; Forms: single column; Buttons: full-width primary CTAs |
| Tablet | 768px | Sidebar, Table, Card, Tabs | Sidebar: collapsed (72px); Cards: 2-column; Tabs: scrollable |
| Desktop | 1440px | All | Full layout; Sidebar: expanded (256px); Multi-column grids; Inline forms |

## Implementation Priority Matrix

### High Priority (Core Components)

- [ ] Header (AppBar) - Used in all 16 screens, critical for navigation
- [ ] Sidebar - Used in all 16 screens, role-based menu routing
- [ ] Button - Primary interaction element across all screens
- [ ] TextField - Core form input for authentication, intake, search
- [ ] Table - Primary data display for 8 screens
- [ ] Card - Dashboard content containers
- [ ] Modal/Dialog - Confirmation and override workflows
- [ ] Calendar + TimeSlotGrid - Core booking flow (SCR-006)
- [ ] ChatBubble + TypingIndicator - AI intake flow (SCR-008)
- [ ] DropZone + FileList - Document upload flow (SCR-012)
- [ ] ConfidenceBadge - AI data trust indicators (3 screens)

### Medium Priority (Feature Components)

- [ ] Badge - Status indicators across 7 screens
- [ ] Tabs - Sub-navigation for SCR-013, SCR-015
- [ ] Breadcrumb - Nested screen navigation (8 screens)
- [ ] Toast - Feedback notifications
- [ ] Alert - Inline error/info banners
- [ ] Select - Dropdown filters and form selects
- [ ] Pagination - List navigation (SCR-007, SCR-016)
- [ ] ProgressBar - Upload progress (SCR-012)
- [ ] Timer - Queue wait display (SCR-011)

### Low Priority (Enhancement Components)

- [ ] Avatar - User photos/initials
- [ ] Chip - Tags and categories
- [ ] Tooltip - Contextual help
- [ ] Spinner - Button loading states
- [ ] Skeleton - Loading placeholders
- [ ] Drawer - Secondary content panels

## Framework-Specific Notes

**Detected Framework**: React 18 + TypeScript
**Component Library**: Material-UI (MUI) 5

### Framework Patterns Applied

- MUI ThemeProvider for design token injection
- Styled-components/emotion for token-based styling
- React Router v6 for screen navigation (SPA)
- MUI Grid v2 for responsive 12-column layouts

### Component Library Mappings

| Wireframe Component | Framework Component | Customization Required |
|-------------------|-------------------|----------------------|
| Button | @mui/material/Button | Color variants, loading state |
| TextField | @mui/material/TextField | Validation error styling |
| Card | @mui/material/Card | Elevation variants, clickable |
| Table | @mui/material/Table | Sortable headers, striped rows |
| Modal | @mui/material/Dialog | Size variants (sm/md/lg) |
| Tabs | @mui/material/Tabs | Underlined variant |
| Badge | @mui/material/Chip | Status color mapping |
| Select | @mui/material/Select | Searchable variant |
| Toggle | @mui/material/Switch | Width override |
| Calendar | @mui/x-date-pickers/DateCalendar | Availability dots |
| Toast | @mui/material/Snackbar | Auto-dismiss timing |
| Alert | @mui/material/Alert | Full-width variant |
| Skeleton | @mui/material/Skeleton | Pulse animation |
| ProgressBar | @mui/material/LinearProgress | Determinate variant |
| Avatar | @mui/material/Avatar | Initials fallback |
| ChatBubble | Custom component | No MUI equivalent |
| TypingIndicator | Custom component | CSS keyframe animation |
| TimeSlotGrid | Custom component | Radio group semantics |
| DropZone | Custom component | HTML5 drag-and-drop API |
| FileList | @mui/material/List | Custom list item |
| Timer | Custom component | setInterval counter |
| ConfidenceBadge | @mui/material/Chip | Color threshold logic |

## Accessibility Considerations

| Component | ARIA Attributes | Keyboard Navigation | Screen Reader Notes |
|-----------|----------------|-------------------|-------------------|
| Button | `role="button"`, `aria-disabled`, `aria-busy` | Enter/Space to activate | Announces label + state |
| TextField | `aria-invalid`, `aria-describedby`, `aria-required` | Tab to focus, type to input | Announces label + helper text |
| Modal | `role="dialog"`, `aria-modal="true"`, `aria-labelledby` | Tab trap within, ESC to close | Announces dialog title on open |
| Table | `aria-sort` on sortable headers | Arrow keys for cell nav | Row/column announced |
| Tabs | `role="tablist"`, `role="tab"`, `aria-selected` | Arrow keys between tabs, Tab to panel | Announces tab name + position |
| Calendar | `role="grid"`, `aria-label="Select date"` | Arrow keys for date nav | Announces date + availability |
| TimeSlotGrid | `role="radiogroup"`, `role="radio"`, `aria-checked` | Arrow keys between slots | Announces time + availability |
| ChatBubble | `aria-live="polite"` on container | N/A (read-only display) | New messages announced |
| DropZone | `role="button"`, `aria-label="Upload files"` | Enter/Space to open picker | "Drop files here or click" |
| Toggle | `role="switch"`, `aria-checked` | Space to toggle | "Setting: On/Off" |
| Toast | `role="alert"`, `aria-live` | Dismissible via close button | Auto-announced on appear |
| ConfidenceBadge | `aria-label="AI confidence: XX%"` | N/A (read-only) | Percentage announced |

## Design System Integration

**Design System Reference**: [designsystem.md](../docs/designsystem.md)

### Components Matching Design System

- [x] Button - Matches primary/secondary/error color tokens
- [x] TextField - Uses neutral/primary/error border tokens
- [x] Card - Uses elevation-1, radius.md, spacing.4 padding
- [x] Table - Uses neutral.100 header, body2 typography
- [x] Badge - Maps to semantic color tokens
- [x] Modal - Uses elevation-3, radius.lg
- [x] Toast - Uses semantic surface colors
- [x] Tabs - Uses primary.500 active indicator

### New Components to Add to Design System

- [ ] ChatBubble - Requires primary.100 (user) and neutral.100 (AI) surface tokens
- [ ] TypingIndicator - Requires CSS animation definition
- [ ] TimeSlotGrid - Requires slot sizing tokens and radio group semantics
- [ ] DropZone - Requires dashed border style token and drag-over state
- [ ] Timer - Requires color threshold mapping (success/warning/error by duration)
- [ ] ConfidenceBadge - Requires threshold-based color logic
