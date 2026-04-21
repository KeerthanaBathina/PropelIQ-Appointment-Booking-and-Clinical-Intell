/**
 * useLogin — manages the complete login flow (US_016).
 *
 * Handles four backend response shapes:
 *   200 + mfaRequired: true  → credential pass, MFA step needed
 *   200 + accessToken        → full login success (stores token + lastLogin)
 *   423 Locked               → account locked (parses lockedUntil timestamp)
 *   401 Invalid              → invalid credentials (parses remainingAttempts)
 *
 * Uses a raw fetch call (not apiPost) for the login endpoint to avoid the
 * generic 401 → session-expired redirect that apiPost triggers for
 * unauthenticated 401 responses (login 401 = wrong password, not expired session).
 */

import { useCallback, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuthStore, type LastLogin } from '@/hooks/useAuth';

const BASE_URL = import.meta.env.VITE_API_BASE_URL as string;

export type LoginStep = 'credentials' | 'mfa';

// ─── Response shapes ──────────────────────────────────────────────────────────

interface LoginSuccessResponse {
  accessToken: string;
  mfaRequired?: false;
  lastLogin?: LastLogin | null;
}

interface LoginMfaRequiredResponse {
  mfaRequired: true;
  mfaTempToken: string;
}

type LoginResponse = LoginSuccessResponse | LoginMfaRequiredResponse;

interface LoginErrorBody {
  message?: string;
  lockedUntil?: string;
  remainingAttempts?: number;
}

// ─── Hook state ───────────────────────────────────────────────────────────────

interface UseLoginReturn {
  /** Current step of the login flow. */
  step: LoginStep;
  /** Temporary token returned when MFA is required. Passed to MfaTotpStep. */
  mfaTempToken: string | null;
  /** ISO timestamp until which the account is locked. */
  lockedUntil: Date | null;
  /** Number of remaining credential attempts before lockout. Null when unknown. */
  remainingAttempts: number | null;
  /** Error message to display (credential failure, server error). */
  error: string | null;
  /** Whether the credential submission is in-flight. */
  isSubmitting: boolean;
  /** Submit credentials — step 1 of login. */
  submitCredentials: (email: string, password: string) => Promise<void>;
  /** Called by MfaTotpStep on successful MFA verification. */
  onMfaSuccess: (accessToken: string, lastLogin: LastLogin | null) => void;
  /** Cancel MFA step and return to credential input. */
  cancelMfa: () => void;
  /** Clear lockout state (called when countdown expires). */
  clearLockout: () => void;
}

export function useLogin(): UseLoginReturn {
  const navigate = useNavigate();
  const setTokens = useAuthStore((s) => s.setTokens);
  const setLastLogin = useAuthStore((s) => s.setLastLogin);

  const [step, setStep] = useState<LoginStep>('credentials');
  const [mfaTempToken, setMfaTempToken] = useState<string | null>(null);
  const [lockedUntil, setLockedUntil] = useState<Date | null>(null);
  const [remainingAttempts, setRemainingAttempts] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const submitCredentials = useCallback(
    async (email: string, password: string) => {
      setError(null);
      setRemainingAttempts(null);
      setIsSubmitting(true);

      try {
        const response = await fetch(`${BASE_URL}/api/auth/login`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ email, password }),
          credentials: 'include', // required for HttpOnly refresh-token cookie
        });

        const text = await response.text().catch(() => '');
        let body: unknown = null;
        try {
          body = JSON.parse(text);
        } catch {
          // non-JSON response — handled below
        }

        if (response.ok) {
          const data = body as LoginResponse;

          if ('mfaRequired' in data && data.mfaRequired === true) {
            // Credential check passed; MFA is required before full access.
            setMfaTempToken(data.mfaTempToken);
            setStep('mfa');
            return;
          }

          // Full login success.
          const successData = data as LoginSuccessResponse;
          setTokens(successData.accessToken);
          setLastLogin(successData.lastLogin ?? null);
          navigate('/dashboard', { replace: true });
          return;
        }

        // Non-2xx responses ─────────────────────────────────────────────────

        const errBody = (body ?? {}) as LoginErrorBody;

        if (response.status === 423) {
          // Account locked (AC-2).
          const until = errBody.lockedUntil ? new Date(errBody.lockedUntil) : null;
          setLockedUntil(until);
          return;
        }

        if (response.status === 401) {
          // Invalid credentials. Show remaining-attempts hint if provided.
          if (typeof errBody.remainingAttempts === 'number') {
            setRemainingAttempts(errBody.remainingAttempts);
            setError(
              `Invalid email or password. ${errBody.remainingAttempts} attempt${errBody.remainingAttempts === 1 ? '' : 's'} remaining before lockout.`,
            );
          } else {
            setError('Invalid email or password. Please try again.');
          }
          return;
        }

        // Generic server error.
        setError(errBody.message ?? 'An unexpected error occurred. Please try again.');
      } catch {
        setError('Unable to connect. Please check your connection and try again.');
      } finally {
        setIsSubmitting(false);
      }
    },
    [navigate, setLastLogin, setTokens],
  );

  const onMfaSuccess = useCallback(
    (accessToken: string, lastLogin: LastLogin | null) => {
      setTokens(accessToken);
      setLastLogin(lastLogin);
      navigate('/dashboard', { replace: true });
    },
    [navigate, setLastLogin, setTokens],
  );

  const cancelMfa = useCallback(() => {
    setStep('credentials');
    setMfaTempToken(null);
    setError(null);
  }, []);

  const clearLockout = useCallback(() => {
    setLockedUntil(null);
  }, []);

  return {
    step,
    mfaTempToken,
    lockedUntil,
    remainingAttempts,
    error,
    isSubmitting,
    submitCredentials,
    onMfaSuccess,
    cancelMfa,
    clearLockout,
  };
}
