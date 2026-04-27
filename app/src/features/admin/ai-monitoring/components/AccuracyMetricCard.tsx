/**
 * AccuracyMetricCard — displays a single AI accuracy metric with:
 * - Circular progress gauge (0–100%)
 * - Color-coded value: green ≥ target, amber within 2%, red below that
 * - Target indicator line
 * - Trend direction arrow
 * - "Insufficient data" state when sampleSize < minSampleSize (edge case)
 *
 * UXR-105: color-coded confidence indicators.
 * UXR-502: Skeleton placeholder while loading.
 */

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CircularProgress from '@mui/material/CircularProgress';
import Skeleton from '@mui/material/Skeleton';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingFlatIcon from '@mui/icons-material/TrendingFlat';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import type { TrendDirection } from '../types';

// ─── Props ────────────────────────────────────────────────────────────────────

interface AccuracyMetricCardProps {
  title: string;
  value: number;
  target: number;
  sampleSize: number;
  minSampleSize: number;
  trend: TrendDirection;
  isLoading?: boolean;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function getValueColor(value: number, target: number): string {
  if (value >= target) return 'success.main';
  if (value >= target - 2) return 'warning.main';
  return 'error.main';
}

function TrendIcon({ direction }: { direction: TrendDirection }) {
  if (direction === 'Up')   return <TrendingUpIcon   sx={{ color: 'success.main', fontSize: 20 }} aria-label="Trending up"   />;
  if (direction === 'Down') return <TrendingDownIcon sx={{ color: 'error.main',   fontSize: 20 }} aria-label="Trending down" />;
  return <TrendingFlatIcon sx={{ color: 'text.secondary', fontSize: 20 }} aria-label="Stable" />;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AccuracyMetricCard({
  title,
  value,
  target,
  sampleSize,
  minSampleSize,
  trend,
  isLoading = false,
}: AccuracyMetricCardProps) {
  const insufficient = sampleSize < minSampleSize;
  const valueColor   = getValueColor(value, target);
  // CircularProgress expects 0-100; clamp the value
  const gaugeValue   = Math.min(100, Math.max(0, value));

  if (isLoading) {
    return (
      <Card variant="outlined" sx={{ borderRadius: 2, height: '100%' }}>
        <CardContent sx={{ p: 3 }}>
          <Skeleton variant="text" width="60%" height={24} sx={{ mb: 1 }} />
          <Skeleton variant="circular" width={80} height={80} sx={{ mb: 2, mx: 'auto' }} />
          <Skeleton variant="text" width="40%" sx={{ mx: 'auto' }} />
        </CardContent>
      </Card>
    );
  }

  return (
    <Card
      variant="outlined"
      sx={{ borderRadius: 2, height: '100%', borderColor: insufficient ? 'divider' : `${valueColor}` }}
      role="region"
      aria-label={`${title} metric`}
    >
      <CardContent sx={{ p: 3, '&:last-child': { pb: 3 } }}>
        {/* Title row */}
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
          <Typography variant="subtitle2" color="text.secondary" fontWeight={600}>
            {title}
          </Typography>
          {!insufficient && <TrendIcon direction={trend} />}
        </Box>

        {insufficient ? (
          /* Insufficient data state (edge case) */
          <Box sx={{ textAlign: 'center', py: 2 }}>
            <Typography variant="h6" color="text.disabled">
              Insufficient data
            </Typography>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
              Minimum {minSampleSize} samples required
            </Typography>
            <Typography variant="caption" color="text.secondary">
              Current: {sampleSize} sample{sampleSize !== 1 ? 's' : ''}
            </Typography>
          </Box>
        ) : (
          /* Gauge + value */
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 1 }}>
            <Tooltip title={`${value.toFixed(1)}% (target: ${target}%)`}>
              <Box sx={{ position: 'relative', display: 'inline-flex' }}>
                {/* Background track */}
                <CircularProgress
                  variant="determinate"
                  value={100}
                  size={80}
                  sx={{ color: 'grey.200', position: 'absolute' }}
                  aria-hidden="true"
                />
                {/* Gauge */}
                <CircularProgress
                  variant="determinate"
                  value={gaugeValue}
                  size={80}
                  sx={{ color: valueColor }}
                  aria-label={`${value.toFixed(1)} percent`}
                />
                {/* Centre label */}
                <Box
                  sx={{
                    position: 'absolute',
                    inset: 0,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                  }}
                >
                  <Typography
                    variant="caption"
                    component="span"
                    fontWeight={700}
                    sx={{ color: valueColor, fontSize: '0.8rem' }}
                  >
                    {value.toFixed(1)}%
                  </Typography>
                </Box>
              </Box>
            </Tooltip>

            {/* Target indicator */}
            <Typography variant="caption" color="text.secondary">
              Target: ≥ {target}%
            </Typography>

            {/* Sample size */}
            <Typography variant="caption" color="text.disabled">
              n = {sampleSize.toLocaleString()}
            </Typography>
          </Box>
        )}
      </CardContent>
    </Card>
  );
}
