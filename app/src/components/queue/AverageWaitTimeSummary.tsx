/**
 * AverageWaitTimeSummary — displays mean wait time for currently waiting patients (US_053 AC-4).
 *
 * Computes the average `waitTimeMinutes` for all entries with status "waiting" or "arrived_late"
 * from the provided queue data. Only active-wait statuses are included (matches AC-4 definition:
 * "average wait time for all currently waiting patients").
 *
 * Renders inline as a single-line summary suitable for placement in the page header area.
 * Shows "—" when there are no waiting patients.
 *
 * Usage:
 *   <AverageWaitTimeSummary entries={data} />
 */

import Typography from '@mui/material/Typography';
import type { QueueEntry } from '@/hooks/useQueueData';

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  entries: QueueEntry[];
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Statuses that count toward "currently waiting" for the average calculation. */
const WAITING_STATUSES = new Set(['waiting', 'arrived_late']);

function computeAverage(entries: QueueEntry[]): number | null {
  const waiting = entries.filter((e) => WAITING_STATUSES.has(e.status));
  if (waiting.length === 0) return null;
  const total = waiting.reduce((sum, e) => sum + e.waitTimeMinutes, 0);
  return Math.round(total / waiting.length);
}

function formatMinutes(minutes: number): string {
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return `${h}h ${m < 10 ? '0' : ''}${m}m`;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AverageWaitTimeSummary({ entries }: Props) {
  const avg = computeAverage(entries);

  return (
    <Typography
      variant="body2"
      color="text.secondary"
      aria-label={avg === null ? 'No waiting patients' : `Average wait time: ${formatMinutes(avg)}`}
      component="span"
    >
      Avg wait: <strong>{avg === null ? '—' : formatMinutes(avg)}</strong>
    </Typography>
  );
}
