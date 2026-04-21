/**
 * ResetPasswordPage — SCR-004 View 2
 *
 * Reached via URL /reset-password?token={token}&email={email} from the password reset email.
 * Presents a new-password form with PasswordStrengthIndicator (reused from US_012)
 * and confirm-password field with match validation.
 *
 * States: Default | Validation | Loading | Success | Error (expired) | Error (invalid) | Error (server)
 *
 * On mount: validates token + email presence in URL; redirects to /forgot-password if missing.
 * On 410 (expired): shows "Link expired" message with resend option (AC-3).
 * On 400 (invalid): shows "Invalid reset link" with resend option.
 * On 200: shows success confirmation + "Sign In" link (AC-4).
 */

import { useCallback, useEffect, useState } from 'react';
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import Paper from '@mui/material/Paper';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import Visibility from '@mui/icons-material/Visibility';
import VisibilityOff from '@mui/icons-material/VisibilityOff';
import PasswordStrengthIndicator from '@/components/PasswordStrengthIndicator';
import { useResetPassword } from '@/hooks/useResetPassword';
import { ApiError } from '@/lib/apiClient';
import { resetPasswordSchema } from '@/validation/resetPasswordSchema';

type ScreenState = 'default' | 'loading' | 'success' | 'expired' | 'invalid' | 'error';

export default function ResetPasswordPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();

  const token = searchParams.get('token') ?? '';
  const email = searchParams.get('email') ?? '';

  // Redirect if token or email is missing from URL
  useEffect(() => {
    if (!token || !email) {
      navigate('/forgot-password', { replace: true });
    }
  }, [token, email, navigate]);

  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [showNew, setShowNew] = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);
  const [newPasswordError, setNewPasswordError] = useState<string | null>(null);
  const [confirmPasswordError, setConfirmPasswordError] = useState<string | null>(null);
  const [screenState, setScreenState] = useState<ScreenState>('default');
  const [serverError, setServerError] = useState<string | null>(null);

  const mutation = useResetPassword();

  // Inline validation within 200ms on blur (UXR-501)
  const handleNewPasswordBlur = useCallback(() => {
    const result = resetPasswordSchema.shape.newPassword.safeParse(newPassword);
    setNewPasswordError(result.success ? null : result.error.errors[0].message);
  }, [newPassword]);

  const handleConfirmPasswordBlur = useCallback(() => {
    if (confirmPassword && confirmPassword !== newPassword) {
      setConfirmPasswordError('Passwords do not match');
    } else {
      setConfirmPasswordError(null);
    }
  }, [confirmPassword, newPassword]);

  const handleNewPasswordChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setNewPassword(e.target.value);
      if (newPasswordError) setNewPasswordError(null);
    },
    [newPasswordError],
  );

  const handleConfirmPasswordChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setConfirmPassword(e.target.value);
      if (confirmPasswordError) setConfirmPasswordError(null);
    },
    [confirmPasswordError],
  );

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();

      const result = resetPasswordSchema.safeParse({ newPassword, confirmPassword });
      if (!result.success) {
        const fieldErrors = result.error.flatten().fieldErrors;
        setNewPasswordError(fieldErrors.newPassword?.[0] ?? null);
        setConfirmPasswordError(fieldErrors.confirmPassword?.[0] ?? null);
        return;
      }

      setScreenState('loading');
      setServerError(null);

      mutation.mutate(
        { token, email, newPassword },
        {
          onSuccess: () => setScreenState('success'),
          onError: (error) => {
            if (error instanceof ApiError) {
              if (error.status === 410) {
                setScreenState('expired');
              } else if (error.status === 400) {
                setScreenState('invalid');
              } else {
                setServerError('Something went wrong. Please try again.');
                setScreenState('error');
              }
            } else {
              setServerError('Something went wrong. Please try again.');
              setScreenState('error');
            }
          },
        },
      );
    },
    [newPassword, confirmPassword, token, email, mutation],
  );

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        bgcolor: '#F5F5F5', // neutral-100
        p: 3,
      }}
    >
      <Paper
        component="main"
        sx={{
          width: '100%',
          maxWidth: 440,
          p: { xs: 3, sm: 4 },
          borderRadius: 2,
          boxShadow: 1,
          bgcolor: '#FFFFFF', // neutral-0
          textAlign: 'center',
        }}
      >
        {screenState === 'success' && <SuccessState />}
        {screenState === 'expired' && <ExpiredState />}
        {screenState === 'invalid' && <InvalidState />}

        {(screenState === 'default' ||
          screenState === 'loading' ||
          screenState === 'error') && (
          <>
            <LockOutlinedIcon
              sx={{ fontSize: 48, color: 'primary.main', mb: 2 }}
              aria-hidden="true"
            />

            <Typography
              variant="h2"
              component="h1"
              sx={{ mb: 1, fontSize: '1.5rem', fontWeight: 400 }}
            >
              Set your new password
            </Typography>

            <Typography variant="body2" sx={{ color: '#757575', mb: 3 }}>
              Choose a strong password to secure your account.
            </Typography>

            {screenState === 'error' && serverError && (
              <Alert severity="error" sx={{ mb: 2, textAlign: 'left' }} role="alert">
                {serverError}
              </Alert>
            )}

            <Box
              component="form"
              onSubmit={handleSubmit}
              noValidate
              aria-label="Set new password form"
              sx={{ textAlign: 'left' }}
            >
              <TextField
                id="new-password"
                label="New Password"
                type={showNew ? 'text' : 'password'}
                value={newPassword}
                onChange={handleNewPasswordChange}
                onBlur={handleNewPasswordBlur}
                error={Boolean(newPasswordError)}
                helperText={newPasswordError ?? ' '}
                fullWidth
                required
                autoComplete="new-password"
                inputProps={{ 'aria-required': true }}
                disabled={screenState === 'loading'}
                InputProps={{
                  endAdornment: (
                    <InputAdornment position="end">
                      <IconButton
                        onClick={() => setShowNew((v) => !v)}
                        edge="end"
                        aria-label={showNew ? 'Hide password' : 'Show password'}
                        size="small"
                      >
                        {showNew ? <VisibilityOff /> : <Visibility />}
                      </IconButton>
                    </InputAdornment>
                  ),
                }}
              />

              {/* Password strength indicator — reused from US_012 */}
              <Box sx={{ mb: 2 }}>
                <PasswordStrengthIndicator password={newPassword} />
              </Box>

              <TextField
                id="confirm-password"
                label="Confirm New Password"
                type={showConfirm ? 'text' : 'password'}
                value={confirmPassword}
                onChange={handleConfirmPasswordChange}
                onBlur={handleConfirmPasswordBlur}
                error={Boolean(confirmPasswordError)}
                helperText={confirmPasswordError ?? ' '}
                fullWidth
                required
                autoComplete="new-password"
                inputProps={{ 'aria-required': true }}
                sx={{ mb: 2 }}
                disabled={screenState === 'loading'}
                InputProps={{
                  endAdornment: (
                    <InputAdornment position="end">
                      <IconButton
                        onClick={() => setShowConfirm((v) => !v)}
                        edge="end"
                        aria-label={showConfirm ? 'Hide confirm password' : 'Show confirm password'}
                        size="small"
                      >
                        {showConfirm ? <VisibilityOff /> : <Visibility />}
                      </IconButton>
                    </InputAdornment>
                  ),
                }}
              />

              <Button
                type="submit"
                variant="contained"
                fullWidth
                disabled={screenState === 'loading'}
                sx={{ mb: 2, py: 1.5 }}
                aria-label={screenState === 'loading' ? 'Resetting password' : 'Reset password'}
              >
                {screenState === 'loading' ? (
                  <>
                    <CircularProgress size={18} color="inherit" sx={{ mr: 1 }} />
                    Resetting...
                  </>
                ) : (
                  'Reset Password'
                )}
              </Button>

              <Typography variant="body2" sx={{ textAlign: 'center' }}>
                <RouterLink
                  to="/login"
                  style={{ color: '#1976D2', textDecoration: 'none' }}
                  aria-label="Back to sign in"
                >
                  ← Back to Sign In
                </RouterLink>
              </Typography>
            </Box>
          </>
        )}
      </Paper>
    </Box>
  );
}

