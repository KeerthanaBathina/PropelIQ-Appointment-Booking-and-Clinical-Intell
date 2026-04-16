# Design System - Unified Patient Access & Clinical Intelligence Platform

## 1. Design System Overview

**Platform**: Web (React 18 + TypeScript + Material-UI 5)
**Grid System**: 8px base grid
**Component Library**: MUI 5 (Material-UI)
**Icon Library**: Material Icons (outlined variant)

---

## 2. Design Tokens

### Color Palette

#### Primary Colors

```yaml
primary:
  50: "#E3F2FD"
  100: "#BBDEFB"
  200: "#90CAF9"
  300: "#64B5F6"
  400: "#42A5F5"
  500: "#1976D2"   # Primary brand color
  600: "#1565C0"
  700: "#0D47A1"
  800: "#0D3B8E"
  900: "#062E6F"
  contrast: "#FFFFFF"
```

#### Secondary Colors

```yaml
secondary:
  50: "#F3E5F5"
  100: "#E1BEE7"
  200: "#CE93D8"
  300: "#BA68C8"
  400: "#AB47BC"
  500: "#7B1FA2"   # Staff portal accent
  600: "#6A1B9A"
  700: "#4A148C"
  800: "#3E1278"
  900: "#2E0D5C"
  contrast: "#FFFFFF"
```

#### Semantic Colors

```yaml
success:
  light: "#81C784"
  main: "#2E7D32"
  dark: "#1B5E20"
  contrast: "#FFFFFF"
  surface: "#E8F5E9"

warning:
  light: "#FFB74D"
  main: "#ED6C02"
  dark: "#E65100"
  contrast: "#FFFFFF"
  surface: "#FFF3E0"

error:
  light: "#EF5350"
  main: "#D32F2F"
  dark: "#C62828"
  contrast: "#FFFFFF"
  surface: "#FFEBEE"

info:
  light: "#4FC3F7"
  main: "#0288D1"
  dark: "#01579B"
  contrast: "#FFFFFF"
  surface: "#E1F5FE"
```

#### Neutral Colors

```yaml
neutral:
  0: "#FFFFFF"
  50: "#FAFAFA"
  100: "#F5F5F5"
  200: "#EEEEEE"
  300: "#E0E0E0"
  400: "#BDBDBD"
  500: "#9E9E9E"
  600: "#757575"
  700: "#616161"
  800: "#424242"
  900: "#212121"
```

#### Appointment Status Colors

```yaml
appointment-status:
  scheduled: "#1976D2"   # Blue
  confirmed: "#0288D1"   # Light blue
  arrived: "#2E7D32"     # Green
  in-visit: "#7B1FA2"    # Purple
  completed: "#388E3C"   # Green (darker)
  cancelled: "#757575"   # Gray
  no-show: "#D32F2F"     # Red
  waitlisted: "#ED6C02"  # Orange
```

#### AI Confidence Colors

```yaml
confidence:
  high: "#2E7D32"       # >=80% - Green
  medium: "#ED6C02"     # 60-79% - Amber
  low: "#D32F2F"        # <60% - Red
```

### Typography

```yaml
font-family:
  primary: "'Roboto', 'Helvetica Neue', Arial, sans-serif"
  monospace: "'Roboto Mono', 'Courier New', monospace"

type-scale:
  h1:
    size: "2.125rem"     # 34px
    weight: 300
    line-height: 1.235
    letter-spacing: "-0.00735em"
    usage: "Page titles"
  h2:
    size: "1.5rem"       # 24px
    weight: 400
    line-height: 1.334
    letter-spacing: "0em"
    usage: "Section titles"
  h3:
    size: "1.25rem"      # 20px
    weight: 500
    line-height: 1.6
    letter-spacing: "0.0075em"
    usage: "Card titles, subsection headers"
  h4:
    size: "1.125rem"     # 18px
    weight: 500
    line-height: 1.5
    letter-spacing: "0.00714em"
    usage: "Sub-headers"
  subtitle1:
    size: "1rem"         # 16px
    weight: 500
    line-height: 1.75
    usage: "Table headers, field labels"
  subtitle2:
    size: "0.875rem"     # 14px
    weight: 500
    line-height: 1.57
    usage: "Secondary labels"
  body1:
    size: "1rem"         # 16px
    weight: 400
    line-height: 1.5
    usage: "Primary body text"
  body2:
    size: "0.875rem"     # 14px
    weight: 400
    line-height: 1.43
    usage: "Secondary body text, table cells"
  caption:
    size: "0.75rem"      # 12px
    weight: 400
    line-height: 1.66
    usage: "Helper text, timestamps"
  overline:
    size: "0.625rem"     # 10px
    weight: 500
    line-height: 2.66
    letter-spacing: "0.08333em"
    text-transform: "uppercase"
    usage: "Tags, status labels"
  button:
    size: "0.875rem"     # 14px
    weight: 500
    line-height: 1.75
    letter-spacing: "0.02857em"
    text-transform: "uppercase"
    usage: "Button labels"
```

