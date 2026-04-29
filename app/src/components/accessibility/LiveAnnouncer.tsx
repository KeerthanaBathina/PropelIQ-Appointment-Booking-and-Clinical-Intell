import {
  createContext,
  useCallback,
  useMemo,
  useState,
  type ReactNode,
} from 'react';

// ─── Visually-hidden CSS (clip-rect technique) ────────────────────────────────
// Using clip rather than display:none / visibility:hidden because those remove
// elements from the accessibility tree, defeating the purpose of a live region.
const hiddenStyle: React.CSSProperties = {
  position: 'absolute',
  width: '1px',
  height: '1px',
  padding: 0,
  margin: '-1px',
  overflow: 'hidden',
  clip: 'rect(0, 0, 0, 0)',
  whiteSpace: 'nowrap',
  border: 0,
};

// ─── Context ──────────────────────────────────────────────────────────────────
export interface LiveAnnouncerContextType {
  /**
   * Announces `message` to screen readers.
   *
   * @param message  Text to announce.  Pass an empty string to clear a previous announcement.
   * @param priority `"polite"` (default) — waits for the user to finish before reading.
   *                 `"assertive"` — interrupts immediately (use sparingly: validation errors,
   *                 critical alerts).
   */
  announce: (message: string, priority?: 'polite' | 'assertive') => void;
}

// Context is co-located with the provider (single file, no circular imports).
// eslint-disable-next-line react-refresh/only-export-components
export const LiveAnnouncerContext = createContext<LiveAnnouncerContextType | undefined>(
  undefined,
);

// ─── Provider ─────────────────────────────────────────────────────────────────

interface LiveAnnouncerProviderProps {
  children: ReactNode;
}

/**
 * Provides a global ARIA live-region infrastructure (US_100, AC-5, edge case 2).
 *
 * Two visually-hidden regions are mounted once at the application root:
 *
 * - **`aria-live="polite"` / `role="status"`** — non-urgent updates such as queue
 *   position changes, AI response completions, and upload progress.  Screen readers
 *   announce these after the user finishes their current interaction.
 *
 * - **`aria-live="assertive"` / `role="alert"`** — urgent updates such as form
 *   validation errors and critical notifications.  Screen readers interrupt
 *   immediately (WCAG 4.1.3 Status Messages, WCAG 3.3.1 Error Identification).
 *
 * The `requestAnimationFrame` clear-then-set pattern ensures identical repeated
 * messages are re-announced.  Without clearing first, assistive technologies
 * (particularly NVDA and JAWS) suppress duplicate content.
 *
 * `aria-atomic="true"` guarantees the full message text is read, not just the
 * changed portion — important for short numeric updates like "#3 → #2" in queue displays.
 *
 * Place this provider outside ThemeProvider so the live regions remain in the DOM
 * even if theme initialisation throws.
 */
export function LiveAnnouncerProvider({ children }: LiveAnnouncerProviderProps) {
  const [politeMessage, setPoliteMessage]     = useState('');
  const [assertiveMessage, setAssertiveMessage] = useState('');

  const announce = useCallback(
    (message: string, priority: 'polite' | 'assertive' = 'polite') => {
      if (priority === 'assertive') {
        setAssertiveMessage('');
        requestAnimationFrame(() => setAssertiveMessage(message));
      } else {
        setPoliteMessage('');
        requestAnimationFrame(() => setPoliteMessage(message));
      }
    },
    [],
  );

  const contextValue = useMemo(() => ({ announce }), [announce]);

  return (
    <LiveAnnouncerContext.Provider value={contextValue}>
      {children}

      {/* Polite region — non-urgent updates (queue, AI, progress) */}
      <div
        aria-live="polite"
        aria-atomic="true"
        role="status"
        style={hiddenStyle}
      >
        {politeMessage}
      </div>

      {/* Assertive region — validation errors, critical alerts */}
      <div
        aria-live="assertive"
        aria-atomic="true"
        role="alert"
        style={hiddenStyle}
      >
        {assertiveMessage}
      </div>
    </LiveAnnouncerContext.Provider>
  );
}
