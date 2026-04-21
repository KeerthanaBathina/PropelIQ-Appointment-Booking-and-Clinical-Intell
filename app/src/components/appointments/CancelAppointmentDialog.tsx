/**
 * CancelAppointmentDialog — UXR-102 destructive-action confirmation dialog (US_019).
 *
 * Shared across SCR-005 (Patient Dashboard) and SCR-007 (Appointment History).
 *
 * States:
 *   - default   : confirmation prompt with appointment summary + Confirm / Go Back
 *   - loading   : Confirm button replaced with CircularProgress; interaction locked
 *   - blocked   : policy Alert shown — "Cancellations within 24 hours are not
 *                 permitted. Please contact the clinic." (AC-2)
 *   - already   : informational Alert — "This appointment has already been
 *                 cancelled." (EC-1)
 *   - error     : generic error Alert with retry affordance (UXR-601)
 *
 * Timezone (EC-2):
 *   `appointmentTime` is a UTC ISO string; formatted to the patient's local timezone
 *   for display via `Intl.DateTimeFormat` (browser locale). The 24-hour eligibility
 *   check is NEVER re-computed here — `cancellable` from the backend is authoritative.
 *
 * Accessibility (UXR-201, UXR-202, UXR-203):
 *   - aria-labelledby wires dialog title
 *   - aria-describedby wires dialog description
 *   - Focus lands on the primary action (Confirm) on open
 *   - All interactive elements reachable via Tab
 *   - Destructive action styled with error color and aria-label
 *
 * @param open             Whether the dialog is visible
 * @param appointment      Appointment to cancel (id, date, provider, type)
 * @param onConfirm        Called when the patient clicks Confirm Cancellation
 * @param onClose          Called when the patient clicks Go Back or closes
 * @param isLoading        True while DELETE API is in-flight
 * @param outcome          Non-null when an error/block has occurred; drives Alert display
 * @param outcomeMessage   Human-readable message for the outcome
 */

import { useRef, useEffect } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogContentText from '@mui/material/DialogContentText';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import type { CancellationOutcome } from '@/hooks/useCancelAppointment';

// ─── Helpers ──────────────────────────────────────────────────────────────────

/**
 * Formats a UTC ISO string to the patient's local timezone (EC-2).
 * e.g. "2026-04-21T10:00:00Z" → "April 21, 2026 at 10:00 AM" (locale-dependent)
 */
function formatLocalDateTime(utcIsoString: string): string {
  const date = new Date(utcIsoString);
  return new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    timeZoneName: 'short',
  }).format(date);
}

// ─── Types ────────────────────────────────────────────────────────────────────

export interface AppointmentSummary {
  id: string;
  /** UTC ISO 8601 timestamp */
  appointmentTime: string;
  providerName: string;
  appointmentType: string;
  bookingReference: string;
}

interface Props {
  open: boolean;
  appointment: AppointmentSummary | null;
  onConfirm: () => void;
  onClose: () => void;
  isLoading: boolean;
  outcome?: Exclude<CancellationOutcome, 'success'> | null;
  outcomeMessage?: string | null;
}

// ─── Alert severity mapping ────────────────────────────────────────────────────

function outcomeToSeverity(
  outcome: Exclude<CancellationOutcome, 'success'>,
): 'warning' | 'info' | 'error' {
  if (outcome === 'policy_blocked') return 'warning';
  if (outcome === 'already_cancelled') return 'info';
  return 'error';
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function CancelAppointmentDialog({
  open,
  appointment,
  onConfirm,
  onClose,
  isLoading,
  outcome,
  outcomeMessage,
}: Props) {
  const confirmBtnRef = useRef<HTMLButtonElement>(null);

  // Move focus to the Confirm button when the dialog opens (WCAG 2.1 AA focus management)
  useEffect(() => {
    if (open && !outcome && !isLoading) {
      // setTimeout defers focus until after MUI transition completes
      const id = setTimeout(() => confirmBtnRef.current?.focus(), 50);
      return () => clearTimeout(id);
    }
  }, [open, outcome, isLoading]);

  const isBlocked = outcome === 'policy_blocked' || outcome === 'already_cancelled';
  const localTime = appointment ? formatLocalDateTime(appointment.appointmentTime) : '';

  return (
    <Dialog
      open={open}
      onClose={isLoading ? undefined : onClose}
      aria-labelledby="cancel-dialog-title"
      aria-describedby="cancel-dialog-description"
      maxWidth="xs"
      fullWidth
    >
      <DialogTitle id="cancel-dialog-title">Cancel Appointment</DialogTitle>

      <DialogContent>
        {/* Outcome alert (policy block, already cancelled, or generic error) */}
        {outcome && outcomeMessage && (
          <Alert
            severity={outcomeToSeverity(outcome)}
            sx={{ mb: 2 }}
            role="alert"
          >
            {outcomeMessage}
          </Alert>
        )}

        {appointment && (
          <>
            <DialogContentText id="cancel-dialog-description" sx={{ mb: 2 }}>
              {isBlocked
                ? 'Review the information about this appointment below.'
                : 'Are you sure you want to cancel this appointment? This action cannot be undone.'}
            </DialogContentText>

            <Divider sx={{ mb: 2 }} />

            {/* Appointment summary */}
            <Box role="region" aria-label="Appointment details">
              <Typography variant="body2" sx={{ mb: 0.5 }}>
                <strong>Date &amp; Time:</strong> {localTime}
              </Typography>
              <Typography variant="body2" sx={{ mb: 0.5 }}>
                <strong>Provider:</strong> {appointment.providerName}
              </Typography>
              <Typography variant="body2" sx={{ mb: 0.5 }}>
                <strong>Type:</strong> {appointment.appointmentType}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 1, fontSize: '0.75rem' }}>
                Ref: {appointment.bookingReference}
              </Typography>
            </Box>
          </>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button
          onClick={onClose}
          disabled={isLoading}
          variant="outlined"
          aria-label="Go back without cancelling"
        >
          Go Back
        </Button>

        {/* Only show Confirm when the outcome is not a terminal block */}
        {!isBlocked && (
          <Button
            ref={confirmBtnRef}
            onClick={onConfirm}
            disabled={isLoading}
            variant="contained"
            color="error"
            aria-label="Confirm appointment cancellation"
            startIcon={
              isLoading ? <CircularProgress size={16} color="inherit" /> : undefined
            }
          >
            {isLoading ? 'Cancelling…' : 'Confirm Cancellation'}
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
