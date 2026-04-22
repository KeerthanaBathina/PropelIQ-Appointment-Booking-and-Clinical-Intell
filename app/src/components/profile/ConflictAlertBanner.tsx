/**
 * ConflictAlertBanner — US_044 SCR-013 conflict indicator (wireframe alert-warning).
 *
 * MUI Alert (warning severity, warning-surface #FFF3E0 background) showing the
 * number of detected data conflicts and a "Review & Resolve" action.
 *
 * When `onReviewClick` is provided the link opens the ConflictResolutionModal;
 * otherwise it falls back to `resolveHref` for backwards compatibility (US_043).
 *
 * Renders nothing when conflictCount is 0.
 */

import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Link from '@mui/material/Link';

// ─── Props ────────────────────────────────────────────────────────────────────

interface ConflictAlertBannerProps {
  conflictCount: number;
  /** Called when the "Review & Resolve" link is clicked. Takes precedence over resolveHref. */
  onReviewClick?: () => void;
  /** Fallback href used when onReviewClick is not provided. */
  resolveHref?: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ConflictAlertBanner({
  conflictCount,
  onReviewClick,
  resolveHref = '#',
}: ConflictAlertBannerProps) {
  if (conflictCount <= 0) return null;

  const label = conflictCount === 1
    ? '1 data conflict detected'
    : `${conflictCount} data conflicts detected`;

  return (
    <Alert
      severity="warning"
      sx={{
        mb: 2,
        bgcolor: 'warning.surface',
        '& .MuiAlert-icon': { color: 'warning.main' },
      }}
      role="alert"
      aria-live="polite"
    >
      <AlertTitle sx={{ mb: 0 }}>
        {label} —{' '}
        <Link
          {...(onReviewClick
            ? { component: 'button', onClick: onReviewClick }
            : { href: resolveHref })}
          sx={{ color: 'warning.dark', fontWeight: 500 }}
          aria-label={`Review and resolve ${conflictCount} data conflict${conflictCount !== 1 ? 's' : ''}`}
        >
          Review &amp; Resolve
        </Link>
      </AlertTitle>
    </Alert>
  );
}
