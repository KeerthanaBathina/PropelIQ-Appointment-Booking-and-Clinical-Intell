/**
 * LastLoginBanner — displays previous login info on dashboard pages (US_016, AC-4).
 *
 * Reads lastLogin from Zustand auth state. Renders nothing if lastLogin is null
 * (graceful skip on first-ever login). Auto-dismisses after 10 seconds.
 *
 * Format: "Last login: Apr 16, 2026, 3:45 PM from 192.168.1.1"
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Collapse from '@mui/material/Collapse';
import { useAuthStore } from '@/hooks/useAuth';

export default function LastLoginBanner() {
  const lastLogin = useAuthStore((s) => s.lastLogin);
  const [visible, setVisible] = useState(true);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Auto-dismiss after 10 seconds (AC-4).
  useEffect(() => {
    if (!lastLogin) return;
    timerRef.current = setTimeout(() => setVisible(false), 10_000);
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, [lastLogin]);

  const handleDismiss = useCallback(() => {
    if (timerRef.current) clearTimeout(timerRef.current);
    setVisible(false);
  }, []);

  if (!lastLogin) return null;

  const formattedTimestamp = new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(lastLogin.timestamp));

  return (
    <Collapse in={visible}>
      <Alert
        severity="info"
        onClose={handleDismiss}
        role="status"
        aria-label="Last login information"
        sx={{ mb: 2, borderRadius: 0 }}
      >
        Last login: {formattedTimestamp} from {lastLogin.ipAddress}
      </Alert>
    </Collapse>
  );
}
