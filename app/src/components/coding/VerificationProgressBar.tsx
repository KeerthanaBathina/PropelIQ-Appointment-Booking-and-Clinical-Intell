/**
 * VerificationProgressBar — MUI LinearProgress showing "X/Y codes verified"
 * with a status chip (US_049 EC-2, FR-049).
 *
 * Progress value = (verified_count + overridden_count) / total_codes * 100.
 * Status chip colours:
 *   "fully verified"    → success (green)
 *   "partially verified" → warning (amber)
 *   "pending review"    → default (grey)
 *
 * Shown at the top of the verification queue section on SCR-014.
 */

import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import LinearProgress from '@mui/material/LinearProgress';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import type { VerificationProgress } from '@/hooks/useVerificationProgress';

// ─── Helpers ──────────────────────────────────────────────────────────────────

type ChipColor = 'success' | 'warning' | 'default';

function resolveChipColor(label: string): ChipColor {
  if (label === 'fully verified')     return 'success';
  if (label === 'partially verified') return 'warning';
  return 'default';
}

type ProgressColor = 'success' | 'warning' | 'primary';

function resolveProgressColor(label: string): ProgressColor {
  if (label === 'fully verified')     return 'success';
  if (label === 'partially verified') return 'warning';
  return 'primary';
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface VerificationProgressBarProps {
  progress:  VerificationProgress | undefined;
  isLoading: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function VerificationProgressBar({
  progress,
  isLoading,
}: VerificationProgressBarProps) {
  if (isLoading) {
    return (
      <Skeleton
        variant="rounded"
        height={52}
        sx={{ borderRadius: 2, mb: 2 }}
        aria-label="Loading verification progress"
      />
    );
  }

  if (!progress) return null;

  const verified = progress.verified_count + progress.overridden_count;
  const pct      = progress.total_codes > 0
    ? Math.round((verified / progress.total_codes) * 100)
    : 0;

  const chipColor     = resolveChipColor(progress.status_label);
  const progressColor = resolveProgressColor(progress.status_label);

  return (
    <Box sx={{ mb: 2 }} role="region" aria-label="Code verification progress">
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 0.75 }}>
        <Typography variant="body2" color="text.secondary">
          {verified}/{progress.total_codes} codes verified
        </Typography>
        <Chip
          label={progress.status_label}
          color={chipColor}
          size="small"
          sx={{ textTransform: 'capitalize', fontWeight: 600, fontSize: '0.75rem' }}
        />
        {progress.pending_count > 0 && (
          <Typography variant="caption" color="text.secondary">
            ({progress.pending_count} pending)
          </Typography>
        )}
      </Box>
      <LinearProgress
        variant="determinate"
        value={pct}
        color={progressColor}
        sx={{ height: 8, borderRadius: '9999px' }}
        aria-valuenow={pct}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={`${pct}% of codes verified`}
      />
    </Box>
  );
}
