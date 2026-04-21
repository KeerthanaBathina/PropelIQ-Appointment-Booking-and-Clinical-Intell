/**
 * AppointmentStatusBadge — shared status chip for appointment tables (US_024, UXR-401).
 *
 * Maps appointment status to design-system colors:
 *   Scheduled  → Blue  #1976D2
 *   Completed  → Green #388E3C
 *   Cancelled  → Gray  #757575
 *   NoShow     → Red   #D32F2F
 *
 * Accessibility: aria-label exposes the human-readable status to screen readers.
 *
 * Usage:
 *   <AppointmentStatusBadge status="Completed" />
 */

import Chip from '@mui/material/Chip';
import type { AppointmentStatus } from '@/hooks/usePatientAppointments';

// ─── Design tokens (UXR-401) ──────────────────────────────────────────────────

const STATUS_COLOR: Record<AppointmentStatus, string> = {
  Scheduled: '#1976D2',
  Completed: '#388E3C',
  Cancelled: '#757575',
  NoShow:    '#D32F2F',
};

const STATUS_LABEL: Record<AppointmentStatus, string> = {
  Scheduled: 'Scheduled',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
  NoShow:    'No-Show',
};

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  status: AppointmentStatus;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AppointmentStatusBadge({ status }: Props) {
  const color = STATUS_COLOR[status];
  const label = STATUS_LABEL[status];

  return (
    <Chip
      label={label}
      size="small"
      aria-label={`Status: ${label}`}
      sx={{
        bgcolor:    `${color}1A`,  // 10% opacity background
        color,
        fontWeight: 600,
        fontSize:   '0.7rem',
        height:     22,
      }}
    />
  );
}

// ─── Re-export helpers for consumers that need the maps ───────────────────────

export { STATUS_COLOR, STATUS_LABEL };
