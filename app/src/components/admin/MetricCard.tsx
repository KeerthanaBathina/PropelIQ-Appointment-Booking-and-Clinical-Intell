/**
 * MetricCard — Individual KPI card for the Admin Metrics Dashboard (US_058 AC-1, SCR-015).
 *
 * States:
 *   loading  — MUI Skeleton placeholders (UXR-502, triggered for >300ms loads).
 *   data     — metric value, formatted label, optional unit, optional trend indicator.
 *   stale    — value displayed with "Data as of [timestamp]" caption in warning.main
 *              (edge case: backend data temporarily unavailable).
 *
 * Usage:
 *   <MetricCard label="Active Users" value={42} isLoading={false} />
 *   <MetricCard label="No-Show Rate" value={12.5} unit="%" isStale staleAt={ts} isLoading={false} />
 */

import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Skeleton from '@mui/material/Skeleton';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingFlatIcon from '@mui/icons-material/TrendingFlat';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';

// ─── Props ────────────────────────────────────────────────────────────────────

export interface MetricCardProps {
  label: string;
  /** Formatted display value; may be number or string. */
  value?: number | string;
  /** Optional unit suffix appended after the value (e.g. "%"). */
  unit?: string;
  /**
   * Percentage change vs prior period; positive = up, negative = down, 0 = flat.
   * When provided a directional arrow is rendered.
   */
  trendDelta?: number;
  /** True while the parent query is loading. Shows Skeleton placeholders (UXR-502). */
  isLoading: boolean;
  /** True when the value comes from stale cache (live query failed). */
  isStale?: boolean;
  /** ISO 8601 timestamp string for the "Data as of" caption. */
  staleAt?: string;
  /** MUI sx-compatible accent color applied to the value (e.g. "error.main"). */
  accentColor?: string;
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

function formatStaleTime(iso?: string): string {
  if (!iso) return '';
  try {
    return new Intl.DateTimeFormat(undefined, {
      month: 'short',
      day:   'numeric',
      hour:  '2-digit',
      minute: '2-digit',
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function MetricCard({
  label,
  value,
  unit,
  trendDelta,
  isLoading,
  isStale,
  staleAt,
  accentColor = 'error.main',
}: MetricCardProps) {
  const hasTrend = trendDelta !== undefined && trendDelta !== null;

  const TrendIcon =
    !hasTrend    ? null
    : trendDelta > 0  ? TrendingUpIcon
    : trendDelta < 0  ? TrendingDownIcon
    : TrendingFlatIcon;

  const trendColor =
    !hasTrend    ? undefined
    : trendDelta > 0  ? 'success.main'
    : trendDelta < 0  ? 'error.main'
    : 'text.secondary';

  return (
    <Card
      variant="outlined"
      sx={{ borderRadius: 2, height: '100%', position: 'relative' }}
      aria-label={label}
    >
      {isStale && (
        <Box
          sx={{
            position: 'absolute',
            top: 6,
            right: 8,
            width: 8,
            height: 8,
            borderRadius: '50%',
            bgcolor: 'warning.main',
          }}
          aria-label="Stale data indicator"
        />
      )}

      <CardContent sx={{ p: 2, '&:last-child': { pb: 2 } }}>
        {isLoading ? (
          <>
            <Skeleton variant="text" width={48} height={52} sx={{ mb: 0.5 }} />
            <Skeleton variant="text" width="70%" height={20} />
          </>
        ) : (
          <>
            {/* Value row */}
            <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: 0.5 }}>
              <Typography
                variant="h4"
                component="div"
                fontWeight={300}
                lineHeight={1.1}
                sx={{ color: accentColor }}
                aria-live="polite"
              >
                {value ?? '—'}
                {unit && (
                  <Typography component="span" variant="h6" sx={{ color: accentColor, ml: 0.25 }}>
                    {unit}
                  </Typography>
                )}
              </Typography>

              {/* Trend arrow */}
              {hasTrend && TrendIcon && (
                <Tooltip title={`${trendDelta > 0 ? '+' : ''}${trendDelta?.toFixed(1)}% vs prior period`}>
                  <Box sx={{ display: 'flex', alignItems: 'center', mb: 0.25 }}>
                    <TrendIcon sx={{ fontSize: 18, color: trendColor }} />
                  </Box>
                </Tooltip>
              )}
            </Box>

            {/* Label */}
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{ textTransform: 'uppercase', letterSpacing: '0.07em', display: 'block', mt: 0.5 }}
            >
              {label}
            </Typography>

            {/* Stale caption */}
            {isStale && staleAt && (
              <Typography
                variant="caption"
                sx={{ color: 'warning.main', display: 'block', mt: 0.5 }}
              >
                Data as of {formatStaleTime(staleAt)}
              </Typography>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
