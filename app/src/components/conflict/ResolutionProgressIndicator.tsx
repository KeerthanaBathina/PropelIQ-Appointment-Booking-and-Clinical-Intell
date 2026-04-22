/**
 * ResolutionProgressIndicator — MUI LinearProgress bar with resolved/total conflict
 * counts showing partial resolution progress (US_045 EC-1, AC-4).
 *
 * Design tokens:
 *   LinearProgress: primary.500 (determinate fill)
 *   Text: typography.body2 for counts, typography.caption for status label
 *
 * Placed at the top of ConflictResolutionModal (below dialog title), refreshed
 * automatically via React Query invalidation on each resolve/dismiss action.
 *
 * Shows a compact skeleton while data is loading.
 */

import Box from '@mui/material/Box';
import LinearProgress from '@mui/material/LinearProgress';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';

import { useResolutionProgress } from '@/hooks/useResolutionProgress';

// ─── Props ────────────────────────────────────────────────────────────────────

interface ResolutionProgressIndicatorProps {
  patientId: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ResolutionProgressIndicator({
  patientId,
}: ResolutionProgressIndicatorProps) {
  const { progress, isLoading } = useResolutionProgress(patientId);

  if (isLoading) {
    return (
      <Box sx={{ mb: 2 }}>
        <Skeleton variant="text" width={180} height={20} sx={{ mb: 0.5 }} />
        <Skeleton variant="rounded" height={6} />
      </Box>
    );
  }

  if (!progress || progress.totalConflicts === 0) return null;

  const pct = Math.round(progress.percentComplete);
  const isAllResolved = progress.remainingCount === 0;

  return (
    <Box sx={{ mb: 2 }} role="status" aria-label="Conflict resolution progress">
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', mb: 0.5 }}>
        <Typography variant="body2" fontWeight={500}>
          {progress.resolvedCount} of {progress.totalConflicts} conflict
          {progress.totalConflicts !== 1 ? 's' : ''} resolved
        </Typography>
        <Typography variant="caption" color={isAllResolved ? 'success.main' : 'text.secondary'}>
          {isAllResolved ? 'All resolved ✓' : `${progress.remainingCount} remaining`}
        </Typography>
      </Box>
      <LinearProgress
        variant="determinate"
        value={pct}
        sx={{
          height: 6,
          borderRadius: 3,
          bgcolor: 'grey.200',
          '& .MuiLinearProgress-bar': {
            bgcolor: isAllResolved ? 'success.main' : 'primary.500',
            borderRadius: 3,
          },
        }}
        aria-valuenow={pct}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={`${pct}% of conflicts resolved`}
      />
    </Box>
  );
}
