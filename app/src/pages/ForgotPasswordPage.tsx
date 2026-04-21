/**
 * ForgotPasswordPage — SCR-004 View 1
 *
 * Wireframe: .propel/context/wireframes/Hi-Fi/wireframe-SCR-004-password-reset.html
 * Centered card (max-width 440px) on neutral-100 shell, shadow-1, padding sp-8.
 * Lock icon (primary-500), "Reset your password" h2, description body2, email field,
 * "Send Reset Link" primary button (full width), "← Back to Sign In" link.
 *
 * States: Default | Validation | Loading | Success | Error
 * Anti-enumeration: always shows the same success message regardless of email registration.
 */

import { useCallback, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Paper from '@mui/material/Paper';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import { useForgotPassword } from '@/hooks/useForgotPassword';
import { ApiError } from '@/lib/apiClient';
import { forgotPasswordSchema } from '@/validation/resetPasswordSchema';

type ScreenState = 'default' | 'loading' | 'success' | 'error';

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [emailError, setEmailError] = useState<string | null>(null);
  const [screenState, setScreenState] = useState<ScreenState>('default');
  const [serverError, setServerError] = useState<string | null>(null);

  const mutation = useForgotPassword();

  // Inline validation within 200ms on blur (UXR-501)
  const handleEmailBlur = useCallback(() => {
    const result = forgotPasswordSchema.shape.email.safeParse(email);
    setEmailError(result.success ? null : result.error.errors[0].message);
  }, [email]);

  const handleEmailChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      setEmail(e.target.value);
      // Clear error when user starts correcting
      if (emailError) setEmailError(null);
    },
    [emailError],
  );

  const handleSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();

      // Validate before submitting
      const result = forgotPasswordSchema.safeParse({ email });
      if (!result.success) {
        setEmailError(result.error.errors[0].message);
        return;
      }

      setScreenState('loading');
      setServerError(null);

      mutation.mutate(
        { email },
        {
          onSuccess: () => {
            // Always show success regardless of whether email is registered (anti-enumeration)
            setScreenState('success');
          },
          onError: (error) => {
            if (error instanceof ApiError && error.status === 429) {
              setServerError('Too many requests. Please wait a moment and try again.');
            } else {
              setServerError('Something went wrong. Please try again.');
            }
            setScreenState('error');
          },
        },
      );
    },
    [email, mutation],
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
          p: { xs: 3, sm: 4 }, // sp-8 = 4 * 8px = 32px
          borderRadius: 2,
          boxShadow: 1,
          bgcolor: '#FFFFFF', // neutral-0
          textAlign: 'center',
        }}
      >
        {screenState === 'success' ? (
          <SuccessState />
        ) : (
          <>
            {/* Lock icon — primary-500 */}
            <LockOutlinedIcon
              sx={{ fontSize: 48, color: 'primary.main', mb: 2 }}
              aria-hidden="true"
            />

            <Typography
              variant="h2"
              component="h1"
              sx={{ mb: 1, fontSize: '1.5rem', fontWeight: 400 }}
            >
              Reset your password
            </Typography>

            <Typography
              variant="body2"
              sx={{ color: '#757575', mb: 3 }} // neutral-600
            >
              Enter your email address and we'll send you a link to reset your password.
            </Typography>

            {screenState === 'error' && serverError && (
              <Alert
                severity="error"
                sx={{ mb: 2, textAlign: 'left' }}
                role="alert"
              >
                {serverError}
              </Alert>
            )}

            <Box
              component="form"
              onSubmit={handleSubmit}
              noValidate
              aria-label="Password reset form"
              sx={{ textAlign: 'left' }}
            >
              <TextField
                id="reset-email"
                label="Email Address"
                type="email"
                value={email}
                onChange={handleEmailChange}
                onBlur={handleEmailBlur}
                error={Boolean(emailError)}
                helperText={emailError ?? ' '}
                fullWidth
                required
                autoComplete="email"
                inputProps={{ 'aria-required': true }}
                sx={{ mb: 2 }}
                disabled={screenState === 'loading'}
                placeholder="e.g., john@email.com"
              />

              <Button
                type="submit"
                variant="contained"
                fullWidth
                disabled={screenState === 'loading'}
                sx={{ mb: 2, py: 1.5 }}
                aria-label={screenState === 'loading' ? 'Sending reset link' : 'Send reset link'}
              >
                {screenState === 'loading' ? (
                  <>
                    <CircularProgress size={18} color="inherit" sx={{ mr: 1 }} />
                    Sending...
                  </>
                ) : (
                  'Send Reset Link'
                )}
              </Button>

              <Typography variant="body2" sx={{ textAlign: 'center' }}>
                <RouterLink
                  to="/login"
                  style={{ color: '#1976D2', textDecoration: 'none' }} // primary-500
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

function SuccessState() {
  return (
    <Box role="status" aria-live="polite" sx={{ py: 2 }}>
      <LockOutlinedIcon
        sx={{ fontSize: 48, color: 'primary.main', mb: 2 }}
        aria-hidden="true"
      />
      <Typography variant="h2" component="h1" sx={{ mb: 2, fontSize: '1.5rem', fontWeight: 400 }}>
        Check your inbox
      </Typography>
      <Typography variant="body2" sx={{ color: '#757575', mb: 3 }}>
        If an account exists with that email, a password reset link has been sent. Please
        check your inbox. The link expires in 1 hour.
      </Typography>
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
  );
}