### Spacing

```yaml
spacing-unit: "8px"

spacing:
  0: "0px"
  1: "4px"
  2: "8px"
  3: "12px"
  4: "16px"
  5: "20px"
  6: "24px"
  8: "32px"
  10: "40px"
  12: "48px"
  16: "64px"
  20: "80px"

component-spacing:
  card-padding: "16px"          # spacing.4
  section-gap: "24px"           # spacing.6
  form-field-gap: "16px"        # spacing.4
  page-margin-mobile: "16px"    # spacing.4
  page-margin-desktop: "24px"   # spacing.6
  sidebar-width: "256px"
  header-height: "64px"
```

### Border Radius

```yaml
radius:
  none: "0px"
  sm: "4px"
  md: "8px"
  lg: "12px"
  xl: "16px"
  full: "9999px"

component-radius:
  button: "4px"        # radius.sm
  card: "8px"          # radius.md
  input: "4px"         # radius.sm
  modal: "12px"        # radius.lg
  chip: "16px"         # radius.xl
  avatar: "9999px"     # radius.full
  badge: "9999px"      # radius.full
  toast: "8px"         # radius.md
```

### Elevation / Shadows

```yaml
elevation:
  0: "none"
  1: "0px 1px 3px rgba(0,0,0,0.12), 0px 1px 2px rgba(0,0,0,0.24)"
  2: "0px 3px 6px rgba(0,0,0,0.16), 0px 3px 6px rgba(0,0,0,0.23)"
  3: "0px 10px 20px rgba(0,0,0,0.19), 0px 6px 6px rgba(0,0,0,0.23)"
  4: "0px 14px 28px rgba(0,0,0,0.25), 0px 10px 10px rgba(0,0,0,0.22)"

component-elevation:
  card: 1
  header: 2
  sidebar: 1
  modal: 3
  drawer: 2
  dropdown: 2
  toast: 3
  fab: 2
```

### Z-Index Scale

```yaml
z-index:
  dropdown: 1000
  sticky: 1020
  fixed: 1030
  modal-backdrop: 1040
  modal: 1050
  popover: 1060
  tooltip: 1070
  toast: 1080
```

### Transitions

```yaml
transitions:
  duration:
    shortest: "150ms"
    short: "200ms"
    standard: "300ms"
    complex: "375ms"
  easing:
    ease-in-out: "cubic-bezier(0.4, 0, 0.2, 1)"
    ease-out: "cubic-bezier(0.0, 0, 0.2, 1)"
    ease-in: "cubic-bezier(0.4, 0, 1, 1)"
    sharp: "cubic-bezier(0.4, 0, 0.6, 1)"
```

---

## 3. Breakpoints

```yaml
breakpoints:
  xs: "0px"
  sm: "600px"
  md: "900px"
  lg: "1200px"
  xl: "1536px"

layout:
  mobile: "320px - 599px"     # xs
  tablet: "600px - 899px"     # sm
  desktop-sm: "900px - 1199px"  # md
  desktop: "1200px - 1535px"    # lg
  desktop-lg: "1536px+"         # xl

grid:
  columns: 12
  gutter: "16px"
  margin-mobile: "16px"
  margin-tablet: "24px"
  margin-desktop: "24px"
  max-content-width: "1440px"
```

