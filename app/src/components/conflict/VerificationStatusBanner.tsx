/**
 * VerificationStatusBanner — MUI Alert displayed at the top of PatientProfile360Page
 * when all conflicts for a patient have been resolved and the profile is Verified
 * (US_045 AC-4).
 *
 * Design tokens:
 *   Alert severity: "success" (green — per task spec)
 *
 * Shown only when verificationStatus === "Verified".
 * Hidden for Unverified and PartiallyVerified states.
 */

import Alert from '@mui/material/Alert';
import Typography from '@mui/material/Typography';
import VerifiedIcon from '@mui/icons-material/Verified';

import { useVerificationStatus } from '@/hooks/useVerificationStatus';

// ─── Props ────────────────────────────────────────────────────────────────────

interface VerificationStatusBannerProps {
  patientId: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function VerificationStatusBanner({
  patientId,
}: VerificationStatusBannerProps) {
  const { verificationStatus } = useVerificationStatus(patientId);

  if (verificationStatus?.status !== 'Verified') return null;

  const verifiedAt = verificationStatus.verifiedAt
    ? new Date(verificationStatus.verifiedAt).toLocaleString(undefined, {
        year: 'numeric', month: 'short', day: 'numeric',
        hour: '2-digit', minute: '2-digit',
      })
    : null;

  return (
    <Alert
      severity="success"
      icon={<VerifiedIcon fontSize="inherit" />}
      sx={{ mb: 2 }}
      role="status"
      aria-label="Profile verified — all conflicts resolved"
    >
      <Typography variant="body2" fontWeight={600} component="span">
        All conflicts resolved — Profile verified ✓
      </Typography>
      {(verificationStatus.verifiedByUserName || verifiedAt) && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.25 }}>
          {verificationStatus.verifiedByUserName && `Verified by ${verificationStatus.verifiedByUserName}`}
          {verificationStatus.verifiedByUserName && verifiedAt && ' · '}
          {verifiedAt}
        </Typography>
      )}
    </Alert>
  );
}
