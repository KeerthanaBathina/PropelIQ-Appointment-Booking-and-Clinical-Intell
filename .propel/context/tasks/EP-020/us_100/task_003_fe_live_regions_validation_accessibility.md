# Task - task_003_fe_live_regions_validation_accessibility

## Requirement Reference

- User Story: us_100
- Story Location: .propel/context/tasks/EP-020/us_100/us_100.md
- Acceptance Criteria:
  - AC-5: Given inline validation fires, When the user enters invalid data, Then the error message appears within 200ms and is announced by screen readers via ARIA live regions.
- Edge Case:
  - How does the system handle accessibility for dynamic content (AI responses, queue updates)? ARIA live regions announce changes; screen reader users receive the same real-time updates as sighted users.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | Yes |
| **Figma URL** | N/A |
| **Wireframe Status** | AVAILABLE |
| **Wireframe Type** | HTML |
| **Wireframe Path/URL** | `.propel/context/wireframes/Hi-Fi/wireframe-SCR-003-registration.html`, `wireframe-SCR-008-ai-intake.html`, `wireframe-SCR-009-manual-intake.html` |
| **Screen Spec** | figma_spec.md — SCR-003, SCR-008, SCR-009, SCR-011 |
| **UXR Requirements** | UXR-206, UXR-501 |
| **Design Tokens** | designsystem.md#inputs, designsystem.md#semantic-colors |

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

Implement ARIA live regions for dynamic content announcements and accessible inline validation across all patient-facing forms, satisfying AC-5 (error messages appear within 200ms and are announced by screen readers) and edge case 2 (AI responses, queue updates announced via live regions). This task creates a `LiveAnnouncer` context/provider that maintains a visually hidden ARIA live region at the application root, enabling any component to programmatically announce messages to screen readers. It also creates an `AccessibleFormField` pattern that combines MUI's `TextField` with `aria-invalid`, `aria-describedby`, and `aria-live="assertive"` for error messages — ensuring validation feedback is both visible and audible within the 200ms threshold (NFR-048). Dynamic content updates (AI conversational responses on SCR-008, queue position changes on SCR-011) use `aria-live="polite"` regions so screen readers announce them without interrupting the user's current task.

## Dependent Tasks

- task_001_fe_accessible_theme_contrast_focus — Requires AA-compliant theme with error color tokens.
- task_002_fe_keyboard_navigation_aria_labels — Requires ARIA labeling patterns and landmark structure.
- US_002 — Requires frontend React scaffold with MUI.

## Impacted Components

- **CREATE** `app/src/components/accessibility/LiveAnnouncer.tsx` — Context provider + visually hidden aria-live region
- **CREATE** `app/src/hooks/useLiveAnnouncer.ts` — Hook to announce messages from any component
- **CREATE** `app/src/components/forms/AccessibleFormField.tsx` — MUI TextField wrapper with inline validation + ARIA error
- **CREATE** `app/src/components/accessibility/DynamicContentRegion.tsx` — Wrapper for AI/queue content with aria-live="polite"
- **MODIFY** `app/src/App.tsx` — Wrap application in LiveAnnouncerProvider
- **MODIFY** `app/src/components/forms/` — Apply AccessibleFormField pattern to existing forms

## Implementation Plan