---

## 4. Component Specifications

### Actions

#### Button

| Variant | Background | Text Color | Border | Usage |
|---------|-----------|------------|--------|-------|
| Primary (Contained) | primary.500 | #FFFFFF | none | Main CTA (Book Appointment, Save, Confirm) |
| Secondary (Outlined) | transparent | primary.500 | 1px primary.500 | Secondary actions (Cancel, Back) |
| Tertiary (Text) | transparent | primary.500 | none | Inline actions (Learn More, View Details) |
| Error (Contained) | error.main | #FFFFFF | none | Destructive actions (Delete, Deactivate) |
| Disabled | neutral.200 | neutral.500 | none | Inactive state |

**Sizes**: Small (30px height), Medium (36px height), Large (42px height)
**States**: Default, Hover (+8% opacity overlay), Active (+16%), Focus (2px outline), Disabled (40% opacity), Loading (spinner replaces label)
**Min Width**: 64px
**Padding**: 8px 16px (medium)
**ARIA**: `role="button"`, `aria-disabled` when disabled, `aria-busy` when loading

#### IconButton

- Sizes: Small (34px), Medium (40px), Large (48px)
- Touch target (mobile): min 44x44px
- States: Same as Button

#### Link

- Color: primary.500
- Underline: On hover
- Visited: primary.700
- Focus: 2px outline
- ARIA: Uses `<a>` semantics

### Inputs

#### TextField

| State | Border | Label Color | Helper Text | Icon |
|-------|--------|-------------|-------------|------|
| Default | neutral.400 | neutral.600 | neutral.500 | neutral.500 |
| Hover | neutral.800 | neutral.600 | neutral.500 | neutral.500 |
| Focused | primary.500 (2px) | primary.500 | neutral.500 | primary.500 |
| Error | error.main (2px) | error.main | error.main | error.main |
| Disabled | neutral.300 | neutral.400 | neutral.400 | neutral.400 |
| Read-only | neutral.300 | neutral.600 | neutral.500 | neutral.500 |

**Sizes**: Small (40px), Medium (48px)
**Padding**: 12px 16px
**Helper text**: caption typography below the field
**ARIA**: `aria-invalid`, `aria-describedby` linked to helper text, `aria-required`

#### Select

- Same states as TextField
- Dropdown panel: elevation-2, max-height 300px, scroll
- ARIA: `role="combobox"`, `aria-expanded`, `aria-activedescendant`

#### Checkbox / Radio

- Size: 24x24 (icon) + label
- Touch target: 44px minimum
- States: Unchecked, Checked, Indeterminate (checkbox only), Disabled
- Color: primary.500 (checked), neutral.400 (unchecked)

#### Toggle (Switch)

- Width: 34px, Height: 20px
- Track: neutral.300 (off), primary.500 (on)
- Thumb: #FFFFFF
- ARIA: `role="switch"`, `aria-checked`

#### DatePicker

- Based on MUI DatePicker
- Format: MM/DD/YYYY
- Range: 90 days forward (booking), no limit (DOB)
- Calendar popup: elevation-3

### Navigation

#### Header (AppBar)

- Height: 64px
- Background: #FFFFFF
- Elevation: 2
- Logo (left), Search (center on staff), User Menu (right)
- Mobile: Hamburger left, Logo center

#### Sidebar

- Width: 256px (expanded), 72px (collapsed), 0px (mobile)
- Background: neutral.50
- Elevation: 1
- Active item: primary.50 background, primary.500 text + left accent bar (3px)
- Mobile: Full-screen overlay with backdrop

#### Tabs

- Variant: Underlined
- Active: primary.500 text, 2px bottom border
- Inactive: neutral.600 text
- Hover: primary.50 background
- ARIA: `role="tablist"`, `role="tab"`, `aria-selected`

#### Breadcrumb

- Separator: "/"
- Current page: neutral.900, no link
- Parent pages: primary.500, linked
- Max items: 3 visible + collapse ellipsis

#### Pagination

