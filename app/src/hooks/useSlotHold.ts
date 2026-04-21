/**
 * useSlotHold — Slot hold lifecycle management with 60-second countdown (US_018, AC-3).
 *
 * Responsibilities:
 *   1. POST /api/appointments/hold to acquire a Redis-backed hold.
 *   2. Counts down from 60 seconds, updating per-second for confirmation modal display.
 *   3. On expiry: fires DELETE /api/appointments/hold/{slotId} and transitions to
 *      'expired' status so the parent can react via useEffect on holdStatus.
 *   4. On manual releaseHold(): fires DELETE and transitions back to 'idle'.
 *
 * Optimistic behaviour (UXR-503):
 *   heldSlotId is set synchronously before the API call returns so TimeSlotGrid
 *   can render the "Reserved" badge without waiting. On hold failure the state is
 *   reverted to null and holdStatus is set to 'error' with holdError populated.
 *
 * Parent usage pattern for expiry:
 *   const hold = useSlotHold();
 *   useEffect(() => {
 *     if (hold.holdStatus === 'expired') {
 *       // react to expiry...
 *       hold.clearExpiredNotification();
 *     }
 *   }, [hold.holdStatus]);
 */

import { useState, useRef, useCallback, useEffect } from 'react';
import { apiPost, apiDelete, ApiError } from '@/lib/apiClient';

// ─── Constants ────────────────────────────────────────────────────────────────

const HOLD_DURATION_SECONDS = 60; // AC-3

// ─── Types ────────────────────────────────────────────────────────────────────

export type HoldStatus = 'idle' | 'holding' | 'expired' | 'error';

export interface UseSlotHoldReturn {
  heldSlotId: string | null;
  secondsRemaining: number;
  holdStatus: HoldStatus;
  holdError: string | null;
  /** Acquires a hold for the given slot. Returns true if successful. */
  startHold: (slotId: string) => Promise<boolean>;
  /** Releases the active hold manually (on cancel or successful booking). */
  releaseHold: () => Promise<void>;
  /** Clears the 'expired' status after the parent has reacted to it. */
  clearExpiredNotification: () => void;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useSlotHold(): UseSlotHoldReturn {
  const [heldSlotId, setHeldSlotId] = useState<string | null>(null);
  const [secondsRemaining, setSecondsRemaining] = useState(0);
  const [holdStatus, setHoldStatus] = useState<HoldStatus>('idle');
  const [holdError, setHoldError] = useState<string | null>(null);

  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  // Ref so the interval callback always reads the current held slot ID
  const heldSlotRef = useRef<string | null>(null);

  const stopTimer = useCallback(() => {
    if (timerRef.current !== null) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const startCountdown = useCallback(
    (slotId: string) => {
      stopTimer();
      heldSlotRef.current = slotId;
      setSecondsRemaining(HOLD_DURATION_SECONDS);

      timerRef.current = setInterval(() => {
        setSecondsRemaining((prev) => {
          const next = prev - 1;
          if (next <= 0) {
            stopTimer();
            const expiredId = heldSlotRef.current;
            heldSlotRef.current = null;
            if (expiredId) {
              // Fire-and-forget: best-effort server-side release (AC-3)
              void apiDelete<void>(`/api/appointments/hold/${expiredId}`).catch(() => {});
            }
            setHeldSlotId(null);
            setHoldStatus('expired');
            return 0;
          }
          return next;
        });
      }, 1000);
    },
    [stopTimer],
  );

  const startHold = useCallback(
    async (slotId: string): Promise<boolean> => {
      // Optimistic: mark as held immediately before API responds (UXR-503)
      setHeldSlotId(slotId);
      setSecondsRemaining(HOLD_DURATION_SECONDS);
      setHoldStatus('holding');
      setHoldError(null);

      try {
        await apiPost<void>('/api/appointments/hold', { slotId });
        startCountdown(slotId);
        return true;
      } catch (err) {
        // Rollback optimistic state on failure (UXR-503)
        const message =
          err instanceof ApiError && err.status === 409
            ? 'This slot was just reserved by another user.'
            : 'Unable to reserve this slot. Please try again.';
        setHeldSlotId(null);
        setSecondsRemaining(0);
        setHoldStatus('error');
        setHoldError(message);
        return false;
      }
    },
    [startCountdown],
  );

  const releaseHold = useCallback(async (): Promise<void> => {
    const slotId = heldSlotRef.current;
    stopTimer();
    heldSlotRef.current = null;
    setHeldSlotId(null);
    setSecondsRemaining(0);
    setHoldStatus('idle');
    setHoldError(null);

    if (slotId) {
      // Best-effort: server TTL will expire the hold regardless of this call
      await apiDelete<void>(`/api/appointments/hold/${slotId}`).catch(() => {});
    }
  }, [stopTimer]);

  const clearExpiredNotification = useCallback(() => {
    setHoldStatus((prev) => (prev === 'expired' ? 'idle' : prev));
  }, []);

  // Clean up timer on unmount
  useEffect(() => () => stopTimer(), [stopTimer]);

  return {
    heldSlotId,
    secondsRemaining,
    holdStatus,
    holdError,
    startHold,
    releaseHold,
    clearExpiredNotification,
  };
}
