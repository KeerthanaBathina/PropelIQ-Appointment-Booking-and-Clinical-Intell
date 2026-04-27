/**
 * QueueStatusBadge — appointment-status and queue-status color-coded Chip (US_053, UXR-401).
 *
 * Design tokens (designsystem.md#appointment-status, UXR-401):
 *   scheduled    → #1976D2 Blue
 *   waiting      → #ED6C02 Orange (waitlisted token — "arrived but waiting")
 *   arrived      → #2E7D32 Green
 *   arrived_late → #ED6C02 Orange
 *   in_visit     → #7B1FA2 Purple
 *   completed    → #388E3C Green (darker)
 *   no_show      → #D32F2F Red
 *   cancelled    → #757575 Gray
 *
 * Priority variant:
 *   urgent       → #D32F2F Red
 *   normal       → #EEEEEE neutral.200 / #424242 neutral.800 text
 *
 * Usage:
 *   <QueueStatusBadge status="waiting" />
 *   <QueueStatusBadge status="urgent" variant="priority" />
 */

import Chip from '@mui/material/Chip';

// ─── Types ────────────────────────────────────────────────────────────────────

type StatusKey =
  | 'scheduled'
  | 'waiting'
  | 'arrived'
  | 'arrived_late'
  | 'in_visit'
  | 'completed'
  | 'no_show'
  | 'cancelled';

type PriorityKey = 'urgent' | 'normal';

interface StatusProps {
  variant?: 'status';
  status: StatusKey | string;
}

interface PriorityProps {
  variant: 'priority';
  status: PriorityKey | string;
}

type Props = StatusProps | PriorityProps;

// ─── Design token maps ────────────────────────────────────────────────────────

const STATUS_CONFIG: Record<string, { label: string; bg: string; text: string }> = {
  scheduled:    { label: 'Scheduled',    bg: '#E3F2FD', text: '#1976D2' },
  waiting:      { label: 'Waiting',      bg: '#FFF3E0', text: '#ED6C02' },
  arrived:      { label: 'Arrived',      bg: '#E8F5E9', text: '#2E7D32' },
  arrived_late: { label: 'Arrived Late', bg: '#FFF3E0', text: '#ED6C02' },
  in_visit:     { label: 'In Visit',     bg: '#F3E5F5', text: '#7B1FA2' },
  completed:    { label: 'Completed',    bg: '#E8F5E9', text: '#388E3C' },
  no_show:      { label: 'No-Show',      bg: '#FFEBEE', text: '#D32F2F' },
  cancelled:    { label: 'Cancelled',    bg: '#F5F5F5', text: '#757575' },
};

const PRIORITY_CONFIG: Record<string, { label: string; bg: string; text: string }> = {
  urgent: { label: 'Urgent', bg: '#FFEBEE', text: '#D32F2F' },
  normal: { label: 'Normal', bg: '#EEEEEE', text: '#424242' },
};

// ─── Component ────────────────────────────────────────────────────────────────

export default function QueueStatusBadge(props: Props) {
  const isPriority = props.variant === 'priority';
  const map        = isPriority ? PRIORITY_CONFIG : STATUS_CONFIG;
  const config     = map[props.status] ?? { label: props.status, bg: '#F5F5F5', text: '#757575' };

  return (
    <Chip
      label={config.label}
      size="small"
      aria-label={`${isPriority ? 'Priority' : 'Status'}: ${config.label}`}
      sx={{
        backgroundColor: config.bg,
        color:           config.text,
        fontWeight:      600,
        fontSize:        '0.625rem',
        letterSpacing:   '0.08333em',
        textTransform:   'uppercase',
        borderRadius:    '999px',
        height:          20,
      }}
    />
  );
}