- Variant: MUI default (numbered pages)
- Show first/last, prev/next
- Max visible pages: 5

### Content

#### Card

- Background: #FFFFFF
- Border: none (elevation 1) or 1px neutral.200 (flat variant)
- Padding: 16px
- Radius: 8px
- Hover: elevation-2 (if clickable)
- ARIA: `role="article"` or `role="button"` if clickable

#### Table

- Header: neutral.100 background, subtitle1 typography
- Row: body2 typography, 52px min height
- Stripe: alternate rows neutral.50
- Hover: neutral.100
- Selected: primary.50
- Border: 1px neutral.200 between rows
- Sortable columns: Sort icon in header, `aria-sort`
- Empty: Illustration + message + CTA

#### Badge

| Type | Background | Text | Usage |
|------|-----------|------|-------|
| Default | neutral.200 | neutral.800 | Counts, labels |
| Primary | primary.500 | #FFFFFF | Notification count |
| Success | success.main | #FFFFFF | Completed status |
| Warning | warning.main | #FFFFFF | Pending, needs review |
| Error | error.main | #FFFFFF | Failed, cancelled |
| Info | info.main | #FFFFFF | Informational |

- Pill shape: radius.full
- Padding: 2px 8px
- Typography: overline

#### Chip

- Height: 32px
- Radius: 16px
- Deletable: X icon on right
- Selectable: Outlined (unselected) -> Filled (selected)

#### Avatar

- Sizes: Small (32px), Medium (40px), Large (56px)
- Fallback: Initials on primary.100 background
- Shape: Circle (radius.full)

#### Tooltip

- Background: neutral.800
- Text: #FFFFFF, caption typography
- Delay: 300ms show, 150ms hide
- Max width: 300px
- Position: Top (default), auto-flip

### Feedback

#### Modal / Dialog

- Overlay: rgba(0,0,0,0.5)
- Background: #FFFFFF
- Radius: 12px
- Elevation: 3
- Width: 400px (small), 600px (medium), 800px (large)
- Focus trap: Enforced
- Escape to close: Enabled
- ARIA: `role="dialog"`, `aria-modal="true"`, `aria-labelledby`

#### Drawer

- Side: Right
- Width: 400px (desktop), 100% (mobile)
- Overlay: rgba(0,0,0,0.5)
- Elevation: 2

#### Toast (Snackbar)

- Position: Bottom-center (mobile), Bottom-right (desktop)
- Duration: 5000ms (auto-dismiss), persistent for errors
- Types: Success (green), Error (red), Warning (amber), Info (blue)
- Radius: 8px
- Elevation: 3
- Close button (X) always visible
- ARIA: `role="alert"`, `aria-live="polite"` (info), `aria-live="assertive"` (error)

#### Alert (Banner)

- Full-width container within content area
- Types: Info, Success, Warning, Error
- Background: Corresponding semantic surface color
- Icon: Left-aligned
- Close button: Optional
- ARIA: `role="alert"`

#### Skeleton

- Background: neutral.200
- Animation: Pulse (opacity 0.4 -> 1.0, 1.5s)
- Shapes: Rectangle, Circle, Text line
- Match target component dimensions

#### ProgressBar

- Height: 4px (linear)
- Color: primary.500
- Background: neutral.200
- Variants: Determinate (with %), Indeterminate (animation)
- ARIA: `role="progressbar"`, `aria-valuenow`, `aria-valuemin`, `aria-valuemax`

#### Spinner

- Sizes: Small (20px), Medium (40px), Large (56px)
- Color: primary.500
- Used for: Button loading, inline loading
- ARIA: `aria-busy="true"` on parent container

### Specialized Healthcare Components

#### ChatBubble

- User bubble: primary.100 background, right-aligned
- AI bubble: neutral.100 background, left-aligned
- Padding: 12px 16px
- Radius: 12px (rounded, flat corner on origin side)
- Max width: 80% of container
- Timestamp: caption typography below bubble

#### TypingIndicator

- Three dots animation (bounce)
- Inside AI-style bubble
- Color: neutral.500

#### Calendar (Booking)

