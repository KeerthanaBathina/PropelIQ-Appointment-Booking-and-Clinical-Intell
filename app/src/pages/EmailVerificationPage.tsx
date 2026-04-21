import { useEffect, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CircularProgress from '@mui/material/CircularProgress';
import Link from '@mui/material/Link';
import Typography from '@mui/material/Typography';
import { Link as RouterLink, useSearchParams } from 'react-router-dom';
import { useVerifyEmail, useResendVerification } from '@/hooks/useEmailVerification';
import { ApiError } from '@/lib/apiClient';

type VerifyState = 'loading' | 'success' | 'expired' | 'invalid' | 'error';

// AC-3 / edge-case: max 3 resend requests per 5 minutes per email address
const MAX_RESEND_ATTEMPTS = 3;
// Cooldown between resend clicks (seconds)
const RESEND_COOLDOWN_SECONDS = 100;

export default function EmailVerificationPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');
  const email = searchParams.get('email') ?? '';

  const [verifyState, setVerifyState] = useState<VerifyState>('loading');
  const [resendCount, setResendCount] = useState(0);
  const [resendCooldown, setResendCooldown] = useState(0);
  const [resendError, setResendError] = useState<string | undefined>();
  const [resendSuccess, setResendSuccess] = useState(false);

  const verifyMutation = useVerifyEmail();
  const resendMutation = useResendVerification();

  // Verify the token once on mount
  useEffect(() => {
    if (!token) {
      setVerifyState('invalid');
      return;
    }

    verifyMutation.mutate(token, {
      onSuccess: () => setVerifyState('success'),
      onError: (error) => {
        if (error instanceof ApiError && error.status === 410) {
          setVerifyState('expired');
        } else if (error instanceof ApiError && error.status === 400) {
          setVerifyState('invalid');
        } else {
          setVerifyState('error');
        }
      },
    });
    // verifyMutation.mutate is stable across renders; intentionally omitted from deps
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  // Countdown timer for resend cooldown
  useEffect(() => {
    if (resendCooldown <= 0) return;
    const timer = setInterval(() => {
      setResendCooldown((prev) => Math.max(0, prev - 1));
    }, 1000);
    return () => clearInterval(timer);
  }, [resendCooldown]);

  function handleResend() {
    if (resendCount >= MAX_RESEND_ATTEMPTS) {
      setResendError('Maximum resend attempts reached. Please try again later.');
      return;
    }

    setResendError(undefined);
    setResendSuccess(false);

    resendMutation.mutate(email, {
      onSuccess: () => {
        setResendCount((c) => c + 1);
        setResendCooldown(RESEND_COOLDOWN_SECONDS);
        setResendSuccess(true);
      },
      onError: (error) => {
        if (error instanceof ApiError && error.status === 429) {
          setResendError('Too many requests. Please wait before resending.');
          setResendCooldown(300);
        } else {
          setResendError('Failed to resend verification email. Please try again.');
        }
      },
    });
  }

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        bgcolor: '#F5F5F5',
        p: 3,
      }}
    >
      <Card sx={{ width: '100%', maxWidth: 480, boxShadow: 3, bgcolor: '#FFFFFF' }}>
        <CardContent sx={{ p: { xs: 3, sm: 4 }, textAlign: 'center' }}>
          <Typography variant="h6" sx={{ fontWeight: 700, color: 'primary.main', mb: 3 }}>
            UPACIP
          </Typography>

          {verifyState === 'loading' && <VerifyLoadingState />}
          {verifyState === 'success' && <VerifySuccessState />}
          {verifyState === 'expired' && (
            <ExpiredLinkState
              email={email}
              onResend={handleResend}
              isResending={resendMutation.isLoading}
              resendCooldown={resendCooldown}
              resendCount={resendCount}
              maxResend={MAX_RESEND_ATTEMPTS}
              resendSuccess={resendSuccess}
              error={resendError}
            />
          )}
          {(verifyState === 'invalid' || verifyState === 'error') && <InvalidLinkState />}
        </CardContent>
      </Card>
    </Box>
  );
}

function VerifyLoadingState() {
  return (
    <Box role="status" aria-live="polite">
      <CircularProgress sx={{ mb: 2 }} />
      <Typography variant="body1">Verifying your email…</Typography>
    </Box>
  );
}

function VerifySuccessState() {
  return (
    <Box role="status" aria-live="polite">
      <Typography variant="h5" component="h1" gutterBottom>
        Email verified!
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
        Your account is now active. You can sign in to get started.
      </Typography>
      <Button component={RouterLink} to="/login" variant="contained" fullWidth>
        Sign in
      </Button>
    </Box>
  );
}

interface ExpiredLinkStateProps {
  email: string;
  onResend: () => void;
  isResending: boolean;
  resendCooldown: number;
  resendCount: number;
  maxResend: number;
  resendSuccess: boolean;
  error?: string;
}

function ExpiredLinkState({
  email,
  onResend,
  isResending,
  resendCooldown,
  resendCount,
  maxResend,
  resendSuccess,
  error,
}: ExpiredLinkStateProps) {
  const exhausted = resendCount >= maxResend;
  const inCooldown = resendCooldown > 0;

  function getButtonLabel(): string {
    if (isResending) return 'Sending…';
    if (inCooldown) return `Resend in ${resendCooldown}s (${resendCount}/${maxResend})`;
    if (exhausted) return 'Maximum attempts reached';
    return resendCount > 0
      ? `Resend verification email (${resendCount}/${maxResend})`
      : 'Resend verification email';
  }

  return (
    <Box>
      <Typography variant="h5" component="h1" gutterBottom>
        Link expired
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        The verification link has expired (links are valid for 1 hour).
        {email ? ` A new link will be sent to ${email}.` : ''}
      </Typography>

      {error && (
        <Alert severity="error" sx={{ mb: 2, textAlign: 'left' }} role="alert">
          {error}
        </Alert>
      )}
      {resendSuccess && !error && (
        <Alert severity="success" sx={{ mb: 2, textAlign: 'left' }} role="status">
          Verification email resent. Please check your inbox.
        </Alert>
      )}

      <Button
        variant="contained"
        fullWidth
        onClick={onResend}
        disabled={isResending || inCooldown || exhausted}
        aria-label={getButtonLabel()}
        sx={{ mb: 2 }}
      >
        {isResending ? <CircularProgress size={22} color="inherit" /> : getButtonLabel()}
      </Button>

      <Typography variant="body2" color="text.secondary">
        <Link component={RouterLink} to="/login" color="primary" underline="none">
          Back to sign in
        </Link>
      </Typography>
    </Box>
  );
}

function InvalidLinkState() {
  return (
    <Box>
      <Typography variant="h5" component="h1" gutterBottom>
        Invalid link
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
        This verification link is invalid or has already been used.
      </Typography>
      <Button
        component={RouterLink}
        to="/register"
        variant="outlined"
        fullWidth
        sx={{ mb: 1 }}
      >
        Create a new account
      </Button>
      <Button component={RouterLink} to="/login" variant="text" fullWidth>
        Sign in
      </Button>
    </Box>
  );
}
