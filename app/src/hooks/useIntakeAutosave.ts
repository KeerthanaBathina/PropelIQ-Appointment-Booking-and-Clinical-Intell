/**
 * useIntakeAutosave — shared 30-second boundary autosave with single retry,
 * local-cache fallback, and restore hydration for both intake surfaces (US_030).
 *
 * Responsibilities (AC-1, AC-2, AC-3, EC-1, EC-2):
 *   AC-1  Every 30 s since the last field change, send the current state to the
 *         server and expose `status: 'saved'` for the brief success indicator.
 *   AC-2  On mount, prefer the server draft; reconcile a newer localStorage draft
 *         if the server draft is older (network-loss recovery).
 *   AC-3  `status` transitions 'saving' → 'saved' → 'idle' so the indicator
 *         appears briefly and fades without disrupting typing or navigation.
 *   EC-1  On first save failure, queue a 5-second retry. If the retry also fails,
 *         write to localStorage and expose `status: 'failed'` with the required
 *         fallback message "Save failed – your responses are cached locally."
 *   EC-2  The save snapshot is captured at the 30-second boundary, not on every
 *         keystroke. Rapid changes within the window do not spam the server.
 *
 * Usage:
 * ```tsx
 * const { autosaveStatus, lastSavedAt, notifyChange, flush } = useIntakeAutosave({
 *   cacheKey:  'intake-manual-draft',
 *   saveThunk: async (snapshot) => {
 *     const r = await apiPost<{ lastSavedAt: string }>('/api/intake/manual/draft', snapshot);
 *     return r.lastSavedAt;
 *   },
 * });
 * ```
 */

import { useCallback, useEffect, useRef, useState } from 'react';

// ─── Types ────────────────────────────────────────────────────────────────────

export type AutosaveStatus =
  | 'idle'    // no pending save
  | 'saving'  // network request in flight (AC-3)
  | 'saved'   // just succeeded — UI shows "Auto-saved" (AC-3: then fades back to idle)
  | 'retrying' // first attempt failed; 5-second retry queued (EC-1)
  | 'failed'; // both attempts failed; local cache written; error message shown (EC-1)

export const AUTOSAVE_FAILED_MESSAGE =
  'Save failed – your responses are cached locally.';

export interface AutosaveResult {
  /** Transient autosave status driving the success indicator and failure banner (AC-3, EC-1). */
  autosaveStatus: AutosaveStatus;
  /** ISO timestamp of the last successful server save (UXR-004 freshness display). */
  lastSavedAt: string | null;
  /**
   * Call on every field change to mark the draft as dirty.
   * The hook will persist the snapshot at the next 30-second boundary (EC-2).
   * Pass the current draft snapshot so the boundary capture is always fresh.
   */
  notifyChange: (snapshot: unknown) => void;
  /**
   * Immediately trigger a save outside the normal 30-second cadence.
   * Used by mode-switch handlers to persist state before navigation (AC-3).
   * Returns the server timestamp on success, or null on failure.
   */
  flush: () => Promise<string | null>;
}

export interface UseIntakeAutosaveOptions {
  /**
   * localStorage key used for the local-cache fallback (EC-1).
   * Should be scoped to the intake surface, e.g. "intake-ai-draft" or "intake-manual-draft".
   */
  cacheKey: string;
  /**
   * Async function that sends the current snapshot to the server.
   * Must return the server-confirmed ISO timestamp on success.
   */
  saveThunk: (snapshot: unknown) => Promise<string>;
  /**
   * Whether autosave should be active. Pass `false` while the form is still loading
   * or after submission to prevent spurious saves (EC-2).
   * Defaults to true.
   */
  enabled?: boolean;
}

// ─── Constants ────────────────────────────────────────────────────────────────

