/**
 * MfaSetupModal — 3-step MFA enrollment dialog (US_016).
 *
 * Step 1: Display QR code + manual entry key (GET /api/auth/mfa/setup)
 * Step 2: Verify first TOTP code to confirm scanner worked (POST /api/auth/mfa/verify-setup)
 * Step 3: Display backup codes with download + copy actions
 *
 * Non-dismissible until setup is complete when enforcedByPolicy is true.
 * Includes focus trap (built into MUI Dialog) and ARIA labels (UXR-201).
 */

import { useCallback, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import FormControlLabel from '@mui/material/FormControlLabel';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import CloseIcon from '@mui/icons-material/Close';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DownloadIcon from '@mui/icons-material/Download';
import { ApiError } from '@/lib/apiClient';
import { useMfaSetupData, useVerifyMfaSetup } from '@/hooks/useMfaSetup';

type SetupStep = 1 | 2 | 3;

interface Props {
  open: boolean;
  /** Whether the dialog can be dismissed before completion (admin-enforced policy). */
  enforcedByPolicy?: boolean;
  onClose: () => void;
  onSetupComplete: () => void;
}

export default function MfaSetupModal({
  open,
  enforcedByPolicy = false,
  onClose,
  onSetupComplete,
}: Props) {
  const [step, setStep] = useState<SetupStep>(1);
  const [totpCode, setTotpCode] = useState('');
  const [verifyError, setVerifyError] = useState<string | null>(null);
  const [savedCodes, setSavedCodes] = useState(false);
  const [copyTooltip, setCopyTooltip] = useState('Copy all');

  const codeInputRef = useRef<HTMLInputElement>(null);

  const { data: setupData, isLoading: loadingSetup, error: setupError } = useMfaSetupData(open);
  const verifySetup = useVerifyMfaSetup();

  const handleClose = useCallback(() => {
    if (enforcedByPolicy && step < 3) return; // non-dismissible
    onClose();
    // Reset state for next open
    setStep(1);
    setTotpCode('');
    setVerifyError(null);
    setSavedCodes(false);
  }, [enforcedByPolicy, onClose, step]);

  const handleVerify = useCallback(async () => {
    setVerifyError(null);
    try {
      await verifySetup.mutateAsync({ totpCode });
      setStep(3);
    } catch (err) {
      setVerifyError(
        err instanceof ApiError && err.status === 400
          ? 'Invalid code. Please check your authenticator app and try again.'
          : 'Verification failed. Please try again.',
      );
      setTotpCode('');
      codeInputRef.current?.focus();
    }
  }, [totpCode, verifySetup]);

  const handleCodeChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const raw = e.target.value.replace(/\D/g, '').slice(0, 6);
    setTotpCode(raw);
    setVerifyError(null);
  }, []);

  const handleDownload = useCallback(() => {
    if (!setupData?.backupCodes) return;
    const content = setupData.backupCodes.join('\n');
    const blob = new Blob([content], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'upacip-backup-codes.txt';
    a.click();
    URL.revokeObjectURL(url);
  }, [setupData]);

  const handleCopy = useCallback(async () => {
    if (!setupData?.backupCodes) return;
    try {
      await navigator.clipboard.writeText(setupData.backupCodes.join('\n'));
      setCopyTooltip('Copied!');
      setTimeout(() => setCopyTooltip('Copy all'), 2000);
    } catch {
      setCopyTooltip('Copy failed');
    }
  }, [setupData]);

  const stepTitles: Record<SetupStep, string> = {
    1: 'Set Up Two-Factor Authentication',
    2: 'Verify Your Authenticator App',
    3: 'Save Your Backup Codes',
  };

  const canDismiss = !enforcedByPolicy || step === 3;

  return (
    <Dialog
      open={open}
      onClose={canDismiss ? handleClose : undefined}
      maxWidth="sm"
      fullWidth
      aria-labelledby="mfa-setup-dialog-title"
      aria-describedby="mfa-setup-dialog-description"
      disableEscapeKeyDown={!canDismiss}
    >
      <DialogTitle id="mfa-setup-dialog-title" sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', pb: 1 }}>
        {stepTitles[step]}
        {canDismiss && (
          <IconButton aria-label="Close MFA setup dialog" onClick={handleClose} size="small">
            <CloseIcon />
          </IconButton>
        )}
      </DialogTitle>

      <DialogContent id="mfa-setup-dialog-description">

        {/* ── Step 1: QR Code ── */}
        {step === 1 && (
          <Stack spacing={3}>
            <Typography variant="body2" color="text.secondary">
              Scan the QR code below with your authenticator app (Google Authenticator,
              Authy, Microsoft Authenticator, etc.).
            </Typography>

            {loadingSetup && (
              <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
                <CircularProgress aria-label="Loading MFA setup data" />
              </Box>
            )}

            {setupError && (
              <Alert severity="error">
                Failed to load QR code. Please close and try again.
              </Alert>
            )}

            {setupData && (
              <>
                {/* QR code rendered as an <img> using the otpAuthUrl.
                    The backend should return a pre-generated QR image URL or
                    data URI. Alternatively render with qrcode.react library. */}
                <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
                  <Box
                    component="img"
                    src={`https://chart.googleapis.com/chart?chs=200x200&chld=M|0&cht=qr&chl=${encodeURIComponent(setupData.otpAuthUrl)}`}
                    alt="QR code for MFA setup — scan with your authenticator app"
                    width={200}
                    height={200}
                    sx={{ border: '1px solid', borderColor: 'divider', borderRadius: 1, p: 1 }}
                  />
                  <Typography variant="caption" color="text.secondary" textAlign="center">
                    Can't scan? Enter this key manually:
                  </Typography>
                  <Typography
                    variant="body2"
                    fontFamily="monospace"
                    letterSpacing="0.2em"
                    bgcolor="grey.100"
                    px={2}
                    py={1}
                    borderRadius={1}
                    aria-label="Manual entry key"
                  >
                    {setupData.manualEntryKey}
                  </Typography>
                </Box>

                <Button variant="contained" fullWidth onClick={() => setStep(2)}>
                  Next — Verify Setup
                </Button>
              </>
            )}
          </Stack>
        )}

        {/* ── Step 2: Verify TOTP ── */}
        {step === 2 && (
          <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">
              Enter the 6-digit code now showing in your authenticator app to confirm
              the setup worked correctly.
            </Typography>

            {verifyError && (
              <Alert severity="error" role="alert">
                {verifyError}
              </Alert>
            )}

            <TextField
              inputRef={codeInputRef}
              label="6-digit verification code"
              value={totpCode}
              onChange={handleCodeChange}
              inputProps={{
                inputMode: 'numeric',
                maxLength: 6,
                autoComplete: 'one-time-code',
                'aria-required': 'true',
                pattern: '[0-9]*',
              }}
              autoFocus
              fullWidth
              sx={{ '& input': { letterSpacing: '0.3em', textAlign: 'center', fontSize: '1.25rem' } }}
            />

            <Stack direction="row" spacing={1}>
              <Button
                variant="outlined"
                fullWidth
                onClick={() => setStep(1)}
                disabled={verifySetup.isPending}
              >
                Back
              </Button>
              <Button
                variant="contained"
                fullWidth
                onClick={() => void handleVerify()}
                disabled={totpCode.length !== 6 || verifySetup.isPending}
                startIcon={verifySetup.isPending ? <CircularProgress size={16} color="inherit" /> : undefined}
              >
                {verifySetup.isPending ? 'Verifying…' : 'Verify'}
              </Button>
            </Stack>
          </Stack>
        )}

        {/* ── Step 3: Backup Codes ── */}
        {step === 3 && (
          <Stack spacing={2}>
            <Alert severity="warning" icon={false}>
              Save these backup codes in a secure location. Each code can only be used
              once to sign in if you lose access to your authenticator app.
            </Alert>

            {setupData?.backupCodes && (
              <>
                <Box
                  sx={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(2, 1fr)',
                    gap: 1,
                    bgcolor: 'grey.50',
                    border: '1px solid',
                    borderColor: 'divider',
                    borderRadius: 1,
                    p: 2,
                  }}
                  aria-label="Backup codes list"
                >
                  {setupData.backupCodes.map((code) => (
                    <Typography
                      key={code}
                      variant="body2"
                      fontFamily="monospace"
                      textAlign="center"
                    >
                      {code}
                    </Typography>
                  ))}
                </Box>

                <Stack direction="row" spacing={1}>
                  <Button
                    variant="outlined"
                    size="small"
                    startIcon={<DownloadIcon />}
                    onClick={handleDownload}
                    aria-label="Download backup codes as text file"
                  >
                    Download
                  </Button>
                  <Tooltip title={copyTooltip} placement="top">
                    <Button
                      variant="outlined"
                      size="small"
                      startIcon={<ContentCopyIcon />}
                      onClick={() => void handleCopy()}
                      aria-label="Copy all backup codes to clipboard"
                    >
                      Copy all
                    </Button>
                  </Tooltip>
                </Stack>
              </>
            )}

            <Divider />

            <FormControlLabel
              control={
                <Checkbox
                  checked={savedCodes}
                  onChange={(e) => setSavedCodes(e.target.checked)}
                  aria-label="Confirm backup codes saved"
                />
              }
              label="I have saved my backup codes in a secure location."
            />

            <Button
              variant="contained"
              fullWidth
              disabled={!savedCodes}
              onClick={() => {
                onSetupComplete();
                handleClose();
              }}
            >
              Done — MFA is enabled
            </Button>
          </Stack>
        )}
      </DialogContent>
    </Dialog>
  );
}
