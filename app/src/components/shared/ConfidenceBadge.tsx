/**
 * ConfidenceBadge — reusable MUI Chip displaying AI confidence as a percentage
 * with colour coding per UXR-105 and designsystem.md § AI Confidence Colors.
 *
 * Thresholds (UXR-105):
 *   High    >= 80%  → success.main  #2E7D32 (green)
 *   Medium  60–79%  → warning.main  #ED6C02 (amber)
 *   Low     < 60%   → error.main    #D32F2F (red)
 *
 * ARIA: aria-label="AI confidence: XX%"
 *
 * Note: a Box-based variant also exists at components/coding/ConfidenceBadge.tsx
 * for the ICD-10 code table. This Chip-based version is used in the verification
 * queue table (US_049) and any new components that need the badge inline with text.
 */

import Chip from '@mui/material/Chip';
import type { SxProps, Theme } from '@mui/material/styles';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function getConfidenceColor(score: number): string {
  if (score >= 0.8) return '#2E7D32'; // success.main
  if (score >= 0.6) return '#ED6C02'; // warning.main
  return '#D32F2F';                   // error.main
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface ConfidenceBadgeProps {
  /** Confidence score in [0.0, 1.0]. */
  score: number;
  sx?: SxProps<Theme>;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ConfidenceBadge({ score, sx }: ConfidenceBadgeProps) {
  const pct   = Math.round(score * 100);
  const color = getConfidenceColor(score);

  return (
    <Chip
      label={`${pct}%`}
      size="small"
      aria-label={`AI confidence: ${pct}%`}
      sx={{
        bgcolor:      color,
        color:        '#FFFFFF',
        fontWeight:   600,
        fontSize:     '0.75rem',
        height:       24,
        borderRadius: '9999px',
        '& .MuiChip-label': { px: 1 },
        ...sx,
      }}
    />
  );
}
