/**
 * UrgentBadge — AC-3 URGENT indicator for medication contraindication conflicts.
 *
 * MUI Chip with error color (#D32F2F) and a CSS pulse animation to draw attention
 * to medication contraindications that must be reviewed immediately (AC-3, AIR-S09).
 *
 * Only renders when `isUrgent` is true; returns null otherwise so callers can
 * include it unconditionally in layouts.
 */

import Chip from '@mui/material/Chip';
import type { SxProps, Theme } from '@mui/material/styles';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';

// ─── Pulse keyframes (accessible: respects prefers-reduced-motion) ────────────

const pulseKeyframes = {
  '@keyframes urgentPulse': {
    '0%':   { boxShadow: '0 0 0 0 rgba(211, 47, 47, 0.45)' },
    '70%':  { boxShadow: '0 0 0 6px rgba(211, 47, 47, 0)' },
    '100%': { boxShadow: '0 0 0 0 rgba(211, 47, 47, 0)' },
  },
};

// ─── Props ────────────────────────────────────────────────────────────────────

interface UrgentBadgeProps {
  isUrgent: boolean;
  size?: 'small' | 'medium';
  sx?: SxProps<Theme>;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function UrgentBadge({ isUrgent, size = 'small', sx }: UrgentBadgeProps) {
  if (!isUrgent) return null;

  return (
    <Chip
      icon={<ErrorOutlineIcon fontSize="small" />}
      label="URGENT"
      color="error"
      size={size}
      aria-label="Urgent — medication contraindication requires immediate review"
      role="status"
      sx={{
        fontWeight: 700,
        letterSpacing: 0.5,
        bgcolor: 'error.main',
        color: 'error.contrastText',
        '& .MuiChip-icon': { color: 'inherit' },
        '@media (prefers-reduced-motion: no-preference)': {
          ...pulseKeyframes,
          animation: 'urgentPulse 1.8s ease-in-out infinite',
        },
        ...sx,
      }}
    />
  );
}