1. **Implement `LiveAnnouncer` context provider (AC-5, edge case 2, UXR-206)**: Create `app/src/components/accessibility/LiveAnnouncer.tsx`:
   ```tsx
   interface LiveAnnouncerContextType {
     announce: (message: string, priority?: 'polite' | 'assertive') => void;
   }

   const LiveAnnouncerContext = createContext<LiveAnnouncerContextType | undefined>(undefined);

   export const LiveAnnouncerProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
     const [politeMessage, setPoliteMessage] = useState('');
     const [assertiveMessage, setAssertiveMessage] = useState('');

     const announce = useCallback((message: string, priority: 'polite' | 'assertive' = 'polite') => {
       if (priority === 'assertive') {
         setAssertiveMessage('');
         // Force re-render by clearing first, then setting after a microtask
         requestAnimationFrame(() => setAssertiveMessage(message));
       } else {
         setPoliteMessage('');
         requestAnimationFrame(() => setPoliteMessage(message));
       }
     }, []);

     const contextValue = useMemo(() => ({ announce }), [announce]);

     return (
       <LiveAnnouncerContext.Provider value={contextValue}>
         {children}
         {/* Visually hidden live regions at application root */}
         <div
           aria-live="polite"
           aria-atomic="true"
           role="status"
           style={{
             position: 'absolute',
             width: '1px',
             height: '1px',
             padding: 0,
             margin: '-1px',
             overflow: 'hidden',
             clip: 'rect(0, 0, 0, 0)',
             whiteSpace: 'nowrap',
             border: 0,
           }}
         >
           {politeMessage}
         </div>
         <div
           aria-live="assertive"
           aria-atomic="true"
           role="alert"
           style={{
             position: 'absolute',
             width: '1px',
             height: '1px',
             padding: 0,
             margin: '-1px',
             overflow: 'hidden',
             clip: 'rect(0, 0, 0, 0)',
             whiteSpace: 'nowrap',
             border: 0,
           }}
         >
           {assertiveMessage}
         </div>
       </LiveAnnouncerContext.Provider>
     );
   };
   ```
   Key design decisions:
   - **Two separate regions** — `polite` for non-urgent updates (queue changes, AI responses), `assertive` for critical updates (validation errors, form submission failures).
   - **`requestAnimationFrame` clear-then-set** — forces the screen reader to re-announce even if the message text is identical (e.g., repeated validation errors). Without clearing, assistive technologies may ignore duplicate messages.
   - **`aria-atomic="true"`** — announces the entire region content, not just the delta. Prevents partial announcements of multi-word messages.
   - **Visually hidden via CSS clip** — the `clip: rect(0, 0, 0, 0)` technique is preferred over `display: none` or `visibility: hidden` because the latter two remove elements from the accessibility tree entirely.
   - **`role="status"`** on polite region and **`role="alert"`** on assertive region — provides semantic meaning for screen readers.

2. **Create `useLiveAnnouncer` hook**: Create `app/src/hooks/useLiveAnnouncer.ts`:
   ```tsx
   export const useLiveAnnouncer = (): LiveAnnouncerContextType => {
     const context = useContext(LiveAnnouncerContext);
     if (!context) {
       throw new Error('useLiveAnnouncer must be used within a LiveAnnouncerProvider');
     }
     return context;
   };
   ```
   Usage from any component:
   ```tsx
   const { announce } = useLiveAnnouncer();

   // Polite announcement (non-urgent)
   announce('Queue position updated: you are now #3');

   // Assertive announcement (validation error)
   announce('Email address is required', 'assertive');
   ```

