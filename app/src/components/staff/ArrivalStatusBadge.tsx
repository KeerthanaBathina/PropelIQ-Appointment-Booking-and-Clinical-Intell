/**
 * ArrivalStatusBadge — appointment-status color-coded Chip (US_052, UXR-401).
 *
 * Maps queue entry statuses to design token colors (designsystem.md#appointment-status):
 *   waiting      → #2E7D32 (arrived/green)
 *   in_visit     → #7B1FA2 (purple)
 *   completed    → #388E3C (green darker)
 *   no_show      → #D32F2F (red)
 *   arrived_late → #ED6C02 (orange/warning)
 *
 * Uses MUI Chip with pill shape, overline typography, and inline color styles to
 * match tokens not available through MUI's built-in `color` prop.
 *
 * Accessibility (UXR-203): aria-label reports the human-readable status.
 *
 * Usage:
 *   <ArrivalStatusBadge status="waiting" />
 *   <ArrivalStatusBadge status="no_show" />
 */

import Chip from '@mui/material/Chip';
import type { QueueEntryStatus } from '@/hooks/useArrivalQueue';

// ─── Design token map (designsystem.md#appointment-status) ───────────────────

const STATUS_CONFIG: Record<
  QueueEntryStatus,
  { label: string; bg: string; text: string }
> = {
  waiting:     { label: 'Waiting',      bg: '#E8F5E9', text: '#2E7D32' },
  in_visit:    { label: 'In Visit',     bg: '#F3E5F5', text: '#7B1FA2' },
  completed:   { label: 'Completed',    bg: '#E8F5E9', text: '#388E3C' },
  no_show:     { label: 'No-Show',      bg: '#FFEBEE', text: '#D32F2F' },
  arrived_late:{ label: 'Arrived Late', bg: '#FFF3E0', text: '#ED6C02' },
};

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  status: QueueEntryStatus;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ArrivalStatusBadge({ status }: Props) {
  const config = STATUS_CONFIG[status] ?? { label: status, bg: '#F5F5F5', text: '#757575' };

  return (
    <Chip
      label={config.label}
      size="small"
      aria-label={`Status: ${config.label}`}
      sx={{
        backgroundColor: config.bg,
        color:           config.text,
        fontWeight:      600,
        fontSize:        '0.625rem',  // overline size (designsystem.md)
        letterSpacing:   '0.08333em',
        textTransform:   'uppercase',
        borderRadius:    '999px',     // radius.full / pill shape
        height:          20,
      }}
    />
  );
}
