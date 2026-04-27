/**
 * queueStore — Zustand store for optimistic queue reorder state (US_054, SCR-011).
 *
 * Holds a locally-overridden display order (array of queueIds) that is applied while
 * a reorder mutation is in-flight or until the server response reconciles the state.
 * When `localOrder` is null the UI defers to server-provided `queuePosition` ordering.
 *
 * Also exports `sortByPriorityTier` — the two-tier sort used throughout the queue:
 *   Tier 1: urgent entries, sorted by arrivalTimestamp ASC (or queuePosition when available)
 *   Tier 2: normal entries, sorted by arrivalTimestamp ASC (or queuePosition when available)
 */

import { create } from 'zustand';
import type { QueueEntry } from '@/hooks/useQueueData';

// ─── Two-tier sort helper ─────────────────────────────────────────────────────

/**
 * Sort queue entries into two priority tiers:
 *   1. urgent   — by arrivalTimestamp ASC (then queuePosition as tiebreaker)
 *   2. normal   — by arrivalTimestamp ASC (then queuePosition as tiebreaker)
 *
 * Entries with no arrivalTimestamp (not yet arrived) are sorted to the end of their tier.
 */
export function sortByPriorityTier(entries: QueueEntry[]): QueueEntry[] {
  const urgentEntries = entries.filter((e) => e.priority === 'urgent');
  const normalEntries = entries.filter((e) => e.priority !== 'urgent');

  const byArrival = (a: QueueEntry, b: QueueEntry): number => {
    // Null arrivals go last within their tier
    if (!a.arrivalTimestamp && !b.arrivalTimestamp) return (a.queuePosition ?? 0) - (b.queuePosition ?? 0);
    if (!a.arrivalTimestamp) return 1;
    if (!b.arrivalTimestamp) return -1;
    const timeDiff = a.arrivalTimestamp.localeCompare(b.arrivalTimestamp);
    if (timeDiff !== 0) return timeDiff;
    return (a.queuePosition ?? 0) - (b.queuePosition ?? 0);
  };

  return [...urgentEntries.sort(byArrival), ...normalEntries.sort(byArrival)];
}

// ─── Store ────────────────────────────────────────────────────────────────────

interface QueueStoreState {
  /** Ordered list of queueIds representing local (optimistic) order. null = use server order. */
  localOrder: string[] | null;
  /** Set a new local ordering (optimistic reorder in progress). */
  setLocalOrder: (ids: string[]) => void;
  /** Clear local order — reverts to server-provided ordering. */
  clearLocalOrder: () => void;
  // ── US_055 — Configurable wait time threshold ──────────────────────────────
  /** Current wait time alert threshold in minutes (synced from useWaitThreshold). Default 30. */
  waitThresholdMinutes: number;
  /** Update the threshold — called by useWaitThreshold when the API value changes (AC-3). */
  setWaitThreshold: (minutes: number) => void;
}

export const useQueueStore = create<QueueStoreState>((set) => ({
  localOrder: null,

  setLocalOrder: (ids) => set({ localOrder: ids }),

  clearLocalOrder: () => set({ localOrder: null }),

  waitThresholdMinutes: 30,

  setWaitThreshold: (minutes) => set({ waitThresholdMinutes: minutes }),
}));