3. **Implement `AccessibleFormField` with inline validation (AC-5, UXR-501)**: Create `app/src/components/forms/AccessibleFormField.tsx`:
   ```tsx
   interface AccessibleFormFieldProps extends Omit<TextFieldProps, 'error'> {
     fieldId: string;
     validate?: (value: string) => string | undefined;
     validateOnBlur?: boolean;
     validateOnChange?: boolean;
   }

   export const AccessibleFormField: React.FC<AccessibleFormFieldProps> = ({
     fieldId,
     validate,
     validateOnBlur = true,
     validateOnChange = false,
     helperText,
     ...props
   }) => {
     const [error, setError] = useState<string | undefined>();
     const [touched, setTouched] = useState(false);
     const { announce } = useLiveAnnouncer();
     const errorId = `${fieldId}-error`;
     const helperId = `${fieldId}-helper`;

     const runValidation = useCallback((value: string) => {
       if (!validate) return;
       const errorMessage = validate(value);
       setError(errorMessage);
       if (errorMessage) {
         announce(errorMessage, 'assertive');
       }
     }, [validate, announce]);

     const handleBlur = useCallback((e: React.FocusEvent<HTMLInputElement>) => {
       setTouched(true);
       if (validateOnBlur) {
         runValidation(e.target.value);
       }
       props.onBlur?.(e);
     }, [validateOnBlur, runValidation, props]);

     const handleChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
       if (validateOnChange && touched) {
         runValidation(e.target.value);
       }
       props.onChange?.(e);
     }, [validateOnChange, touched, runValidation, props]);

     const hasError = touched && !!error;

     return (
       <TextField
         {...props}
         id={fieldId}
         error={hasError}
         onBlur={handleBlur}
         onChange={handleChange}
         helperText={
           hasError ? (
             <span id={errorId} role="alert">
               {error}
             </span>
           ) : helperText ? (
             <span id={helperId}>{helperText}</span>
           ) : undefined
         }
         inputProps={{
           ...props.inputProps,
           'aria-invalid': hasError,
           'aria-describedby': hasError ? errorId : helperText ? helperId : undefined,
           'aria-required': props.required,
         }}
       />
     );
   };
   ```
   Key behaviors (AC-5):
   - **Validation triggers on blur** by default (per UXR-501: "Field validation triggers on blur/change with <200ms response"). Synchronous validation functions execute in <1ms — well within the 200ms threshold.
   - **`aria-invalid={true}`** when field has error — screen readers announce "invalid entry" on field focus.
   - **`aria-describedby`** links to the error message element — screen readers read the error after the field label.
   - **`role="alert"`** on error text — triggers an immediate announcement when the error appears (AC-5: "announced by screen readers via ARIA live regions"). This is more reliable than relying on `aria-live` because `role="alert"` is natively assertive.
   - **`useLiveAnnouncer` with `assertive` priority** — belt-and-suspenders approach: the error is both announced via `role="alert"` on the text and via the global assertive live region. This ensures cross-screen-reader compatibility (NVDA, JAWS, VoiceOver handle live regions differently).
   - **Validate on change after first touch** — once the user has seen an error (via blur), subsequent keystrokes re-validate immediately so the error clears as soon as valid input is entered.

