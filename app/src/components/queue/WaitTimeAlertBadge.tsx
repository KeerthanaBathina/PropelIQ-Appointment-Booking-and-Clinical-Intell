/**
 * WaitTimeAlertBadge — Amber or red MUI Chip when a patient's wait time exceeds the
 * configurable threshold (US_055 AC-2, UXR-401).
 *
 * Severity tiers (designsystem.md#colors — semantic):
 *   Amber (warning) #ED6C02 — wait >= threshold  AND  wait < threshold × 1.5
 *   Red   (error)   #D32F2F — wait >= threshold × 1.5
 *   null            (hidden) — wait < threshold
 *
 * Accessibility: `aria-label` announces the exact wait time and threshold to screen
 * readers (UXR-206 — ARIA live region for dynamic queue updates).
 *
 * Usage:
 *   <WaitTimeAlertBadge waitTimeMinutes={35} thresholdMinutes={30} />
 *   // → Red badge: "35m wait"
 */

import AccessTimeIcon from '@mui/icons-material/AccessTime';
import Chip from '@mui/material/Chip';

// ─── Design tokens ────────────────────────────────────────────────────────────

const AMBER_BG   = '#FFF3E0';
const AMBER_TEXT = '#ED6C02';
const RED_BG     = '#FFEBEE';
const RED_TEXT   = '#D32F2F';

// ─── Types ────────────────────────────────────────────────────────────────────

interface Props {
  /** Server-computed wait time in whole minutes. */
  waitTimeMinutes:  number;
  /** Configurable alert threshold in minutes (from useWaitThreshold). */
  thresholdMinutes: number;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function WaitTimeAlertBadge({ waitTimeMinutes, thresholdMinutes }: Props) {
  if (waitTimeMinutes < thresholdMinutes) return null;

  const isRed  = waitTimeMinutes >= thresholdMinutes * 1.5;
  const bg     = isRed ? RED_BG     : AMBER_BG;
  const color  = isRed ? RED_TEXT   : AMBER_TEXT;
  const label  = `${waitTimeMinutes}m wait`;

  return (
    <Chip
      icon={<AccessTimeIcon fontSize="small" />}
      label={label}
      size="small"
      aria-label={`Patient waiting ${waitTimeMinutes} minutes, exceeds threshold of ${thresholdMinutes} minutes`}
      sx={{
        backgroundColor: bg,
        color,
        fontWeight: 600,
        ml: 0.5,
        '& .MuiChip-icon': { color: 'inherit' },
      }}
    />
  );
}
