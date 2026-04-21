/**
 * BookingConfirmationModal — UXR-102 confirmation dialog preventing accidental bookings.
 *
 * Displays selected slot details (date, time, provider, appointment type) for patient
 * review before committing the booking. A Chip shows the hold countdown timer so the
 * patient knows how long the slot is reserved; at ≤ 15 seconds remaining the Chip
 * switches to 'warning' colour with an ARIA live announcement (WCAG 2.1 AA).
 *
 * Loading state: "Confirm Booking" button is replaced with a spinner during API call.
 * Error state: a service-unavailable or generic error Alert is shown above the actions
 * when the booking API returns non-409 errors (EC-1).
 *
 * @param open             - Whether the dialog is open
 * @param slot             - Selected appointment slot
 * @param visitType        - Patient-selected visit type label
 * @param secondsRemaining - Seconds left on the hold timer (0 = not counting)
 * @param onConfirm        - Called when patient clicks "Confirm Booking"
 * @param onCancel         - Called when patient clicks "Cancel" or closes dialog
 * @param isLoading        - True while booking API is in-flight
 * @param bookingError     - Optional error string to show (503 or generic)
 * @param isWaitlistOffer  - US_020 AC-3: true when slot was offered via waitlist notification
 * @param offerWithin24Hours - US_020 EC-2: true when offered slot is < 24 h from now * @param isMinorConsentRequired - US_031 AC-1: true when patient is a minor and guardian consent is incomplete */

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Typography from '@mui/material/Typography';
import type { AppointmentSlot } from '@/hooks/useAppointmentSlots';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatDateFull(dateStr: string): string {
  const [y, m, day] = dateStr.split('-').map(Number);
  return new Date(y, m - 1, day).toLocaleDateString('en-US', {
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  });
}

function formatTime(time24: string): string {
  const [hStr, mStr] = time24.split(':');
  const h = parseInt(hStr, 10);
  const period = h >= 12 ? 'PM' : 'AM';
  const hour12 = h % 12 === 0 ? 12 : h % 12;
  return `${hour12}:${mStr ?? '00'} ${period}`;
}

// ─── Types ────────────────────────────────────────────────────────────────────

interface Props {
  open: boolean;
  slot: AppointmentSlot | null;
  visitType: string;
  secondsRemaining: number;
  onConfirm: () => void;
  onCancel: () => void;
  isLoading: boolean;
  bookingError?: string | null;
  /** US_020 AC-3: true when slot was surfaced via a waitlist notification link. */
  isWaitlistOffer?: boolean;
  /** US_020 EC-2: true when offered slot starts within 24 hours of now. */
  offerWithin24Hours?: boolean;
  /**
   * US_031 AC-1: true when the patient is a minor and guardian consent has not been
   * completed. Blocks the Confirm Booking button and shows an explanatory notice.
   */
  isMinorConsentRequired?: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function BookingConfirmationModal({
  open,
  slot,
  visitType,
  secondsRemaining,
  onConfirm,
  onCancel,
  isLoading,
  bookingError,
  isWaitlistOffer = false,
  offerWithin24Hours = false,
  isMinorConsentRequired = false,
}: Props) {
  const showCountdown = secondsRemaining > 0;
  const urgentCountdown = secondsRemaining > 0 && secondsRemaining <= 15;

  return (
    <Dialog
      open={open}
      onClose={isLoading ? undefined : onCancel}
      aria-labelledby="confirm-modal-title"
      maxWidth="xs"
      fullWidth
    >
      <DialogTitle id="confirm-modal-title">Confirm Appointment</DialogTitle>

      <DialogContent>
        {/* US_031 AC-1: minor consent blocker */}
        {isMinorConsentRequired && (
          <Alert severity="warning" sx={{ mb: 2 }}>
            This booking is for a minor patient. Please complete the Guardian Consent section
            in the intake form before confirming this appointment.
          </Alert>
        )}

        {/* US_020 AC-3: waitlist offer banner */}
        {isWaitlistOffer && (
          <Alert severity="info" sx={{ mb: 2 }}>
            This slot was offered from your waitlist — book now to secure your appointment.
          </Alert>
        )}

        {/* US_020 EC-2: within-24h notice (informational only, does not block booking) */}
        {isWaitlistOffer && offerWithin24Hours && (
          <Alert severity="warning" sx={{ mb: 2 }}>
            This appointment is within 24 hours. Please confirm promptly.
          </Alert>
        )}
        {slot && (
          <Box>
            <Typography variant="body2" sx={{ mb: 1 }}>
              <strong>Date:</strong> {formatDateFull(slot.date)}
            </Typography>
            <Typography variant="body2" sx={{ mb: 1 }}>
              <strong>Time:</strong> {formatTime(slot.startTime)}
            </Typography>
            <Typography variant="body2" sx={{ mb: 1 }}>
              <strong>Provider:</strong> {slot.providerName}
            </Typography>
            <Typography variant="body2" sx={{ mb: showCountdown ? 1.5 : 0 }}>
              <strong>Type:</strong> {visitType}
            </Typography>

            {/* Hold countdown — ARIA live region for screen-reader announcements (WCAG 2.1 AA) */}
            {showCountdown && (
              <Box aria-live="polite" aria-atomic="true">
                <Chip
                  label={`Slot held for ${secondsRemaining}s`}
                  color={urgentCountdown ? 'warning' : 'default'}
                  size="small"
                />
              </Box>
            )}
          </Box>
        )}

        {/* EC-1 / generic booking error */}
        {bookingError && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {bookingError}
          </Alert>
        )}
      </DialogContent>

      <DialogActions>
        <Button onClick={onCancel} disabled={isLoading} variant="outlined">
          Cancel
        </Button>
        <Button
          id="modal-confirm"
          onClick={onConfirm}
          disabled={isLoading || isMinorConsentRequired}
          variant="contained"
          startIcon={isLoading ? <CircularProgress size={16} color="inherit" /> : null}
        >
          {isLoading ? 'Booking…' : 'Confirm Booking'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