// ─── Terminal states ──────────────────────────────────────────────────────────

function SuccessState() {
  return (
    <Box role="status" aria-live="polite" sx={{ py: 2 }}>
      <LockOutlinedIcon sx={{ fontSize: 48, color: 'success.main', mb: 2 }} aria-hidden="true" />
      <Typography variant="h2" component="h1" sx={{ mb: 2, fontSize: '1.5rem', fontWeight: 400 }}>
        Password reset successfully!
      </Typography>
      <Typography variant="body2" sx={{ color: '#757575', mb: 3 }}>
        You can now sign in with your new password.
      </Typography>
      <Button
        component={RouterLink}
        to="/login"
        variant="contained"
        fullWidth
        sx={{ py: 1.5 }}
      >
        Sign In
      </Button>
    </Box>
  );
}

/** AC-3 — Reset link expired (HTTP 410) */
function ExpiredState() {
  return (
    <Box role="alert" aria-live="assertive" sx={{ py: 2 }}>
      <LockOutlinedIcon sx={{ fontSize: 48, color: 'error.main', mb: 2 }} aria-hidden="true" />
      <Typography variant="h2" component="h1" sx={{ mb: 2, fontSize: '1.5rem', fontWeight: 400 }}>
        Link expired
      </Typography>
      <Typography variant="body2" sx={{ color: '#757575', mb: 3 }}>
        This reset link is no longer valid. Links expire after 1 hour for security. Request
        a new one to continue.
      </Typography>
      <Button
        component={RouterLink}
        to="/forgot-password"
        variant="contained"
        fullWidth
        sx={{ py: 1.5 }}
      >
        Request New Reset Link
      </Button>
    </Box>
  );
}

/** Invalid token (HTTP 400) */
function InvalidState() {
  return (
    <Box role="alert" aria-live="assertive" sx={{ py: 2 }}>
      <LockOutlinedIcon sx={{ fontSize: 48, color: 'error.main', mb: 2 }} aria-hidden="true" />
      <Typography variant="h2" component="h1" sx={{ mb: 2, fontSize: '1.5rem', fontWeight: 400 }}>
        Invalid reset link
      </Typography>
      <Typography variant="body2" sx={{ color: '#757575', mb: 3 }}>
        This reset link is invalid. It may have already been used. Request a new one to
        continue.
      </Typography>
      <Button
        component={RouterLink}
        to="/forgot-password"
        variant="contained"
        fullWidth
        sx={{ py: 1.5 }}
      >
        Request New Reset Link
      </Button>
    </Box>
  );
}
