/**
 * LoginPage — SCR-001
 *
 * Reads ?expired=true query parameter and displays a warning alert:
 * "Session expired due to inactivity. Please sign in again." (AC-1)
 * Alert auto-dismisses after 10 seconds or on first form field interaction.
 *
 * US_016 additions:
 *   - AccountLockoutAlert with dynamic countdown (AC-2, AC-3)
 *   - MfaTotpStep shown after credential validation for MFA-enabled users (AC-1 US_016)
 *   - Remaining-attempts hint on 401 responses
 *
 * Design tokens: neutral-0 (#FFFFFF), primary-500 (#1976D2), neutral-700 (#616161)
 * Wireframe reference: .propel/context/wireframes/Hi-Fi/wireframe-SCR-001-login.html
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Collapse from '@mui/material/Collapse';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import Visibility from '@mui/icons-material/Visibility';
import VisibilityOff from '@mui/icons-material/VisibilityOff';
import AccountLockoutAlert from '@/components/auth/AccountLockoutAlert';
import MfaTotpStep from '@/components/auth/MfaTotpStep';
import { useLogin } from '@/hooks/useLogin';

const AUTO_DISMISS_MS = 10_000;

export default function LoginPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();

  const isExpired = searchParams.get('expired') === 'true';

  // Session-expired banner state
  const [showExpiredAlert, setShowExpiredAlert] = useState(isExpired);
  const dismissTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (!isExpired) return;
    dismissTimerRef.current = setTimeout(() => setShowExpiredAlert(false), AUTO_DISMISS_MS);
    return () => {
      if (dismissTimerRef.current) clearTimeout(dismissTimerRef.current);
    };
  }, [isExpired]);

  const dismissExpiredAlert = useCallback(() => {
    if (dismissTimerRef.current) clearTimeout(dismissTimerRef.current);
    setShowExpiredAlert(false);
  }, []);

  // Form state
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);

  const {
    step,
    mfaTempToken,
    lockedUntil,
    error: loginError,
    isSubmitting,
    submitCredentials,
    onMfaSuccess,
    cancelMfa,
    clearLockout,
  } = useLogin();

  const handleSubmit = useCallback(
    async (e: React.FormEvent) => {
      e.preventDefault();
      await submitCredentials(email, password);
    },
    [email, password, submitCredentials],
  );

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }}>
      {/* Left branding panel — hidden on mobile (matches wireframe) */}
      <Box
        sx={{
          flex: 1,
          display: { xs: 'none', md: 'flex' },
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          background: 'linear-gradient(135deg, #1565C0, #1976D2)',
          color: '#fff',
          p: 5,
        }}
      >
        <Typography variant="h4" fontWeight={700} mb={1}>
          UPACIP
        </Typography>
        <Typography variant="h5" fontWeight={300} mb={2}>
          Unified Patient Access
        </Typography>
        <Typography variant="body1" sx={{ opacity: 0.85, maxWidth: 360, textAlign: 'center' }}>
          Streamline scheduling, clinical intelligence, and patient engagement in one
          secure platform.
        </Typography>
      </Box>

      {/* Right form panel */}
      <Box
        sx={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          bgcolor: 'background.paper',
          p: 3,
        }}
      >
        <Paper
          elevation={0}
          sx={{ width: '100%', maxWidth: 400, p: { xs: 0, sm: 4 } }}
        >
          {/* ── MFA TOTP step (AC-1 US_016) ── */}
          {step === 'mfa' && mfaTempToken ? (
            <MfaTotpStep
              tempToken={mfaTempToken}
              onVerified={onMfaSuccess}
              onCancel={cancelMfa}
            />
          ) : (
            /* ── Credential form ── */
            <Box
              component="form"
              aria-label="Login form"
              onSubmit={(e) => void handleSubmit(e)}
            >
              <Typography variant="h5" fontWeight={400} mb={3}>
                Sign in to your account
              </Typography>

              {/* Session expired banner */}
              <Collapse in={showExpiredAlert}>
                <Alert
                  severity="warning"
                  onClose={dismissExpiredAlert}
                  sx={{ mb: 2 }}
                  role="status"
                >
                  Session expired due to inactivity. Please sign in again.
                </Alert>
              </Collapse>

              {/* Account lockout alert (AC-2) */}
              {lockedUntil && (
                <AccountLockoutAlert
                  lockedUntil={lockedUntil}
                  onExpired={clearLockout}
                />
              )}

              {/* Credential error */}
              {loginError && !lockedUntil && (
                <Alert severity="error" role="alert" sx={{ mb: 2 }}>
                  {loginError}
                </Alert>
              )}

              <Stack spacing={2.5}>
                <TextField
                  id="email"
                  label="Email Address"
                  type="email"
                  value={email}
                  onChange={(e) => {
                    setEmail(e.target.value);
                    dismissExpiredAlert();
                  }}
                  required
                  autoComplete="email"
                  inputProps={{ 'aria-required': 'true' }}
                  fullWidth
                />

                <TextField
                  id="password"
                  label="Password"
                  type={showPassword ? 'text' : 'password'}
                  value={password}
                  onChange={(e) => {
                    setPassword(e.target.value);
                    dismissExpiredAlert();
                  }}
                  required
                  autoComplete="current-password"
                  inputProps={{ 'aria-required': 'true' }}
                  fullWidth
                  InputProps={{
                    endAdornment: (
                      <InputAdornment position="end">
                        <IconButton
                          aria-label={showPassword ? 'Hide password' : 'Show password'}
                          onClick={() => setShowPassword((v) => !v)}
                          edge="end"
                        >
                          {showPassword ? <VisibilityOff /> : <Visibility />}
                        </IconButton>
                      </InputAdornment>
                    ),
                  }}
                />

                <Button
                  type="submit"
                  variant="contained"
                  fullWidth
                  size="large"
                  disabled={isSubmitting}
                  startIcon={isSubmitting ? <CircularProgress size={18} color="inherit" /> : undefined}
                >
                  {isSubmitting ? 'Signing in…' : 'Sign In'}
                </Button>
              </Stack>

              <Box
                sx={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  mt: 2,
                  fontSize: '0.875rem',
                }}
              >
                <Button variant="text" size="small" onClick={() => navigate('/forgot-password')}>
                  Forgot Password?
                </Button>
                <Button variant="text" size="small" onClick={() => navigate('/register')}>
                  Create Account
                </Button>
              </Box>
            </Box>
          )}
        </Paper>
      </Box>
    </Box>
  );
}
