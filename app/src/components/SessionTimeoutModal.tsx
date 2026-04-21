/**
 * SessionTimeoutModal — UXR-603
 *
 * Non-dismissible alertdialog shown 2 minutes before session expiry.
 * Countdown: 120 → 0 seconds.
 * Primary: "Extend Session" → POST /api/auth/extend-session
 * Secondary: "Logout Now" → immediate invalidation
 * At 0: auto-invalidates without user input.
 *
 * Accessibility: role="alertdialog", aria-modal, focus trap (MUI Dialog default),
 *                aria-live="polite" for countdown (WCAG 2.2.1)
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';

const COUNTDOWN_START = 120; // seconds

interface SessionTimeoutModalProps {
  open: boolean;
  onExtend: () => Promise<void>;
  onLogout: () => void;
}

export default function SessionTimeoutModal({
  open,
  onExtend,
  onLogout,
}: SessionTimeoutModalProps) {
  const [countdown, setCountdown] = useState<number>(COUNTDOWN_START);
  const [extending, setExtending] = useState<boolean>(false);
  const [extendError, setExtendError] = useState<string | null>(null);

  // Reset countdown whenever the modal opens
  useEffect(() => {
    if (open) {
      setCountdown(COUNTDOWN_START);
      setExtendError(null);
    }
  }, [open]);

  // Countdown interval — 1 tick per second
  const onExpireRef = useRef(onLogout);
  onExpireRef.current = onLogout;

  useEffect(() => {
    if (!open) return;

    const id = setInterval(() => {
      setCountdown((prev) => {
        if (prev <= 1) {
          clearInterval(id);
          // Auto-expire: call outside state setter to avoid stale closure
          setTimeout(() => onExpireRef.current(), 0);
          return 0;
        }
        return prev - 1;
      });
    }, 1_000);

    return () => clearInterval(id);
  }, [open]);

  const handleExtend = useCallback(async () => {
    setExtending(true);
    setExtendError(null);
    try {
      await onExtend();
      // Modal is closed by the provider after successful extend
    } catch {
      setExtendError('Unable to extend session. Please try again or log out.');
    } finally {
      setExtending(false);
    }
  }, [onExtend]);

  return (
    <Dialog
      open={open}
      // Non-dismissible: disable backdrop click and ESC key (UXR-603)
      onClose={() => {}}
      disableEscapeKeyDown
      aria-modal="true"
      aria-labelledby="session-timeout-title"
      aria-describedby="session-timeout-desc"
      PaperProps={{ role: 'alertdialog' }}
      maxWidth="xs"
      fullWidth
    >
      <DialogTitle id="session-timeout-title" component="h3">
        Session Expiring Soon
      </DialogTitle>

      <DialogContent>
        <Stack spacing={2}>
          <Typography id="session-timeout-desc" variant="body1">
            Your session will expire due to inactivity.
          </Typography>

          {/* Live countdown region (WCAG 2.2.1) */}
          <Typography
            variant="h4"
            component="p"
            align="center"
            aria-live="polite"
            aria-atomic="true"
            sx={{ fontVariantNumeric: 'tabular-nums', color: countdown <= 30 ? 'error.main' : 'text.primary' }}
          >
            {countdown}s
          </Typography>

          <Typography variant="body2" color="text.secondary" align="center">
            Click <strong>Extend Session</strong> to stay logged in.
          </Typography>

          {extendError && (
            <Alert severity="error" sx={{ mt: 1 }}>
              {extendError}
            </Alert>
          )}
        </Stack>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2, gap: 1 }}>
        <Button
          variant="outlined"
          color="inherit"
          onClick={onLogout}
          disabled={extending}
        >
          Logout Now
        </Button>

        <Button
          variant="contained"
          color="primary"
          onClick={() => void handleExtend()}
          disabled={extending}
          startIcon={extending ? <CircularProgress size={16} color="inherit" /> : undefined}
          autoFocus
        >
          {extending ? 'Extending…' : 'Extend Session'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
