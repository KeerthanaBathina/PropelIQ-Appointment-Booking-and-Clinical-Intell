/**
 * ConfidenceBadge — Pill-shaped badge displaying AI confidence as a percentage
 * with colour coding per UXR-105 and designsystem.md § AI Confidence Colors.
 *
 * Thresholds (designsystem.md #ConfidenceBadge):
 *   High    >= 80%  → success.main  #2E7D32 (green)
 *   Medium  60–79%  → warning.main  #ED6C02 (amber)
 *   Low     < 60%   → error.main    #D32F2F (red)
 *
 * Shape: `radius.full` (9999px), pill.
 * Typography: overline (0.625rem, uppercase, letter-spacing 0.08333em).
 * ARIA: aria-label="AI confidence: XX%"
 */

import Box from '@mui/material/Box';
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
  const pct     = Math.round(score * 100);
  const bgColor = getConfidenceColor(score);
  const label   = `AI confidence: ${pct}%`;

  return (
    <Box
      component="span"
      aria-label={label}
      role="img"
      sx={{
        display:       'inline-flex',
        alignItems:    'center',
        justifyContent: 'center',
        bgcolor:       bgColor,
        color:         '#FFFFFF',
        borderRadius:  '9999px',
        px:            1,
        py:            0.25,
        fontSize:      '0.625rem',   // overline
        fontWeight:    500,
        lineHeight:    2.66,
        letterSpacing: '0.08333em',
        textTransform: 'uppercase',
        whiteSpace:    'nowrap',
        minWidth:      36,
        ...sx,
      }}
    >
      {pct}%
    </Box>
  );
}