4. **Implement `DynamicContentRegion` for AI and queue updates (edge case 2, UXR-206)**: Create `app/src/components/accessibility/DynamicContentRegion.tsx`:
   ```tsx
   interface DynamicContentRegionProps {
     children: React.ReactNode;
     ariaLabel: string;
     priority?: 'polite' | 'assertive';
     atomic?: boolean;
     relevant?: 'additions' | 'removals' | 'text' | 'all' | 'additions text';
   }

   export const DynamicContentRegion: React.FC<DynamicContentRegionProps> = ({
     children,
     ariaLabel,
     priority = 'polite',
     atomic = false,
     relevant = 'additions text',
   }) => {
     return (
       <div
         aria-live={priority}
         aria-atomic={atomic}
         aria-relevant={relevant}
         aria-label={ariaLabel}
         role={priority === 'assertive' ? 'alert' : 'status'}
       >
         {children}
       </div>
     );
   };
   ```
   Usage for specific screens:
   ```tsx
   // SCR-008: AI Conversational Intake — AI responses announced
   <DynamicContentRegion
     ariaLabel="AI assistant response"
     priority="polite"
     relevant="additions"
   >
     {aiMessages.map(msg => <ChatBubble key={msg.id} message={msg} />)}
   </DynamicContentRegion>

   // SCR-011: Arrival Queue — Queue position changes announced
   <DynamicContentRegion
     ariaLabel="Patient queue updates"
     priority="polite"
     relevant="additions text"
   >
     <Typography>Your position: #{queuePosition}</Typography>
     <Typography>Estimated wait: {estimatedWait} minutes</Typography>
   </DynamicContentRegion>

   // Toast notifications — announced immediately
   <DynamicContentRegion
     ariaLabel="Notification"
     priority="assertive"
     atomic={true}
   >
     <Snackbar message={notification} />
   </DynamicContentRegion>
   ```
   Key behaviors (edge case 2):
   - **`aria-live="polite"`** for AI responses and queue updates — screen readers wait for the user to finish their current interaction before announcing the update. This prevents interrupting form input or navigation.
   - **`aria-live="assertive"`** for toast notifications — immediately interrupts the screen reader to announce critical messages (e.g., "Appointment confirmed").
   - **`aria-relevant="additions"`** for chat messages — only new messages are announced, not the entire conversation history. Without this, every new message would re-read all previous messages.
   - **`aria-relevant="additions text"`** for queue updates — announces both new elements and text changes (e.g., position number changing from #5 to #4).
   - **`aria-atomic={false}`** for chat (default) — announces only the changed portion. **`aria-atomic={true}`** for toasts — announces the complete notification text.

5. **Wrap application in `LiveAnnouncerProvider`**: Update `app/src/App.tsx`:
   ```tsx
   import { LiveAnnouncerProvider } from './components/accessibility/LiveAnnouncer';

   function App() {
     return (
       <LiveAnnouncerProvider>
         <ThemeProvider theme={theme}>
           <CssBaseline />
           <SkipToContent />
           <RouterProvider router={router} />
         </ThemeProvider>
       </LiveAnnouncerProvider>
     );
   }
   ```
   - `LiveAnnouncerProvider` wraps the entire application — any component at any depth can call `useLiveAnnouncer()` to make announcements.
   - Placed outside `ThemeProvider` to ensure live regions exist even if theme loading fails.
   - The visually hidden live region divs are rendered as the last children of the provider, ensuring they are always in the DOM.

6. **Apply `AccessibleFormField` to patient-facing forms (AC-5)**: Replace standard `TextField` usage on screens with validation:
   - **SCR-003 (Registration)**: Email, password, name, phone — all fields use `AccessibleFormField` with validators.
   - **SCR-004 (Password Reset)**: Email field with validation.
   - **SCR-008 (AI Intake)**: Free-text input with character limit validation.
   - **SCR-009 (Manual Intake)**: All patient information fields with required/format validators.
   - **SCR-006 (Appointment Booking)**: Date/time selection validation.

   Example validator:
   ```tsx
   const emailValidator = (value: string): string | undefined => {
     if (!value) return 'Email address is required';
     if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) return 'Enter a valid email address';
     return undefined;
   };

   <AccessibleFormField
     fieldId="registration-email"
     label="Email address"
     required
     validate={emailValidator}
     validateOnBlur
     validateOnChange
   />
   ```

7. **Apply `DynamicContentRegion` to AI and queue screens (edge case 2)**: Wrap dynamic content areas on relevant screens:
   - **SCR-008 (AI Intake)**: Chat message list wrapped in `DynamicContentRegion` with `polite` priority and `additions` relevance.
   - **SCR-011 (Arrival Queue)**: Queue position and wait time wrapped in `DynamicContentRegion` with `polite` priority.
   - **SCR-012 (Document Upload)**: Upload progress and parsing status wrapped in `DynamicContentRegion` with `polite` priority.
   - Toast/Snackbar notifications: Wrapped in `DynamicContentRegion` with `assertive` priority.

## Current Project State

```text
UPACIP/
├── app/
│   ├── package.json
│   ├── tsconfig.json
│   ├── .eslintrc.js                             ← from task_001 (jsx-a11y configured)
│   ├── src/
│   │   ├── App.tsx                              ← from task_002 (landmarks, SkipToContent)
│   │   ├── main.tsx
│   │   ├── theme/
│   │   │   ├── theme.ts                         ← from task_001 (AA-compliant palette, focus)
│   │   │   └── contrastUtils.ts                 ← from task_001
│   │   ├── components/
│   │   │   ├── accessibility/
│   │   │   │   ├── SkipToContent.tsx             ← from task_002
│   │   │   │   ├── FocusTrap.tsx                 ← from task_002
│   │   │   │   ├── AccessibleSelect.tsx          ← from task_002
│   │   │   │   ├── AccessibleDatePicker.tsx      ← from task_002
│   │   │   │   └── AccessibleAutocomplete.tsx    ← from task_002
│   │   │   ├── forms/
│   │   │   ├── layout/
│   │   │   │   ├── Sidebar.tsx                   ← from task_002 (keyboard nav)
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

> Assumes US_002 (frontend React scaffold), task_001 (accessible theme), and task_002 (keyboard nav + ARIA labels) are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | app/src/components/accessibility/LiveAnnouncer.tsx | LiveAnnouncerProvider context with polite + assertive live regions |
| CREATE | app/src/hooks/useLiveAnnouncer.ts | Hook to announce messages to screen readers from any component |
| CREATE | app/src/components/forms/AccessibleFormField.tsx | MUI TextField wrapper with aria-invalid, aria-describedby, live error |
| CREATE | app/src/components/accessibility/DynamicContentRegion.tsx | Wrapper for dynamic content with configurable aria-live |
| MODIFY | app/src/App.tsx | Wrap application in LiveAnnouncerProvider |
| MODIFY | app/src/components/forms/ | Apply AccessibleFormField to patient-facing forms on SCR-003, SCR-008, SCR-009 |

## External References

- [WCAG 2.1 — Status Messages (4.1.3)](https://www.w3.org/WAI/WCAG21/Understanding/status-messages.html)
- [WCAG 2.1 — Error Identification (3.3.1)](https://www.w3.org/WAI/WCAG21/Understanding/error-identification.html)
- [WCAG 2.1 — Error Suggestion (3.3.3)](https://www.w3.org/WAI/WCAG21/Understanding/error-suggestion.html)
- [WAI-ARIA — aria-live](https://www.w3.org/TR/wai-aria-1.2/#aria-live)
- [WAI-ARIA — aria-relevant](https://www.w3.org/TR/wai-aria-1.2/#aria-relevant)
- [WAI-ARIA — aria-atomic](https://www.w3.org/TR/wai-aria-1.2/#aria-atomic)
- [MDN — ARIA Live Regions](https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/ARIA_Live_Regions)
- [MUI 5 — TextField API](https://mui.com/material-ui/api/text-field/)
- [Deque — axe-core Rule Descriptions](https://github.com/dequelabs/axe-core/blob/develop/doc/rule-descriptions.md)

## Build Commands

```powershell
# Build frontend
cd app; npm run build

# Run ESLint with a11y rules
cd app; npx eslint src/components/accessibility/ src/components/forms/ src/hooks/ --ext .ts,.tsx

# Run dev server
cd app; npm run dev
```

## Implementation Validation Strategy

- [ ] `npm run build` completes with zero errors
- [ ] Inline validation error appears within 200ms of invalid input (AC-5)
- [ ] Error message announced by NVDA/VoiceOver when validation fires (AC-5)
- [ ] aria-invalid set to true on fields with errors, false when corrected (AC-5)
- [ ] aria-describedby links error message element to input field (AC-5)
- [ ] LiveAnnouncer polite region announces queue position updates (edge case 2)
- [ ] LiveAnnouncer assertive region announces toast notifications immediately (edge case 2)
- [ ] AI chat responses announced via aria-live="polite" without interrupting user (edge case 2)
- [ ] DynamicContentRegion with aria-relevant="additions" only announces new messages
- [ ] Visual comparison against wireframes completed at 375px, 768px, 1440px
- [ ] Run `/analyze-ux` to validate wireframe alignment

## Implementation Checklist

- [ ] Create LiveAnnouncerProvider with polite + assertive visually hidden live regions
- [ ] Create useLiveAnnouncer hook with announce(message, priority) API
- [ ] Create AccessibleFormField with aria-invalid, aria-describedby, role="alert" on errors
- [ ] Create DynamicContentRegion wrapper with configurable aria-live/aria-relevant
- [ ] Wrap App.tsx in LiveAnnouncerProvider
- [ ] Apply AccessibleFormField to forms on SCR-003, SCR-008, SCR-009
- [ ] Apply DynamicContentRegion to AI chat (SCR-008), queue (SCR-011), notifications
- [ ] Reference wireframes from Design References table during implementation
