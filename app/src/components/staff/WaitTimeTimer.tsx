/**
 * WaitTimeTimer — real-time wait time display for a queued patient (US_052 AC-4).
 *
 * Calculates elapsed time from `arrivalTimestamp` to now with per-second re-render.
 * Color-coded thresholds (designsystem.md semantic colors):
 *   - success.main (#2E7D32) for < 15 minutes
 *   - warning.main (#ED6C02) for 15–30 minutes
 *   - error.main   (#D32F2F) for > 30 minutes
 *
 * When arrivalTimestamp is null (patient not yet arrived), renders "—".
 *
 * Accessibility (UXR-206): aria-label reports the human-readable wait time
 * so screen readers can announce it in the live region.
 *
 * Usage:
 *   <WaitTimeTimer arrivalTimestamp="2026-04-24T10:00:00Z" />
 */

import { useEffect, useState } from 'react';
import Typography from '@mui/material/Typography';

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  /** ISO-8601 UTC timestamp when patient arrived, or null when not yet arrived. */
  arrivalTimestamp: string | null;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function getElapsedMinutes(arrivalTimestamp: string): number {
  const arrivedAt = new Date(arrivalTimestamp).getTime();
  const now = Date.now();
  return Math.floor((now - arrivedAt) / 60_000);
}

function formatElapsed(minutes: number): string {
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return `${h}h ${m < 10 ? '0' : ''}${m}m`;
}

function getTimerColor(minutes: number): string {
  if (minutes < 15) return 'success.main';
  if (minutes <= 30) return 'warning.main';
  return 'error.main';
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function WaitTimeTimer({ arrivalTimestamp }: Props) {
  const [elapsed, setElapsed] = useState<number | null>(
    arrivalTimestamp ? getElapsedMinutes(arrivalTimestamp) : null,
  );

  useEffect(() => {
    if (!arrivalTimestamp) {
      setElapsed(null);
      return;
    }

    // Update immediately
    setElapsed(getElapsedMinutes(arrivalTimestamp));

    const id = window.setInterval(() => {
      setElapsed(getElapsedMinutes(arrivalTimestamp));
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

  const color = getTimerColor(elapsed);
  const label = formatElapsed(elapsed);

  return (
    <Typography
      variant="h4"
      component="span"
      color={color}
      aria-label={`Wait time: ${label}`}
      sx={{ fontWeight: 500, fontSize: '0.875rem', lineHeight: 1.5 }}
    >
      {label}
    </Typography>
  );
}
