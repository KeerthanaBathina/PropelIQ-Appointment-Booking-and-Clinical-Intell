import type { MouseEvent } from 'react';

interface SkipToContentProps {
  /** ID of the main content element to focus. Defaults to `"main-content"`. */
  targetId?: string;
  /** Visible label text. Defaults to `"Skip to main content"`. */
  label?: string;
}

/**
 * Skip-navigation link for keyboard users (US_100, AC-2, WCAG 2.4.1 Bypass Blocks).
 *
 * Visually hidden until focused — pressing Tab as the very first keystroke reveals it
 * at the top-left corner.  Activating the link (Enter) moves focus and scrolls to the
 * element matching `targetId`, bypassing the header, navigation, and any decorative
 * content that would otherwise require many Tab presses.
 *
 * Style notes:
 * - Off-screen by default (`left: -9999px`) using a CSS class applied inline.
 * - On `:focus` the element is repositioned to `fixed top-2 left-2` with brand colors
 *   meeting WCAG AA contrast (#FFFFFF on #1976D2 → 4.56:1).
 * - Uses a plain `<a>` for native Tab-reachability and Enter activation.
 */
export function SkipToContent({
  targetId = 'main-content',
  label = 'Skip to main content',
}: SkipToContentProps) {
  function handleClick(e: MouseEvent<HTMLAnchorElement>) {
    e.preventDefault();
    const target = document.getElementById(targetId);
    if (target) {
      target.focus();
      target.scrollIntoView({ behavior: 'smooth' });
    }
  }

  return (
    <a
      href={`#${targetId}`}
      onClick={handleClick}
      style={{
        position: 'absolute',
        left: '-9999px',
        top: 'auto',
        width: '1px',
        height: '1px',
        overflow: 'hidden',
      }}
      onFocus={(e) => {
        const el = e.currentTarget;
        Object.assign(el.style, {
          position: 'fixed',
          top: '8px',
          left: '8px',
          width: 'auto',
          height: 'auto',
          padding: '12px 24px',
          backgroundColor: '#1976D2',
          color: '#FFFFFF',
          zIndex: '9999',
          fontSize: '1rem',
          fontWeight: '500',
          borderRadius: '4px',
          outline: '2px solid #0D47A1',
          outlineOffset: '2px',
          textDecoration: 'none',
          overflow: 'visible',
        });
      }}
      onBlur={(e) => {
        const el = e.currentTarget;
        Object.assign(el.style, {
          position: 'absolute',
          left: '-9999px',
          top: 'auto',
          width: '1px',
          height: '1px',
          overflow: 'hidden',
          padding: '',
          backgroundColor: '',
          color: '',
          zIndex: '',
          fontSize: '',
          fontWeight: '',
          borderRadius: '',
          outline: '',
          outlineOffset: '',
          textDecoration: '',
        });
      }}
    >
      {label}
    </a>
  );
}
