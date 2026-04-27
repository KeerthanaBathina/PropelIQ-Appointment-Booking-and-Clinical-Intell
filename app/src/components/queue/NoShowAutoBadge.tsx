/**
 * NoShowAutoBadge — Red "No-Show (Auto)" Chip for system-auto-detected no-show entries (US_055).
 *
 * Renders in the Status column of ArrivalQueueTable when `isAutoDetected` is true,
 * replacing the standard `QueueStatusBadge status="no_show"` to surface the auto-detection context.
 *
 * Variants (designsystem.md#colors — appointment-status):
 *   Normal auto     → label "No-Show (Auto)"          — red badge (#D32F2F)
 *   Delayed detect  → label "No-Show (Auto - Delayed)" — red badge, indicates detection delay edge case
 *
 * Delayed detection edge case: patient arrived at ~14 minutes (just before auto no-show window),
 * the timer reset upon arrival marking but the no-show was not cancelled in time (US_055 edge case).
 *
 * Row opacity (0.6) for no-show rows is applied at the table-row level in ArrivalQueueTable, not here.
 *
 * Usage:
 *   <NoShowAutoBadge isAutoDetected={entry.isAutoNoShow} isDelayedDetection={entry.isDelayedDetection} />
 */

import Chip from '@mui/material/Chip';

// ─── Design tokens ────────────────────────────────────────────────────────────

const NO_SHOW_BG   = '#FFEBEE';
const NO_SHOW_TEXT = '#D32F2F';

// ─── Types ────────────────────────────────────────────────────────────────────

interface Props {
  /** Set to true to render the badge; false/undefined renders nothing. */
  isAutoDetected: boolean;
  /** Set to true to show the delayed-detection variant label. */
  isDelayedDetection?: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function NoShowAutoBadge({ isAutoDetected, isDelayedDetection = false }: Props) {
  if (!isAutoDetected) return null;

  const label = isDelayedDetection
    ? 'No-Show (Auto - Delayed)'
    : 'No-Show (Auto)';

  return (
    <Chip
      label={label}
      size="small"
      aria-label={label}
      sx={{
        backgroundColor: NO_SHOW_BG,
        color:           NO_SHOW_TEXT,
        fontWeight:      600,
      }}
    />
  );
}
