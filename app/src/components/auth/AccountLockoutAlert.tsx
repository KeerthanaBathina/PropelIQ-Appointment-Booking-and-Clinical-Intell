/**
 * AccountLockoutAlert — SCR-001 lockout error state (US_016, AC-2, AC-3).
 *
 * Rendered on the login page when the backend returns 423 Locked.
 * Displays a dynamic countdown "Account locked. Try again in MM:SS."
 * When the countdown reaches 0, calls onExpired() so the parent can
 * re-enable the login form (AC-3).
 *
 * Edge case: login attempts during lockout return the same message; the timer
 * does NOT reset because the parent's lockedUntil value is fixed.
 *
 * UXR-601: Actionable error — includes "Contact support" link.
 */

import { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';

interface Props {
  /** UTC Date until which the account is locked. */
  lockedUntil: Date;
  /** Called when the countdown reaches zero. */
  onExpired: () => void;
}

function formatCountdown(ms: number): string {
  const totalSeconds = Math.max(0, Math.ceil(ms / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
}

export default function AccountLockoutAlert({ lockedUntil, onExpired }: Props) {
  const [remaining, setRemaining] = useState(() => lockedUntil.getTime() - Date.now());
  const onExpiredRef = useRef(onExpired);
  onExpiredRef.current = onExpired;

  useEffect(() => {
    // Immediately expired — notify parent without starting a timer.
    if (lockedUntil.getTime() <= Date.now()) {
      onExpiredRef.current();
      return;
    }

    const tick = () => {
      const ms = lockedUntil.getTime() - Date.now();
      if (ms <= 0) {
        setRemaining(0);
        onExpiredRef.current();
        return;
      }
      setRemaining(ms);
    };

    tick(); // run immediately to avoid 1-second flicker on mount
    const id = setInterval(tick, 1000);
    return () => clearInterval(id);
  }, [lockedUntil]);

  const countdownText = formatCountdown(remaining);

  return (
    <Alert
      severity="error"
      role="alert"
      aria-live="assertive"
      sx={{ mb: 2 }}
    >
      <Typography variant="body2" component="span">
        Account locked. Try again in{' '}
        <Typography
          component="span"
          variant="body2"
          fontWeight={700}
          aria-label={`${countdownText} remaining`}
          aria-live="off"
        >
          {countdownText}
        </Typography>
        .{' '}
        <Link
          href="mailto:support@upacip.health"
          underline="always"
          color="inherit"
          fontSize="inherit"
        >
          Contact support if you need immediate access.
        </Link>
      </Typography>
    </Alert>
  );
}
