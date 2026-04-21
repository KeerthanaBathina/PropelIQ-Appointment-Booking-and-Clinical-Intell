/**
 * WaitlistConfirmationNotice — Success notice shown after a patient joins the waitlist (US_020, AC-1).
 *
 * Renders a MUI Alert with a summary of the registered criteria and a reminder that the
 * waitlist entry persists until explicitly removed (EC-1).  The notice is dismissable via
 * an `onDismiss` callback so the parent can clear it after the patient continues browsing.
 */

import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Typography from '@mui/material/Typography';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatDate(dateStr: string): string {
  if (!dateStr) return '';
  const [y, m, day] = dateStr.split('-').map(Number);
  return new Date(y, m - 1, day).toLocaleDateString('en-US', {
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  });
}

function formatTime(t: string): string {
  const [hStr, mStr] = t.split(':');
  const h = parseInt(hStr, 10);
  const period = h >= 12 ? 'PM' : 'AM';
  const hour12 = h % 12 === 0 ? 12 : h % 12;
  return `${hour12}:${mStr ?? '00'} ${period}`;
}

// ─── Types ────────────────────────────────────────────────────────────────────

export interface WaitlistCriteria {
  preferredDate: string;
  preferredTimeStart: string;
  preferredTimeEnd: string;
  providerName: string | null;
  visitType: string;
}

interface WaitlistConfirmationNoticeProps {
  criteria: WaitlistCriteria;
  /** Called when the patient dismisses the notice. */
  onDismiss?: () => void;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function WaitlistConfirmationNotice({
  criteria,
  onDismiss,
}: WaitlistConfirmationNoticeProps) {
  const timeRange =
    criteria.preferredTimeStart && criteria.preferredTimeEnd
      ? `${formatTime(criteria.preferredTimeStart)} – ${formatTime(criteria.preferredTimeEnd)}`
      : 'Any time';

  return (
    <Alert
      severity="success"
      onClose={onDismiss}
      sx={{ mb: 3 }}
      role="status"
      aria-live="polite"
    >
      <AlertTitle>You're on the waitlist!</AlertTitle>
      <Typography variant="body2" component="span">
        We'll notify you as soon as a slot matching your preferences becomes available.
      </Typography>
      <Typography
        variant="body2"
        component="ul"
        sx={{ mt: 1, pl: 2, mb: 0.5, listStyleType: 'disc' }}
      >
        <li>
          <strong>Date:</strong> {criteria.preferredDate ? formatDate(criteria.preferredDate) : 'Any'}
        </li>
        <li>
          <strong>Time:</strong> {timeRange}
        </li>
        <li>
          <strong>Provider:</strong> {criteria.providerName ?? 'Any provider'}
        </li>
        <li>
          <strong>Visit type:</strong> {criteria.visitType}
        </li>
      </Typography>
      <Typography variant="caption" color="text.secondary">
        Your waitlist entry remains active until you remove it.
      </Typography>
    </Alert>
  );
}
