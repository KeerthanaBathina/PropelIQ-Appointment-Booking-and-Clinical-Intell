import { type ReactNode, useEffect, useRef } from 'react';

interface FocusTrapProps {
  children: ReactNode;
  /** When `true` the trap is active and focus is contained within the wrapper. */
  active: boolean;
  /**
   * When `true` (default) focus is restored to the element that was active before
   * the trap was activated — e.g. the button that opened the dialog.
   */
  restoreFocus?: boolean;
}

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
  'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * Keyboard focus containment for modal overlays and drawers (US_100, AC-2).
 *
 * When `active` is `true`:
 * - The first focusable child receives focus immediately.
 * - Tab on the last focusable element wraps back to the first.
 * - Shift+Tab on the first focusable element wraps to the last.
 * - Focus is restored to the previously active element when `active` becomes `false`.
 *
 * Note: MUI `Dialog` already implements focus trapping via its own `Unstable_TrapFocus`.
 * Use this component for non-MUI overlays or custom drawer content that needs explicit
 * containment.
 *
 * WCAG references: 2.1.2 No Keyboard Trap, 2.4.3 Focus Order.
 */
export function FocusTrap({ children, active, restoreFocus = true }: FocusTrapProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const previousFocusRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!active || !containerRef.current) return;

    previousFocusRef.current = document.activeElement as HTMLElement;

    const focusableElements = Array.from(
      containerRef.current.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
    );
    const firstFocusable = focusableElements[0];
    const lastFocusable = focusableElements[focusableElements.length - 1];

    firstFocusable?.focus();

    function handleKeyDown(e: KeyboardEvent) {
      if (e.key !== 'Tab') return;

      if (e.shiftKey && document.activeElement === firstFocusable) {
        e.preventDefault();
        lastFocusable?.focus();
      } else if (!e.shiftKey && document.activeElement === lastFocusable) {
        e.preventDefault();
        firstFocusable?.focus();
      }
    }

    const container = containerRef.current;
    container.addEventListener('keydown', handleKeyDown);

    return () => {
      container.removeEventListener('keydown', handleKeyDown);
      if (restoreFocus) {
        previousFocusRef.current?.focus();
      }
    };
  }, [active, restoreFocus]);

  return <div ref={containerRef}>{children}</div>;
}
