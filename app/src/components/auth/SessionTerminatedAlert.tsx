/**
 * SessionTerminatedAlert — US_065 AC-2, UXR-603
 *
 * Displayed on the login page when `?reason=session_terminated` is present in the URL,
 * indicating the user's session was ended because their account signed in on another device.
 *
 * Behaviour:
 *   - Auto-dismisses after 30 seconds.
 *   - Dismiss button clears the alert AND removes the `reason` query parameter from the URL.
 *   - Accessible: role="alert", aria-live="assertive" per WCAG 2.1 AA.
 *   - Responsive: full-width on mobile (xs), max-width 480 px centred on larger viewports.
 *
 * Design tokens: warning palette from MUI theme (maps to designsystem.md#colors warning).
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Box from '@mui/material/Box';
import Collapse from '@mui/material/Collapse';
import Typography from '@mui/material/Typography';

const AUTO_DISMISS_MS = 30_000;

export default function SessionTerminatedAlert() {
  const [searchParams, setSearchParams] = useSearchParams();
  const reason = searchParams.get('reason');
  const isTerminated = reason === 'session_terminated';

  const [visible, setVisible] = useState(isTerminated);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Start auto-dismiss timer when the alert is shown.
  useEffect(() => {
    if (!isTerminated) return;
    setVisible(true);

    timerRef.current = setTimeout(() => {
      setVisible(false);
    }, AUTO_DISMISS_MS);

    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [isTerminated]);

  const handleDismiss = useCallback(() => {
    if (timerRef.current) clearTimeout(timerRef.current);
    setVisible(false);
    // Remove ?reason=session_terminated from the URL so refresh does not re-show the alert.
    setSearchParams(
      (prev) => {
        prev.delete('reason');
        return prev;
      },
      { replace: true },
    );
    // Clear the sessionStorage marker set by apiClient on 440 response.
    sessionStorage.removeItem('sessionTerminatedAt');
  }, [setSearchParams]);

  if (!isTerminated) return null;

  return (
    <Box
      sx={{
        width: '100%',
        maxWidth: { xs: '100%', sm: 480 },
        mx: 'auto',
        mb: 2,
      }}
    >
      <Collapse in={visible} onExited={handleDismiss}>
        <Alert
          severity="warning"
          role="alert"
          aria-live="assertive"
          onClose={handleDismiss}
        >
          <AlertTitle>
            <Typography variant="body1" component="span" fontWeight={700}>
              Session Terminated
            </Typography>
          </AlertTitle>
          Your session was ended because your account signed in from another device. If this
          wasn&apos;t you, please change your password immediately.
        </Alert>
      </Collapse>
    </Box>
  );
}