const SAVE_INTERVAL_MS  = 30_000;  // AC-1: 30-second boundary
const RETRY_DELAY_MS    = 5_000;   // EC-1: retry once after 5 seconds
const SAVED_DISPLAY_MS  = 2_500;   // AC-3: indicator visible duration before fading to idle

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useIntakeAutosave({
  cacheKey,
  saveThunk,
  enabled = true,
}: UseIntakeAutosaveOptions): AutosaveResult {
  const [autosaveStatus, setAutosaveStatus] = useState<AutosaveStatus>('idle');
  const [lastSavedAt, setLastSavedAt]       = useState<string | null>(null);

  // The latest dirty snapshot captured by notifyChange (EC-2: boundary capture)
  const pendingSnapshotRef = useRef<unknown>(null);
  // True when there is a change since the last successful save
  const isDirtyRef = useRef(false);

  // Refs to manage timers without causing re-renders
  const saveIntervalRef  = useRef<ReturnType<typeof setInterval> | null>(null);
  const retryTimeoutRef  = useRef<ReturnType<typeof setTimeout> | null>(null);
  const savedFadeRef     = useRef<ReturnType<typeof setTimeout> | null>(null);

  // ── Core save logic ────────────────────────────────────────────────────────

  const attemptSave = useCallback(
    async (snapshot: unknown, isRetry = false): Promise<string | null> => {
      setAutosaveStatus(isRetry ? 'retrying' : 'saving');

      try {
        const ts = await saveThunk(snapshot);

        setLastSavedAt(ts);
        setAutosaveStatus('saved');
        isDirtyRef.current = false;

        // Persist to localStorage as golden copy for EC-1 reconciliation
        try {
          localStorage.setItem(cacheKey, JSON.stringify({ snapshot, savedAt: ts }));
        } catch {
          // Quota exceeded or private browsing — silently ignore
        }

        // Fade the "saved" indicator back to idle after SAVED_DISPLAY_MS (AC-3)
        if (savedFadeRef.current) clearTimeout(savedFadeRef.current);
        savedFadeRef.current = setTimeout(() => {
          setAutosaveStatus('idle');
        }, SAVED_DISPLAY_MS);

        return ts;
      } catch {
        if (!isRetry) {
          // EC-1: first failure — schedule a single retry after 5 seconds
          setAutosaveStatus('retrying');
          retryTimeoutRef.current = setTimeout(() => {
            attemptSave(snapshot, true);
          }, RETRY_DELAY_MS);
        } else {
          // EC-1: retry also failed — write to localStorage and surface failure message
          setAutosaveStatus('failed');
          try {
            localStorage.setItem(
              cacheKey,
              JSON.stringify({ snapshot, savedAt: new Date().toISOString(), offlineOnly: true }),
            );
          } catch {
            // Silently ignore storage errors
          }
        }
        return null;
      }
    },
    [cacheKey, saveThunk],
  );

  // ── Periodic 30-second boundary save (EC-2) ───────────────────────────────

  useEffect(() => {
    if (!enabled) return;

    saveIntervalRef.current = setInterval(() => {
      if (!isDirtyRef.current || pendingSnapshotRef.current === null) return;
      // Capture the snapshot at the 30-second mark — not earlier (EC-2)
      const snapshot = pendingSnapshotRef.current;
      attemptSave(snapshot);
    }, SAVE_INTERVAL_MS);

    return () => {
      if (saveIntervalRef.current) clearInterval(saveIntervalRef.current);
    };
  }, [enabled, attemptSave]);

  // ── Cleanup all timers on unmount ─────────────────────────────────────────

  useEffect(() => {
    return () => {
      if (saveIntervalRef.current)  clearInterval(saveIntervalRef.current);
      if (retryTimeoutRef.current)  clearTimeout(retryTimeoutRef.current);
      if (savedFadeRef.current)     clearTimeout(savedFadeRef.current);
    };
  }, []);

  // ── notifyChange — mark dirty and record latest snapshot (EC-2) ───────────

  const notifyChange = useCallback((snapshot: unknown) => {
    pendingSnapshotRef.current = snapshot;
    isDirtyRef.current = true;
    // Reset 'failed' status on new input so the user can keep typing without a sticky error
    setAutosaveStatus((prev) => (prev === 'failed' ? 'idle' : prev));
  }, []);

  // ── flush — immediate out-of-band save (mode-switch, navigate-away) ───────

  const flush = useCallback(async (): Promise<string | null> => {
    if (!isDirtyRef.current || pendingSnapshotRef.current === null) return lastSavedAt;
    const snapshot = pendingSnapshotRef.current;
    return attemptSave(snapshot);
  }, [attemptSave, lastSavedAt]);

  return { autosaveStatus, lastSavedAt, notifyChange, flush };
}

// ─── Local cache helpers (AC-2 restore reconciliation) ───────────────────────

export interface LocalCacheDraft {
  snapshot: unknown;
  savedAt: string;
  offlineOnly?: boolean;
}

/**
 * Reads the local cache entry for the given key.
 * Returns null when nothing is cached or JSON parsing fails.
 */
export function readLocalCacheDraft(cacheKey: string): LocalCacheDraft | null {
  try {
    const raw = localStorage.getItem(cacheKey);
    if (!raw) return null;
    return JSON.parse(raw) as LocalCacheDraft;
  } catch {
    return null;
  }
}

/**
 * Clears the local cache entry after a successful server sync.
 */
export function clearLocalCacheDraft(cacheKey: string): void {
  try {
    localStorage.removeItem(cacheKey);
  } catch {
    // Silently ignore
  }
}
