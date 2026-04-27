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
 *
 * Server sync (US_065 AC-4):
 *   Every 60 seconds, polls GET /api/session/time-remaining (when enabled and warning
 *   modal is not visible). If the server reports remainingSeconds ≤ warningThresholdSeconds
 *   (120 s), the warning is triggered immediately to correct for client-server clock drift.
 *   If the server reports session expired (401 / null), the expire callback fires.
 *   Network failures are fail-open — the client-side timer continues unaffected.
 */

import { useCallback, useEffect, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

export const WARN_THRESHOLD_MS = 13 * 60 * 1000;   // 780 000 ms
export const EXPIRE_THRESHOLD_MS = 15 * 60 * 1000;  // 900 000 ms
const POLL_INTERVAL_MS = 30_000;                     // 30 s
const THROTTLE_MS = 1_000;                           // 1 s
const SERVER_SYNC_INTERVAL_MS = 60_000;              // 60 s

const TRACKED_EVENTS: (keyof WindowEventMap)[] = [
  'mousemove',
  'keydown',
  'mousedown',
  'touchstart',
  'scroll',
];

interface TimeRemainingResponse {
  remainingSeconds: number;
  warningThresholdSeconds: number;
}

interface UseSessionTimeoutOptions {
  /** Called when elapsed inactivity >= WARN_THRESHOLD_MS */
  onWarn: () => void;
  /** Called when elapsed inactivity >= EXPIRE_THRESHOLD_MS */
  onExpire: () => void;
  /** When false, all listeners and intervals are removed */
  enabled: boolean;
  /**
   * When true, server-side polling is paused (the 1-second modal countdown takes over).
   * Set to true by SessionTimeoutProvider when the warning modal is visible (US_065 AC-4).
   */
  isWarningVisible?: boolean;
}

/**
 * Manages client-side inactivity tracking with optional server-side TTL synchronisation.
 * Returns `resetTimer` so external callers (API interceptor) can reset on 2xx responses.
 */
export function useSessionTimeout({
  onWarn,
  onExpire,
  enabled,
  isWarningVisible = false,
}: UseSessionTimeoutOptions): {
  resetTimer: () => void;
} {
  // Use ref so updates don't trigger re-renders (AC-2 performance)
  const lastActivityRef = useRef<number>(Date.now());
  const warnFiredRef = useRef<boolean>(false);
  const throttleTimeRef = useRef<number>(0);

  // Stable refs so polling callback doesn't re-subscribe on prop changes
  const onWarnRef   = useRef(onWarn);
  const onExpireRef = useRef(onExpire);
  onWarnRef.current   = onWarn;
  onExpireRef.current = onExpire;

  const resetTimer = useCallback(() => {
    lastActivityRef.current = Date.now();
    warnFiredRef.current = false;
  }, []);

  // ── Client-side inactivity polling ──────────────────────────────────────────
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
        onExpireRef.current();
        return;
      }

      if (elapsed >= WARN_THRESHOLD_MS && !warnFiredRef.current) {
        warnFiredRef.current = true;
        onWarnRef.current();
      }
    }, POLL_INTERVAL_MS);

    return () => {
      TRACKED_EVENTS.forEach((evt) => window.removeEventListener(evt, handleActivity));
      clearInterval(intervalId);
    };
  }, [enabled]);

  // ── Server-side TTL synchronisation (US_065 AC-4) ───────────────────────────
  // Polls GET /api/session/time-remaining every 60 seconds when enabled and the warning
  // modal is not visible. Pauses during the modal countdown to avoid redundant calls.
  //
  // Error handling:
  //   401 — apiClient.handleAuthError fires _invalidateFn → session expired (handled globally).
  //   440 — apiClient.handleAuthError fires _sessionTerminatedFn (handled globally).
  //   Network error — query goes to error state; fail-open: client timer continues.
  const { data: serverTime } = useQuery<TimeRemainingResponse>({
    queryKey: ['session', 'time-remaining'],
    queryFn: () => apiGet<TimeRemainingResponse>('/api/session/time-remaining'),
    enabled: enabled && !isWarningVisible,
    refetchInterval: enabled && !isWarningVisible ? SERVER_SYNC_INTERVAL_MS : false,
    retry: 1,
    // Stale time 0 so each poll fetch is always fresh
    staleTime: 0,
  });

  useEffect(() => {
    if (!serverTime || !enabled || isWarningVisible) return;

    const { remainingSeconds, warningThresholdSeconds } = serverTime;

    // Session expired server-side (Redis TTL drained) — onExpire handled by the 401 path
    // in apiClient.handleAuthError, but defensively guard here too.
    if (remainingSeconds <= 0) {
      onExpireRef.current();
      return;
    }

    // Server-corrected warning trigger: fires the modal even if the client timer hasn't
    // reached 13 minutes yet (corrects for browser tab sleep, clock drift, etc.).
    if (remainingSeconds <= warningThresholdSeconds && !warnFiredRef.current) {
      warnFiredRef.current = true;
      onWarnRef.current();
    }
  }, [serverTime, enabled, isWarningVisible]);

  return { resetTimer };
}

