/**
 * MfaTotpStep — 6-digit TOTP input step (US_016, AC-1).
 *
 * Displayed on the login page after valid credentials are submitted for an
 * MFA-enabled staff/admin user. Auto-submits when all 6 digits are entered.
 * Supports switching to a backup code input.
 *
 * API: POST /api/auth/mfa/verify
 *   Request:  { tempToken: string; totpCode: string }
 *   Response: { accessToken: string; lastLogin?: LastLogin | null }
 *
 * UXR-201: Keyboard-navigable numeric input, clear error state.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import LockClockOutlinedIcon from '@mui/icons-material/LockClockOutlined';
import { ApiError, apiPost } from '@/lib/apiClient';
import { type LastLogin } from '@/hooks/useAuth';

interface MfaVerifyResponse {
  accessToken: string;
  lastLogin?: LastLogin | null;
}

interface Props {
  /** Temporary token returned by the login endpoint when MFA is required. */
  tempToken: string;
  /** Called after successful TOTP verification with the full access token. */
  onVerified: (accessToken: string, lastLogin: LastLogin | null) => void;
  /** Cancel and return to the credential input step. */
  onCancel: () => void;
}

type InputMode = 'totp' | 'backup';

export default function MfaTotpStep({ tempToken, onVerified, onCancel }: Props) {
  const [inputMode, setInputMode] = useState<InputMode>('totp');
  const [code, setCode] = useState('');
  const [isVerifying, setIsVerifying] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const inputRef = useRef<HTMLInputElement>(null);

  // Auto-focus the input on mount and when mode switches.
  useEffect(() => {
    inputRef.current?.focus();
  }, [inputMode]);

  const verify = useCallback(
    async (value: string) => {
      if (isVerifying) return;
      setError(null);
      setIsVerifying(true);

      try {
        const data = await apiPost<MfaVerifyResponse>('/api/auth/mfa/verify', {
          tempToken,
          totpCode: value,
        });
        onVerified(data.accessToken, data.lastLogin ?? null);
      } catch (err) {
        if (err instanceof ApiError) {
          if (err.status === 429) {
            setError('Too many attempts. Please wait before trying again.');
          } else {
            // Try to parse remaining attempts from the JSON body.
            let attempts: number | null = null;
            try {
              const body = JSON.parse(err.message) as { remainingAttempts?: number };
              attempts = body.remainingAttempts ?? null;
            } catch {
              // not JSON
            }
            setError(
              attempts !== null
                ? `Invalid code. ${attempts} attempt${attempts === 1 ? '' : 's'} remaining.`
                : 'Invalid code. Please try again.',
            );
          }
        } else {
          setError('Verification failed. Please try again.');
        }
        setCode('');
        inputRef.current?.focus();
      } finally {
        setIsVerifying(false);
      }
    },
    [isVerifying, onVerified, tempToken],
  );

  const handleChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const maxLen = inputMode === 'totp' ? 6 : 8;
      // Allow only alphanumeric characters; TOTP is numeric, backup codes may be alphanumeric.
      const raw = inputMode === 'totp'
        ? e.target.value.replace(/\D/g, '').slice(0, maxLen)
        : e.target.value.replace(/[^A-Za-z0-9]/g, '').slice(0, maxLen);

      setCode(raw);
      setError(null);

      // Auto-submit when full length is reached.
      if (raw.length === maxLen) {
        void verify(raw);
      }
    },
    [inputMode, verify],
  );

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
      const maxLen = inputMode === 'totp' ? 6 : 8;
      if (code.length < maxLen) return;
      void verify(code);
    },
    [code, inputMode, verify],
  );

  const switchToBackup = useCallback(() => {
    setInputMode('backup');
    setCode('');
    setError(null);
    setRemainingAttempts(null);
  }, []);

  const switchToTotp = useCallback(() => {
    setInputMode('totp');
    setCode('');
    setError(null);
    setRemainingAttempts(null);
  }, []);

  const isTotp = inputMode === 'totp';
  const maxLen = isTotp ? 6 : 8;

  return (
    <Box
      component="form"
      aria-label="Two-factor authentication form"
      onSubmit={(e) => void handleSubmit(e)}
    >
      <Box sx={{ display: 'flex', justifyContent: 'center', mb: 2 }}>
        <LockClockOutlinedIcon sx={{ fontSize: 40, color: 'primary.main' }} aria-hidden="true" />
      </Box>

      <Typography variant="h5" fontWeight={400} mb={1} textAlign="center">
        Two-Factor Authentication
      </Typography>
      <Typography variant="body2" color="text.secondary" mb={3} textAlign="center">
        {isTotp
          ? 'Enter the 6-digit code from your authenticator app.'
          : 'Enter one of your 8-character backup codes.'}
      </Typography>

      {error && (
        <Alert severity="error" role="alert" sx={{ mb: 2 }}>
          {error}
        </Alert>
      )}

      <TextField
        inputRef={inputRef}
        label={isTotp ? '6-digit code' : 'Backup code'}
        value={code}
        onChange={handleChange}
        inputProps={{
          inputMode: isTotp ? 'numeric' : 'text',
          maxLength: maxLen,
          autoComplete: isTotp ? 'one-time-code' : 'off',
          'aria-label': isTotp ? '6-digit TOTP code' : '8-character backup code',
          pattern: isTotp ? '[0-9]*' : '[A-Za-z0-9]*',
        }}
        disabled={isVerifying}
        fullWidth
        placeholder={isTotp ? '••••••' : '••••••••'}
        sx={{ mb: 2, '& input': { letterSpacing: '0.3em', textAlign: 'center', fontSize: '1.25rem' } }}
      />

      <Stack spacing={1.5}>
        <Button
          type="submit"
          variant="contained"
          fullWidth
          disabled={code.length < maxLen || isVerifying}
          startIcon={isVerifying ? <CircularProgress size={16} color="inherit" /> : undefined}
        >
          {isVerifying ? 'Verifying…' : 'Verify'}
        </Button>

        <Box sx={{ display: 'flex', justifyContent: 'space-between' }}>
          {isTotp ? (
            <Button
              variant="text"
              size="small"
              onClick={switchToBackup}
              aria-label="Use backup code instead"
            >
              Use backup code
            </Button>
          ) : (
            <Button
              variant="text"
              size="small"
              onClick={switchToTotp}
              aria-label="Use authenticator app instead"
            >
              Use authenticator app
            </Button>
          )}

          <Button
            variant="text"
            size="small"
            color="inherit"
            onClick={onCancel}
            sx={{ color: 'text.secondary' }}
            aria-label="Cancel and return to sign in"
          >
            Cancel
          </Button>
        </Box>
      </Stack>
    </Box>
  );
}
