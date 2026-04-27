/**
 * SessionTimeoutProvider — wraps authenticated routes.
 *
 * - Activates timer only when user is authenticated.
 * - Exposes { resetTimer } via SessionTimeoutContext for the API interceptor.
 * - Renders SessionTimeoutModal when inactivity >= 13 min.
 * - Navigates to /login?expired=true on invalidation.
 *
 * AC-1: redirect to login with expired flag
 * AC-2: any activity or 2xx API call resets timer
 * AC-4: modal with 120-second countdown at 13-min mark
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuthStore } from '@/hooks/useAuth';
import { useSessionTimeout } from '@/hooks/useSessionTimeout';
import {
  apiPost,
  registerActivityReset,
  registerSessionInvalidate,
  registerSessionTerminated,
  unregisterActivityReset,
  unregisterSessionInvalidate,
  unregisterSessionTerminated,
} from '@/lib/apiClient';
import SessionTimeoutModal from '@/components/SessionTimeoutModal';

// ─── Context ─────────────────────────────────────────────────────────────────

interface SessionTimeoutContextValue {
  /** Call on every successful 2xx API response to reset the inactivity timer (AC-2) */
  resetTimer: () => void;
}

const SessionTimeoutContext = createContext<SessionTimeoutContextValue>({
  resetTimer: () => {},
});

// eslint-disable-next-line react-refresh/only-export-components -- standard context hook pattern
export function useSessionTimeoutContext(): SessionTimeoutContextValue {
  return useContext(SessionTimeoutContext);
}

// ─── Provider ────────────────────────────────────────────────────────────────

interface SessionTimeoutProviderProps {
  children: ReactNode;
}

export function SessionTimeoutProvider({ children }: SessionTimeoutProviderProps) {
  const navigate = useNavigate();
  const accessToken = useAuthStore((s) => s.accessToken);
  const clearAuth = useAuthStore((s) => s.clearAuth);

  const [showWarning, setShowWarning] = useState(false);

  const invalidateSession = useCallback(() => {
    setShowWarning(false);
    clearAuth();
    // Best-effort server-side logout — don't block navigation on failure
    apiPost('/api/auth/logout', {}).catch(() => {});
    navigate('/login?expired=true', { replace: true });
  }, [clearAuth, navigate]);

  // Handle concurrent session termination (US_065 AC-2, 440 response).
  // Stops timers, clears auth, and navigates to login with ?reason=session_terminated
  // so SessionTerminatedAlert is displayed.
  const handleSessionTerminated = useCallback(() => {
    setShowWarning(false);
    clearAuth();
    navigate('/login?reason=session_terminated', { replace: true });
  }, [clearAuth, navigate]);

  const handleWarn = useCallback(() => {
    setShowWarning(true);
  }, []);

  const { resetTimer } = useSessionTimeout({
    enabled: !!accessToken,
    onWarn: handleWarn,
    onExpire: invalidateSession,
    isWarningVisible: showWarning,
  });

  // Register resetTimer, invalidateSession, and sessionTerminated with apiClient interceptors
  useEffect(() => {
    registerActivityReset(resetTimer);
    registerSessionInvalidate(invalidateSession);
    registerSessionTerminated(handleSessionTerminated);
    return () => {
      unregisterActivityReset();
      unregisterSessionInvalidate();
      unregisterSessionTerminated();
    };
  }, [resetTimer, invalidateSession, handleSessionTerminated]);

  // "Extend Session" flow — POST /api/session/extend (US_065 AC-4)
  // Throws so SessionTimeoutModal can distinguish network errors from 401 responses.
  const handleExtend = useCallback(async () => {
    const data = await apiPost<{ accessToken: string }>('/api/session/extend', {});
    useAuthStore.getState().setTokens(data.accessToken);
    resetTimer();
    setShowWarning(false);
  }, [resetTimer]);

  const contextValue = useMemo(() => ({ resetTimer }), [resetTimer]);

  return (
    <SessionTimeoutContext.Provider value={contextValue}>
      {children}
      <SessionTimeoutModal
        open={showWarning}
        onExtend={handleExtend}
        onLogout={invalidateSession}
      />
    </SessionTimeoutContext.Provider>
  );
}
