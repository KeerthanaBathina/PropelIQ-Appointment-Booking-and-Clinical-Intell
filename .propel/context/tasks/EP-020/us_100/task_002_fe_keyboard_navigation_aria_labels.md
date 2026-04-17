# Task - task_002_fe_keyboard_navigation_aria_labels

## Requirement Reference

- User Story: us_100
- Story Location: .propel/context/tasks/EP-020/us_100/us_100.md
- Acceptance Criteria:
  - AC-2: Given any interactive element exists, When a user navigates via keyboard, Then all elements are reachable via Tab key with logical tab order, and actions are triggerable via Enter/Space.
  - AC-3: Given form inputs and buttons exist, When a screen reader reads them, Then all elements have descriptive ARIA labels that announce the element's purpose correctly.
- Edge Case:
  - What happens when third-party components (MUI) have accessibility gaps? Custom ARIA attributes override MUI defaults; known gaps are documented with planned fixes.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-001-login.html` through `wireframe-SCR-009-manual-intake.html` (all patient-facing screens) |
| **Screen Spec** | figma_spec.md — SCR-001 through SCR-009 |
| **UXR Requirements** | UXR-202, UXR-203, UXR-205 |
| **Design Tokens** | designsystem.md#actions, designsystem.md#inputs, designsystem.md#navigation |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Frontend | React | 18.x |
| UI Component Library | Material-UI (MUI) | 5.x |
| State Management | Zustand | 4.x |

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

Implement keyboard navigation infrastructure and ARIA labeling patterns across all patient-facing screens (SCR-001 through SCR-009), satisfying AC-2 (full keyboard reachability with logical tab order) and AC-3 (descriptive ARIA labels for screen readers). This task builds three reusable accessibility components: a `SkipToContent` link for bypassing navigation, a `FocusTrap` wrapper for modal/dialog keyboard containment, and an `AriaLabelProvider` pattern for consistent ARIA attribute application across MUI components with known gaps. The implementation ensures every interactive element — buttons, form fields, links, tabs, and menus — is reachable via Tab, activatable via Enter/Space, and correctly announced by assistive technologies (NVDA, JAWS, VoiceOver). MUI components with documented accessibility gaps (edge case 1) receive custom ARIA overrides via wrapper components or `slotProps`.

## Dependent Tasks

- task_001_fe_accessible_theme_contrast_focus — Requires AA-compliant theme with focus indicator styles.
- US_002 — Requires frontend React scaffold with MUI.

## Impacted Components

- **CREATE** `app/src/components/accessibility/SkipToContent.tsx` — Skip navigation link for keyboard users
- **CREATE** `app/src/components/accessibility/FocusTrap.tsx` — Keyboard focus containment for modals/drawers
- **CREATE** `app/src/components/accessibility/AccessibleSelect.tsx` — MUI Select wrapper with ARIA fixes
- **CREATE** `app/src/components/accessibility/AccessibleDatePicker.tsx` — MUI DatePicker wrapper with ARIA fixes
- **CREATE** `app/src/components/accessibility/AccessibleAutocomplete.tsx` — MUI Autocomplete wrapper with ARIA fixes
- **MODIFY** `app/src/App.tsx` — Add SkipToContent link and main content landmark
- **MODIFY** `app/src/components/layout/Sidebar.tsx` — Add keyboard navigation and ARIA attributes to navigation

## Implementation Plan

1. **Implement `SkipToContent` component (AC-2, UXR-202)**: Create `app/src/components/accessibility/SkipToContent.tsx`:
   ```tsx
   interface SkipToContentProps {
     targetId?: string;
     label?: string;
   }

   export const SkipToContent: React.FC<SkipToContentProps> = ({
     targetId = 'main-content',
     label = 'Skip to main content',
   }) => {
     const handleClick = (e: React.MouseEvent<HTMLAnchorElement>) => {
       e.preventDefault();
       const target = document.getElementById(targetId);
       if (target) {
         target.focus();
         target.scrollIntoView({ behavior: 'smooth' });
       }
     };

     return (
       <a
         href={`#${targetId}`}
         onClick={handleClick}
         sx={{
           position: 'absolute',
           left: '-9999px',
           top: 'auto',
           width: '1px',
           height: '1px',
           overflow: 'hidden',
           '&:focus': {
             position: 'fixed',
             top: '8px',
             left: '8px',
             width: 'auto',
             height: 'auto',
             padding: '12px 24px',
             backgroundColor: '#1976D2',
             color: '#FFFFFF',
             zIndex: 9999,
             fontSize: '1rem',
             fontWeight: 500,
             borderRadius: '4px',
             outline: '2px solid #0D47A1',
             outlineOffset: '2px',
             textDecoration: 'none',
           },
         }}
       >
         {label}
       </a>
     );
   };
   ```
   Key behaviors:
   - **Visually hidden** by default — positioned off-screen via `left: -9999px`.
   - **Visible on focus** — appears at top-left when user presses Tab as the first focusable element.
   - **Navigates to `#main-content`** — sets focus and scrolls to the main content area, skipping the header, sidebar, and navigation.
   - Uses `<a>` semantics for native keyboard support (Enter activates, Tab reaches it).
   - Styled with primary brand color (#1976D2) and WCAG AA contrast text (#FFFFFF).

2. **Implement `FocusTrap` component (AC-2)**: Create `app/src/components/accessibility/FocusTrap.tsx`:
   ```tsx
   interface FocusTrapProps {
     children: React.ReactNode;
     active: boolean;
     restoreFocus?: boolean;
   }

   export const FocusTrap: React.FC<FocusTrapProps> = ({
     children,
     active,
     restoreFocus = true,
   }) => {
     const containerRef = useRef<HTMLDivElement>(null);
     const previousFocusRef = useRef<HTMLElement | null>(null);

     useEffect(() => {
       if (!active || !containerRef.current) return;

       previousFocusRef.current = document.activeElement as HTMLElement;

       const focusableSelector =
         'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
         'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

       const focusableElements = containerRef.current.querySelectorAll(focusableSelector);
       const firstFocusable = focusableElements[0] as HTMLElement;
       const lastFocusable = focusableElements[focusableElements.length - 1] as HTMLElement;

       firstFocusable?.focus();

       const handleKeyDown = (e: KeyboardEvent) => {
         if (e.key !== 'Tab') return;

         if (e.shiftKey && document.activeElement === firstFocusable) {
           e.preventDefault();
           lastFocusable?.focus();
         } else if (!e.shiftKey && document.activeElement === lastFocusable) {
           e.preventDefault();
           firstFocusable?.focus();
         }
       };

       containerRef.current.addEventListener('keydown', handleKeyDown);
       const container = containerRef.current;

       return () => {
         container.removeEventListener('keydown', handleKeyDown);
         if (restoreFocus) {
           previousFocusRef.current?.focus();
         }
       };
     }, [active, restoreFocus]);

     return <div ref={containerRef}>{children}</div>;
   };
   ```
   Key behaviors:
   - **Tab cycles within container** — pressing Tab on the last element wraps to the first; Shift+Tab on first wraps to last.
   - **Auto-focuses first element** on activation — user immediately starts interacting with modal content.
   - **Restores focus** on deactivation — focus returns to the element that opened the modal (AC-2 requirement for logical tab flow).
   - Used by MUI `Dialog`, `Drawer`, and custom modal components.
   - Note: MUI Dialog has built-in focus trap via `disableEnforceFocus={false}` (default). This custom `FocusTrap` is for any non-MUI overlay components.

3. **Create `AccessibleSelect` wrapper (AC-3, edge case 1)**: Create `app/src/components/accessibility/AccessibleSelect.tsx`:
   ```tsx
   interface AccessibleSelectProps extends SelectProps {
     ariaLabel: string;
     ariaRequired?: boolean;
     helperTextId?: string;
   }

   export const AccessibleSelect: React.FC<AccessibleSelectProps> = ({
     ariaLabel,
     ariaRequired = false,
     helperTextId,
     ...props
   }) => {
     return (
       <Select
         {...props}
         inputProps={{
           'aria-label': ariaLabel,
           'aria-required': ariaRequired,
           'aria-describedby': helperTextId,
           ...props.inputProps,
         }}
       />
     );
   };
   ```
   Fixes MUI gap: native `<select>` rendered by MUI lacks `aria-required` and `aria-label` attributes. This wrapper injects them via `inputProps`.

4. **Create `AccessibleDatePicker` wrapper (AC-3, edge case 1)**: Create `app/src/components/accessibility/AccessibleDatePicker.tsx`:
   ```tsx
   interface AccessibleDatePickerProps extends DatePickerProps<Dayjs> {
     ariaLabel: string;
     calendarAriaLabel?: string;
   }

   export const AccessibleDatePicker: React.FC<AccessibleDatePickerProps> = ({
     ariaLabel,
     calendarAriaLabel = 'Choose date',
     ...props
   }) => {
     return (
       <DatePicker
         {...props}
         slotProps={{
           ...props.slotProps,
           textField: {
             ...props.slotProps?.textField,
             inputProps: {
               'aria-label': ariaLabel,
             },
           },
           openPickerButton: {
             'aria-label': calendarAriaLabel,
           },
           popper: {
             role: 'dialog',
             'aria-label': calendarAriaLabel,
           },
         }}
       />
     );
   };
   ```
   Fixes MUI gap: DatePicker's calendar popup lacks `aria-label`, and the open button has no descriptive label. This wrapper adds both via `slotProps`.

5. **Create `AccessibleAutocomplete` wrapper (AC-3, edge case 1)**: Create `app/src/components/accessibility/AccessibleAutocomplete.tsx`:
   ```tsx
   interface AccessibleAutocompleteProps<T> extends AutocompleteProps<T, boolean, boolean, boolean> {
     listboxAriaLabel: string;
   }

   export function AccessibleAutocomplete<T>({
     listboxAriaLabel,
     ...props
   }: AccessibleAutocompleteProps<T>) {
     return (
       <Autocomplete
         {...props}
         ListboxProps={{
           ...props.ListboxProps,
           'aria-label': listboxAriaLabel,
         }}
       />
     );
   }
   ```
   Fixes MUI gap: Autocomplete's dropdown listbox lacks `aria-label`. Screen readers announce "list" without context. This wrapper adds a descriptive label (e.g., "Provider search results").

6. **Add landmark regions and ARIA attributes to layout (AC-3, UXR-203)**: Update `app/src/App.tsx` and layout components:
   ```tsx
   // App.tsx
   <SkipToContent />
   <header role="banner" aria-label="Site header">
     <AppBar />
   </header>
   <nav role="navigation" aria-label="Main navigation">
     <Sidebar />
   </nav>
   <main id="main-content" role="main" tabIndex={-1} aria-label="Main content">
     <Outlet />
   </main>
   ```
   - **`role="banner"`** on header — screen readers announce "banner" landmark.
   - **`role="navigation"`** on sidebar — screen readers announce "navigation" landmark.
   - **`role="main"`** with `id="main-content"` — target for SkipToContent link.
   - **`tabIndex={-1}`** on `<main>` — allows programmatic focus via SkipToContent without appearing in normal tab order.
   - All landmarks have `aria-label` for differentiation when multiple landmarks exist.

7. **Add keyboard navigation to Sidebar (AC-2, UXR-202)**: Update sidebar navigation to support arrow key navigation:
   ```tsx
   // Sidebar.tsx
   const handleKeyDown = (e: React.KeyboardEvent, index: number) => {
     const items = document.querySelectorAll('[role="menuitem"]');
     let targetIndex = index;

     switch (e.key) {
       case 'ArrowDown':
         e.preventDefault();
         targetIndex = (index + 1) % items.length;
         break;
       case 'ArrowUp':
         e.preventDefault();
         targetIndex = (index - 1 + items.length) % items.length;
         break;
       case 'Home':
         e.preventDefault();
         targetIndex = 0;
         break;
       case 'End':
         e.preventDefault();
         targetIndex = items.length - 1;
         break;
       default:
         return;
     }

     (items[targetIndex] as HTMLElement).focus();
   };

   // Each nav item:
   <ListItem
     role="menuitem"
     tabIndex={isActive ? 0 : -1}
     onKeyDown={(e) => handleKeyDown(e, index)}
     aria-current={isActive ? 'page' : undefined}
   >
     <ListItemIcon aria-hidden="true">
       <DashboardIcon />
     </ListItemIcon>
     <ListItemText primary="Dashboard" />
   </ListItem>
   ```
   - **Arrow key navigation** within the sidebar menu (Up/Down cycles, Home/End jumps).
   - **Roving tabindex** — only the active item has `tabIndex={0}`; others have `tabIndex={-1}`. Tab enters the menu at the active item, then arrow keys navigate.
   - **`aria-current="page"`** on the active nav item — screen readers announce "current page".
   - **`aria-hidden="true"`** on decorative icons — prevents screen readers from announcing icon names.

8. **Establish ARIA labeling patterns for form components (AC-3)**: Define reusable patterns for all form inputs across patient-facing screens:
   ```tsx
   // Pattern 1: TextField with aria-describedby for helper/error text
   <TextField
     id="patient-email"
     label="Email address"
     inputProps={{
       'aria-required': true,
       'aria-describedby': 'patient-email-helper',
     }}
     helperText={<span id="patient-email-helper">Enter your registered email</span>}
   />

   // Pattern 2: Button with descriptive aria-label
   <Button
     aria-label="Book appointment for Dr. Smith on March 15"
     onClick={handleBook}
   >
     Book
   </Button>

   // Pattern 3: IconButton with aria-label (always required)
   <IconButton aria-label="Close dialog">
     <CloseIcon />
   </IconButton>

   // Pattern 4: Checkbox group with fieldset/legend
   <FormControl component="fieldset">
     <FormLabel component="legend">Notification preferences</FormLabel>
     <FormGroup>
       <FormControlLabel
         control={<Checkbox />}
         label="Email reminders"
       />
       <FormControlLabel
         control={<Checkbox />}
         label="SMS reminders"
       />
     </FormGroup>
   </FormControl>
   ```
   Key ARIA patterns:
   - Every `TextField` links to its helper text via `aria-describedby`.
   - Every `IconButton` has an explicit `aria-label` (icons alone are not announced).
   - Checkbox/Radio groups use `<fieldset>` and `<legend>` semantics via MUI's `FormControl` and `FormLabel`.
   - Buttons with generic text ("Book", "Delete") use `aria-label` to provide context.
   - These patterns are documented as team conventions; all new patient-facing components must follow them.

## Current Project State

```text
UPACIP/
├── app/
│   ├── package.json
│   ├── tsconfig.json
│   ├── .eslintrc.js
│   ├── src/
│   │   ├── App.tsx
│   │   ├── main.tsx
│   │   ├── theme/
│   │   │   ├── theme.ts                         ← from task_001
│   │   │   └── contrastUtils.ts                 ← from task_001
│   │   ├── components/
│   │   │   ├── layout/
│   │   │   │   ├── Sidebar.tsx
│   │   │   │   ├── Header.tsx
│   │   │   │   └── Layout.tsx
│   │   │   └── common/
│   │   ├── pages/
│   │   ├── hooks/
│   │   ├── stores/
│   │   └── utils/
│   └── public/
└── .propel/
    └── context/
        ├── wireframes/Hi-Fi/
        └── docs/
```

> Assumes US_002 (frontend React scaffold) and task_001 (accessible theme) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/components/accessibility/SkipToContent.tsx | Skip-to-content link, visually hidden until focused |
| CREATE | app/src/components/accessibility/FocusTrap.tsx | Keyboard focus containment for modals and overlays |
| CREATE | app/src/components/accessibility/AccessibleSelect.tsx | MUI Select wrapper with aria-required, aria-label fixes |
| CREATE | app/src/components/accessibility/AccessibleDatePicker.tsx | MUI DatePicker wrapper with calendar aria-label |
| CREATE | app/src/components/accessibility/AccessibleAutocomplete.tsx | MUI Autocomplete wrapper with listbox aria-label |
| MODIFY | app/src/App.tsx | Add SkipToContent, landmark roles (banner, navigation, main) |
| MODIFY | app/src/components/layout/Sidebar.tsx | Arrow key navigation, roving tabindex, aria-current |

## External References

- [WCAG 2.1 — Bypass Blocks (2.4.1)](https://www.w3.org/WAI/WCAG21/Understanding/bypass-blocks.html)
- [WCAG 2.1 — Focus Order (2.4.3)](https://www.w3.org/WAI/WCAG21/Understanding/focus-order.html)
- [WCAG 2.1 — Name, Role, Value (4.1.2)](https://www.w3.org/WAI/WCAG21/Understanding/name-role-value.html)
- [WAI-ARIA Authoring Practices — Menu Pattern](https://www.w3.org/WAI/ARIA/apg/patterns/menubar/)
- [WAI-ARIA Authoring Practices — Landmarks](https://www.w3.org/WAI/ARIA/apg/practices/landmark-regions/)
- [MUI 5 — Accessibility](https://mui.com/material-ui/getting-started/accessibility/)
- [MUI 5 — Select API](https://mui.com/material-ui/api/select/)
- [MUI 5 — DatePicker slotProps](https://mui.com/x/api/date-pickers/date-picker/)

## Build Commands

```powershell
# Build frontend
cd app; npm run build

# Run ESLint with a11y rules
cd app; npx eslint src/components/accessibility/ --ext .ts,.tsx

# Run dev server (test keyboard navigation manually)
cd app; npm run dev
```

## Implementation Validation Strategy

- [ ] `npm run build` completes with zero errors
- [ ] SkipToContent link visible on first Tab press, navigates to #main-content
- [ ] All interactive elements reachable via Tab in logical order (AC-2)
- [ ] All actions triggerable via Enter or Space key (AC-2)
- [ ] FocusTrap cycles focus within modal dialogs on Tab (AC-2)
- [ ] FocusTrap restores focus to trigger element on close (AC-2)
- [ ] All form inputs have descriptive ARIA labels announced by screen readers (AC-3)
- [ ] IconButtons have explicit aria-label attributes (AC-3)
- [ ] Sidebar supports ArrowUp/ArrowDown navigation (AC-2)
- [ ] AccessibleSelect, AccessibleDatePicker, AccessibleAutocomplete pass ARIA audit (edge case 1)
- [ ] Visual comparison against wireframes completed at 375px, 768px, 1440px
- [ ] Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [ ] Create SkipToContent component (visually hidden, visible on focus, navigates to main)
- [ ] Create FocusTrap component (Tab cycling, auto-focus first, restore focus on close)
- [ ] Create AccessibleSelect wrapper with aria-required and aria-label via inputProps
- [ ] Create AccessibleDatePicker wrapper with calendar aria-label via slotProps
- [ ] Create AccessibleAutocomplete wrapper with listbox aria-label
- [ ] Add landmark roles (banner, navigation, main) and SkipToContent to App.tsx
- [ ] Add arrow key navigation and roving tabindex to Sidebar
- [ ] Reference wireframes from Design References table during implementation
