/**
 * QueueWaitTimeTimer — live-updating MM:SS wait time display (US_053, UXR-103).
 *
 * Calculates elapsed time from `arrivalTimestamp` to now with per-second re-renders.
 * Displays as MM:SS format per the wireframe (wireframe-SCR-011-arrival-queue.html).
 *
 * Visual states (designsystem.md semantic colors):
 *   normal (< 30 min) — no background highlight, color follows threshold
 *   alert  (≥ 30 min) — `error.surface` row highlight is applied by the table parent;
 *                        the timer itself turns error.main red
 *
 * Color thresholds:
 *   < 15 min  → success.main  (#2E7D32)
 *   15-30 min → warning.main  (#ED6C02)
 *   > 30 min  → error.main    (#D32F2F) + timer-alert styling
 *
 * Renders "—" when arrivalTimestamp is null (Scheduled patients, no arrival yet).
 *
 * Accessibility (UXR-206): aria-label reports a human-readable description.
 *
 * Usage:
 *   <QueueWaitTimeTimer arrivalTimestamp="2026-04-24T10:00:00Z" />
 *   <QueueWaitTimeTimer arrivalTimestamp={null} />
 */

import { useEffect, useState } from 'react';
import Typography from '@mui/material/Typography';

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  /** ISO-8601 UTC arrival timestamp, or null for not-yet-arrived patients. */
  arrivalTimestamp: string | null;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function getElapsedSeconds(ts: string): number {
  return Math.floor((Date.now() - new Date(ts).getTime()) / 1_000);
}

/** Format elapsed seconds as MM:SS matching wireframe timer style. */
function formatMmSs(totalSeconds: number): string {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
}

function getColor(totalSeconds: number): string {
  const minutes = totalSeconds / 60;
  if (minutes < 15) return 'success.main';
  if (minutes <= 30) return 'warning.main';
  return 'error.main';
}

function getAriaLabel(totalSeconds: number): string {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (minutes === 0) return `Wait time: ${seconds} seconds`;
  return `Wait time: ${minutes} minute${minutes !== 1 ? 's' : ''} ${seconds} seconds`;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function QueueWaitTimeTimer({ arrivalTimestamp }: Props) {
  const [elapsed, setElapsed] = useState<number | null>(
    arrivalTimestamp ? getElapsedSeconds(arrivalTimestamp) : null,
  );

  useEffect(() => {
    if (!arrivalTimestamp) {
      setElapsed(null);
      return;
    }

    setElapsed(getElapsedSeconds(arrivalTimestamp));

    const id = window.setInterval(() => {
      setElapsed(getElapsedSeconds(arrivalTimestamp));
    }, 1_000);

    return () => window.clearInterval(id);
  }, [arrivalTimestamp]);

  if (elapsed === null) {
    return (
      <Typography variant="body2" color="text.secondary" aria-label="Not yet arrived">
        —
      </Typography>
    );
  }

  return (
    <Typography
      variant="body2"
      component="span"
      fontFamily="monospace"
      fontWeight={600}
      color={getColor(elapsed)}
      aria-label={getAriaLabel(elapsed)}
    >
      {formatMmSs(elapsed)}
    </Typography>
  );
}