- MUI DateCalendar base
- Available dates: primary.500 dot indicator
- Selected date: primary.500 fill
- Unavailable: neutral.300 text, no interaction
- Today: Outlined circle

#### TimeSlotGrid

- Grid layout: 3-4 columns (desktop), 2 columns (mobile)
- Slot chip: Outlined (available), Filled primary (selected), Filled neutral.200 (unavailable)
- Size: 80px x 40px per slot
- ARIA: `role="radiogroup"`, each slot `role="radio"`

#### DropZone

- Border: 2px dashed neutral.400 (default), primary.500 (drag-over)
- Background: neutral.50 (default), primary.50 (drag-over)
- Height: 200px
- Icon: Upload icon centered
- Text: "Drag files here or click to browse"
- Accepts: PDF, TIFF, PNG, JPEG (per spec.md)

#### FileList

- Item height: 56px
- Icon: File type icon (left)
- Name + size (center)
- Status badge + action button (right)
- Processing: ProgressBar below item

#### Timer (Wait Time)

- Format: MM:SS or HH:MM:SS
- Color: success.main (<15 min), warning.main (15-30 min), error.main (>30 min)
- Typography: h4

#### ConfidenceBadge

- Pill shape
- High (>=80%): success.main background
- Medium (60-79%): warning.main background
- Low (<60%): error.main background
- Text: White, percentage value
- ARIA: `aria-label="AI confidence: XX%"`

---

## 5. States Specification

Every screen supports these five states:

| State | Visual Treatment | Content |
|-------|-----------------|---------|
| Default | Normal rendering | Real data displayed |
| Loading | Skeleton screens | Skeleton placeholders matching final layout |
| Empty | Illustration + message + CTA | Encouraging copy, primary action button |
| Error | Alert banner + retry | Actionable error message, retry button |
| Validation | Inline field errors | Red borders, helper text, summary at top |

### State-Specific Rules

- **Loading**: Shown when data fetch exceeds 300ms; skeleton shapes match component dimensions
- **Empty**: Different messages per screen (e.g., "No appointments yet" vs "No documents uploaded")
- **Error**: Always includes recovery action; API errors show user-friendly message (never raw error)
- **Validation**: Errors appear on blur (not on every keystroke); scroll to first error on submit

---

## 6. Accessibility Standards

### WCAG 2.1 Level AA Compliance

| Criterion | Requirement | Implementation |
|-----------|-------------|----------------|
| 1.1.1 Non-text Content | Alt text for images | All images have `alt`, decorative images use `alt=""` |
| 1.3.1 Info and Relationships | Semantic HTML | Proper heading hierarchy, landmarks, form labels |
| 1.4.3 Contrast (Minimum) | 4.5:1 normal, 3:1 large | All text meets contrast ratios per color palette |
| 1.4.11 Non-text Contrast | 3:1 for UI components | Borders, focus rings, icons meet contrast |
| 2.1.1 Keyboard | All functionality via keyboard | Tab order logical, Enter/Space for actions |
| 2.4.3 Focus Order | Logical focus sequence | DOM order matches visual order |
| 2.4.7 Focus Visible | Visible focus indicator | 2px primary.500 outline, 2px offset |
| 4.1.2 Name, Role, Value | ARIA labels | All interactive elements have accessible names |

### Focus Management

```yaml
focus-ring:
  color: primary.500
  width: "2px"
  style: "solid"
  offset: "2px"
```

---

## 7. Visual Validation Criteria

| Criterion | Pass Condition |
|-----------|---------------|
| Color contrast | All text passes WCAG 2.1 AA (4.5:1 normal, 3:1 large) |
| Touch targets | All interactive elements >= 44x44px on mobile |
| Typography scale | No font sizes below 12px (caption) |
| Spacing consistency | All spacing values align to 8px grid (4px for half-step) |
| Component states | All interactive components implement 6 states minimum |
| Breakpoint coverage | All screens render correctly at 320px, 768px, 1024px, 1440px |
| Status color coding | Appointment statuses use consistent palette cross-screen |
| AI confidence | Color-coded badges used consistently across SCR-012, SCR-013, SCR-014 |
