/**
 * useSessionTimeout — tracks user inactivity and triggers warning/invalidation callbacks.
 *
 * Timer thresholds (AC-1, AC-4):
 *   WARN_THRESHOLD   = 13 min  (780 000 ms) — show warning modal, countdown starts at 120 s
 *   EXPIRE_THRESHOLD = 15 min  (900 000 ms) — invalidate session
 *
 * Tracked events: mousemove, keydown, mousedown, touchstart, scroll
 * Poll interval: 30 s (30 000 ms) — lightweight check vs constant comparison
 * Throttle: once per second to prevent performance overhead on fast events (AC-2)
 */

import { useCallback, useEffect, useRef } from 'react';

export const WARN_THRESHOLD_MS = 13 * 60 * 1000;   // 780 000 ms
export const EXPIRE_THRESHOLD_MS = 15 * 60 * 1000;  // 900 000 ms
const POLL_INTERVAL_MS = 30_000;                     // 30 s
const THROTTLE_MS = 1_000;                           // 1 s

const TRACKED_EVENTS: (keyof WindowEventMap)[] = [
  'mousemove',
  'keydown',
  'mousedown',
  'touchstart',
  'scroll',
];

interface UseSessionTimeoutOptions {
  /** Called when elapsed inactivity >= WARN_THRESHOLD_MS */
  onWarn: () => void;
  /** Called when elapsed inactivity >= EXPIRE_THRESHOLD_MS */
  onExpire: () => void;
  /** When false, all listeners and intervals are removed */
  enabled: boolean;
}

/**
 * Manages client-side inactivity tracking.
 * Returns `resetTimer` so external callers (API interceptor) can reset on 2xx responses.
 */
export function useSessionTimeout({ onWarn, onExpire, enabled }: UseSessionTimeoutOptions): {
  resetTimer: () => void;
} {
  // Use ref so updates don't trigger re-renders (AC-2 performance)
  const lastActivityRef = useRef<number>(Date.now());
  const warnFiredRef = useRef<boolean>(false);
  const throttleTimeRef = useRef<number>(0);

  const resetTimer = useCallback(() => {
    lastActivityRef.current = Date.now();
    warnFiredRef.current = false;
  }, []);

  useEffect(() => {
    if (!enabled) return;

    // Throttled activity handler — at most once per THROTTLE_MS
    const handleActivity = () => {
      const now = Date.now();
      if (now - throttleTimeRef.current < THROTTLE_MS) return;
      throttleTimeRef.current = now;
      lastActivityRef.current = now;
      warnFiredRef.current = false;
    };

    TRACKED_EVENTS.forEach((evt) =>
      window.addEventListener(evt, handleActivity, { passive: true }),
    );

    // Polling check every POLL_INTERVAL_MS
    const intervalId = setInterval(() => {
      const elapsed = Date.now() - lastActivityRef.current;

      if (elapsed >= EXPIRE_THRESHOLD_MS) {
        onExpire();
        return;
      }

      if (elapsed >= WARN_THRESHOLD_MS && !warnFiredRef.current) {
        warnFiredRef.current = true;
        onWarn();
      }
    }, POLL_INTERVAL_MS);

    return () => {
      TRACKED_EVENTS.forEach((evt) => window.removeEventListener(evt, handleActivity));
      clearInterval(intervalId);
    };
  }, [enabled, onWarn, onExpire]);

  return { resetTimer };
}
