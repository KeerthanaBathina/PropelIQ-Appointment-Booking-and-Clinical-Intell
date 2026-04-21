/**
 * AppointmentCard — shared appointment display card (US_019, UXR-401).
 *
 * Renders a single appointment with:
 *   - Date/time in the patient's local timezone (EC-2)
 *   - Provider and appointment type details
 *   - Color-coded status badge (UXR-401):
 *       Scheduled  = Blue  #1976D2
 *       Completed  = Green #388E3C
 *       Cancelled  = Gray  #757575
 *       No-show    = Red   #D32F2F
 *   - Cancel button only when status=Scheduled AND cancellable=true (AC-1, AC-2)
 *   - Disabled (non-interactive) cancel slot when status=Scheduled AND cancellable=false
 *     (within-24h window, hint displayed via tooltip copy)
 *
 * Used on SCR-005 (Patient Dashboard) and SCR-007 (Appointment History).
 *
 * @param appointment  The appointment to display
 * @param onCancel     Called when patient clicks Cancel; receives appointment
 * @param compact      When true, renders in table-row style (SCR-007); default card style (SCR-005)
 */

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Paper from '@mui/material/Paper';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import CancelOutlinedIcon from '@mui/icons-material/CancelOutlined';
import EventRepeatIcon from '@mui/icons-material/EventRepeat';
import type { PatientAppointment, AppointmentStatus } from '@/hooks/usePatientAppointments';

// ─── Design tokens (UXR-401) ──────────────────────────────────────────────────

const STATUS_COLOR: Record<AppointmentStatus, string> = {
  Scheduled: '#1976D2',
  Completed: '#388E3C',
  Cancelled: '#757575',
  NoShow: '#D32F2F',
};

const STATUS_LABEL: Record<AppointmentStatus, string> = {
  Scheduled: 'Scheduled',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
  NoShow: 'No-Show',
};

// Left-border colour matches status (wireframe SCR-005 `.apt-card` pattern)
const STATUS_BORDER_COLOR: Record<AppointmentStatus, string> = {
  Scheduled: '#1976D2',
  Completed: '#388E3C',
  Cancelled: '#BDBDBD',
  NoShow: '#D32F2F',
};

// ─── Helpers ──────────────────────────────────────────────────────────────────

/**
 * Formats a UTC ISO string to the patient's local timezone (EC-2).
 * e.g. "2026-04-21T10:00:00Z" → "Apr 21, 2026, 10:00 AM EDT"
 */
function formatLocalDateTime(utcIsoString: string): string {
  return new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  }).format(new Date(utcIsoString));
}

// ─── Types ────────────────────────────────────────────────────────────────────

interface Props {
  appointment: PatientAppointment;
  onCancel?: (appointment: PatientAppointment) => void;
  /** Called when the patient clicks Reschedule (US_023). */
  onReschedule?: (appointment: PatientAppointment) => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AppointmentCard({ appointment, onCancel, onReschedule }: Props) {
  const { status, cancellable } = appointment;
  const isScheduled = status === 'Scheduled';
  const showCancelButton = isScheduled;
  // Walk-in flag may not be typed on PatientAppointment yet; read safely.
  const isWalkIn = (appointment as PatientAppointment & { isWalkIn?: boolean }).isWalkIn === true;
  const localTime = formatLocalDateTime(appointment.appointmentTime);
  const borderColor = STATUS_BORDER_COLOR[status];
  const statusColor = STATUS_COLOR[status];
  const statusLabel = STATUS_LABEL[status];

  return (
    <Paper
      variant="outlined"
      sx={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        p: 2,
        borderLeft: `4px solid ${borderColor}`,
        borderRadius: 1,
        mb: 1,
        // Slightly muted background for cancelled/no-show
        bgcolor: status === 'Cancelled' || status === 'NoShow' ? 'action.hover' : 'background.paper',
      }}
      aria-label={`${statusLabel} appointment on ${localTime} with ${appointment.providerName}`}
    >
      {/* Appointment details */}
      <Box>
        <Typography variant="body2" fontWeight={500}>
          {localTime}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          {appointment.providerName} &bull; {appointment.appointmentType}
        </Typography>
        {appointment.bookingReference && (
          <Typography
            variant="caption"
            display="block"
            color="text.disabled"
            sx={{ fontSize: '0.7rem', mt: 0.25 }}
          >
            {appointment.bookingReference}
          </Typography>
        )}
      </Box>

      {/* Status badge + reschedule + cancel actions */}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexShrink: 0 }}>
        <Chip
          label={statusLabel}
          size="small"
          aria-label={`Status: ${statusLabel}`}
          sx={{
            bgcolor: `${statusColor}1A`, // 10% opacity fill
            color: statusColor,
            fontWeight: 600,
            fontSize: '0.7rem',
            height: 22,
          }}
        />

        {/* Reschedule action — shown for scheduled appointments (US_023) */}
        {showCancelButton && (
          <Tooltip
            title={
              isWalkIn
                ? 'Walk-in appointments cannot be rescheduled'
                : 'Reschedule this appointment'
            }
            arrow
          >
            <span>
              <IconButton
                size="small"
                disabled={isWalkIn}
                onClick={() => onReschedule?.(appointment)}
                aria-label={
                  isWalkIn
                    ? `Cannot reschedule walk-in appointment with ${appointment.providerName}`
                    : `Reschedule appointment with ${appointment.providerName} on ${localTime}`
                }
                sx={{
                  color: isWalkIn ? 'text.disabled' : 'primary.main',
                  '&:hover': !isWalkIn
                    ? { bgcolor: 'primary.50', color: 'primary.dark' }
                    : undefined,
                }}
              >
                <EventRepeatIcon fontSize="small" />
              </IconButton>
            </span>
          </Tooltip>
        )}

        {/* Cancel action */}
        {showCancelButton && (
          <Tooltip
            title={
              cancellable
                ? 'Cancel this appointment'
                : 'Cancellations are only allowed more than 24 hours before the appointment'
            }
            arrow
          >
            {/* span wrapper required by Tooltip when button is disabled */}
            <span>
              <IconButton
                size="small"
                disabled={!cancellable}
                onClick={() => onCancel?.(appointment)}
                aria-label={
                  cancellable
                    ? `Cancel appointment with ${appointment.providerName} on ${localTime}`
                    : `Cannot cancel — less than 24 hours until appointment with ${appointment.providerName}`
                }
                sx={{
                  color: cancellable ? 'error.main' : 'text.disabled',
                  '&:hover': cancellable
                    ? { bgcolor: 'error.light', color: 'error.dark' }
                    : undefined,
                }}
              >
                <CancelOutlinedIcon fontSize="small" />
              </IconButton>
            </span>
          </Tooltip>
        )}
      </Box>
    </Paper>
  );
}
