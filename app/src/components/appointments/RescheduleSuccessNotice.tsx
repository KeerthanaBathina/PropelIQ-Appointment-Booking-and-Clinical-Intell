/**
 * RescheduleSuccessNotice — inline confirmation view showing original and new
 * appointment times after a successful reschedule (US_023, AC-3).
 *
 * Mirrors the layout of BookingSuccessView for visual consistency.
 *
 * Accessibility (UXR-201, UXR-202):
 *   - role="status" on the root so screen readers announce the success on mount
 *   - Time values use <time> semantics via Typography display
 */

import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import Box from '@mui/material/Box';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import type { RescheduleConfirmation } from '@/hooks/useRescheduleAppointment';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatLocalDateTime(utcIsoString: string): string {
  return new Intl.DateTimeFormat(undefined, {
    year:   'numeric',
    month:  'long',
    day:    'numeric',
    hour:   'numeric',
    minute: '2-digit',
  }).format(new Date(utcIsoString));
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  confirmation: RescheduleConfirmation;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function RescheduleSuccessNotice({ confirmation }: Props) {
  const {
    oldAppointmentTime,
    newAppointmentTime,
    providerName,
    appointmentType,
    bookingReference,
  } = confirmation;

  return (
    <Box
      role="status"
      aria-live="polite"
      sx={{
        display:        'flex',
        flexDirection:  'column',
        alignItems:     'center',
        gap:            2,
        py:             3,
        px:             2,
        textAlign:      'center',
      }}
    >
      {/* Success icon */}
      <CheckCircleOutlineIcon
        sx={{ fontSize: 56, color: 'success.main' }}
        aria-hidden="true"
      />

      <Typography variant="h6" fontWeight={600}>
        Appointment Rescheduled
      </Typography>

      <Typography variant="body2" color="text.secondary">
        {appointmentType} with {providerName}
      </Typography>

      <Divider sx={{ width: '100%' }} />

      {/* Old time */}
      <Box sx={{ width: '100%', textAlign: 'left' }}>
        <Typography
          variant="caption"
          color="text.secondary"
          display="block"
          gutterBottom
        >
          Previous appointment
        </Typography>
        <Typography
          variant="body2"
          sx={{ textDecoration: 'line-through', color: 'text.secondary' }}
          aria-label={`Previous appointment time: ${formatLocalDateTime(oldAppointmentTime)}`}
        >
          {formatLocalDateTime(oldAppointmentTime)}
        </Typography>
      </Box>

      {/* New time */}
      <Box sx={{ width: '100%', textAlign: 'left' }}>
        <Typography
          variant="caption"
          color="text.secondary"
          display="block"
          gutterBottom
        >
          New appointment
        </Typography>
        <Typography
          variant="body1"
          fontWeight={600}
          color="primary.main"
          aria-label={`New appointment time: ${formatLocalDateTime(newAppointmentTime)}`}
        >
          {formatLocalDateTime(newAppointmentTime)}
        </Typography>
      </Box>

      {/* Booking reference */}
      {bookingReference && (
        <Typography variant="caption" color="text.disabled" sx={{ alignSelf: 'flex-start' }}>
          Reference: {bookingReference}
        </Typography>
      )}
    </Box>
  );
}
