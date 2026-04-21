/**
 * JoinWaitlistDialog — UXR-102 confirmation dialog for waitlist registration (US_020, AC-1).
 *
 * Displays the pre-filled waitlist criteria (preferred date, time range, provider, visit type)
 * for the patient to review before submitting.  Focus is trapped inside the dialog per UXR-201
 * (WCAG 2.1 AA, 2.4.3 Focus Order).
 *
 * States:
 *   default   — criteria summary, "Join Waitlist" and "Cancel" buttons
 *   loading   — "Joining Waitlist…" spinner, both buttons disabled
 *   duplicate — inline Alert, dismiss returns to default state
 *   error     — inline Alert, patient can retry or dismiss
 */

import { useEffect, useRef } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Typography from '@mui/material/Typography';
import Box from '@mui/material/Box';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatDate(dateStr: string): string {
  if (!dateStr) return '';
  const [y, m, day] = dateStr.split('-').map(Number);
  return new Date(y, m - 1, day).toLocaleDateString('en-US', {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  });
}

function formatTimeRange(start: string, end: string): string {
  const fmt = (t: string) => {
    const [hStr, mStr] = t.split(':');
    const h = parseInt(hStr, 10);
    const period = h >= 12 ? 'PM' : 'AM';
    const hour12 = h % 12 === 0 ? 12 : h % 12;
    return `${hour12}:${mStr ?? '00'} ${period}`;
  };
  return `${fmt(start)} – ${fmt(end)}`;
}

// ─── Types ────────────────────────────────────────────────────────────────────

export interface JoinWaitlistDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  isLoading: boolean;
  /** Normalised error kind returned by useJoinWaitlist on failure. */
  errorKind?: 'duplicate' | 'validation' | 'error' | null;
  errorMessage?: string | null;
  /** Pre-filled from the booking page state (AC-1). */
  preferredDate: string;
  preferredTimeStart: string;
  preferredTimeEnd: string;
  visitType: string;
  providerName?: string | null;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function JoinWaitlistDialog({
  open,
  onClose,
  onConfirm,
  isLoading,
  errorKind,
  errorMessage,
  preferredDate,
  preferredTimeStart,
  preferredTimeEnd,
  visitType,
  providerName,
}: JoinWaitlistDialogProps) {
  // UXR-201: move focus to the dialog title when it opens
  const titleRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    if (open) {
      const frame = requestAnimationFrame(() => titleRef.current?.focus());
      return () => cancelAnimationFrame(frame);
    }
  }, [open]);

  const hasError = Boolean(errorKind);
  const errorSeverity = errorKind === 'duplicate' ? 'info' : 'error';

  return (
    <Dialog
      open={open}
      onClose={isLoading ? undefined : onClose}
      aria-labelledby="waitlist-dialog-title"
      maxWidth="xs"
      fullWidth
    >
      <DialogTitle id="waitlist-dialog-title">
        <Typography
          component="h2"
          variant="h6"
          tabIndex={-1}
          ref={titleRef}
          sx={{ outline: 'none' }}
        >
          Join Waitlist
        </Typography>
      </DialogTitle>

      <DialogContent>
        {/* Criteria summary */}
        <Box sx={{ mb: hasError ? 2 : 0 }}>
          <Typography variant="body2" sx={{ mb: 0.75 }}>
            <strong>Preferred Date:</strong>{' '}
            {preferredDate ? formatDate(preferredDate) : 'Any available date'}
          </Typography>
          <Typography variant="body2" sx={{ mb: 0.75 }}>
            <strong>Preferred Time:</strong>{' '}
            {preferredTimeStart && preferredTimeEnd
              ? formatTimeRange(preferredTimeStart, preferredTimeEnd)
              : 'Any time'}
          </Typography>
          <Typography variant="body2" sx={{ mb: 0.75 }}>
            <strong>Provider:</strong> {providerName ?? 'Any provider'}
          </Typography>
          <Typography variant="body2">
            <strong>Visit Type:</strong> {visitType}
          </Typography>
        </Box>

        {/* Inline error / duplicate notice */}
        {hasError && errorMessage && (
          <Alert severity={errorSeverity} sx={{ mt: 2 }} role="alert">
            {errorMessage}
          </Alert>
        )}
      </DialogContent>

      <DialogActions>
        <Button
          onClick={onClose}
          disabled={isLoading}
          variant="outlined"
          aria-label="Cancel joining waitlist"
        >
          Cancel
        </Button>
        {errorKind !== 'duplicate' && (
          <Button
            onClick={onConfirm}
            disabled={isLoading}
            variant="contained"
            aria-label="Confirm joining waitlist"
            startIcon={isLoading ? <CircularProgress size={16} color="inherit" /> : null}
          >
            {isLoading ? 'Joining Waitlist…' : 'Join Waitlist'}
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
