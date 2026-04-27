/**
 * TrendChart — Rolling 7-day / 30-day trend line chart for the Admin Dashboard (US_058 AC-2).
 *
 * Rendered as a pure SVG line chart (no external chart library required) with:
 *   - ToggleButtonGroup for 7d / 30d period selection
 *   - One polyline per metric, color-coded
 *   - X-axis date labels, Y-axis grid lines
 *   - Skeleton loading state (UXR-502)
 *   - Error/empty fallback message
 *
 * Design tokens applied:
 *   - Grid lines: neutral-300 (grey.300)
 *   - Axis labels: neutral-600 (text.secondary)
 *   - Line colors: error, secondary, warning, info, success palettes (UXR-403)
 */

import Box from '@mui/material/Box';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { useAdminMetricsTrends } from '@/hooks/useAdminMetrics';
import type { MetricTrendPoint, TrendPeriod } from '@/types/adminMetrics';

// ─── Chart configuration ──────────────────────────────────────────────────────

interface SeriesDef {
  label: string;
  key: keyof Omit<MetricTrendPoint, 'date'>;
  color: string;
}

const SERIES: SeriesDef[] = [
  { label: 'Active Users',       key: 'activeUsers',       color: '#d32f2f' }, // error.main
  { label: 'Daily Appts',        key: 'dailyAppointments', color: '#9c27b0' }, // secondary.main
  { label: 'No-Show Rate %',     key: 'noShowRate',        color: '#ed6c02' }, // warning.main
  { label: 'AI Agreement %',     key: 'aiAgreementRate',   color: '#0288d1' }, // info.main
  { label: 'Uptime %',           key: 'uptimePercent',     color: '#2e7d32' }, // success.main
];

const SVG_W = 720;
const SVG_H = 240;
const PADDING = { top: 16, right: 20, bottom: 40, left: 48 };

// ─── SVG helpers ──────────────────────────────────────────────────────────────

function toPoints(
  data: MetricTrendPoint[],
  key: keyof Omit<MetricTrendPoint, 'date'>,
  yMin: number,
  yMax: number,
): string {
  if (!data.length) return '';
  const plotW = SVG_W - PADDING.left - PADDING.right;
  const plotH = SVG_H - PADDING.top  - PADDING.bottom;
  const range = yMax - yMin || 1;

  return data
    .map((pt, i) => {
      const x = PADDING.left + (i / Math.max(data.length - 1, 1)) * plotW;
      const y = PADDING.top + plotH - ((pt[key] as number - yMin) / range) * plotH;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(' ');
}

function allValues(data: MetricTrendPoint[]): number[] {
  return data.flatMap(pt => SERIES.map(s => pt[s.key] as number));
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function TrendChart() {
  const [period, setPeriod] = useState<TrendPeriod>('7d');
  const { data, isLoading, isError } = useAdminMetricsTrends(period);

  const handlePeriod = (_: React.MouseEvent<HTMLElement>, next: TrendPeriod | null) => {
    if (next) setPeriod(next);
  };

  const plotW = SVG_W - PADDING.left - PADDING.right;
  const plotH = SVG_H - PADDING.top  - PADDING.bottom;

  // Compute y-axis range across all series so every line is on the same scale
  const vals = data ? allValues(data.dataPoints) : [];
  const yMin = vals.length ? Math.floor(Math.min(...vals) * 0.95) : 0;
  const yMax = vals.length ? Math.ceil (Math.max(...vals) * 1.05) : 100;

  // X-axis labels: evenly spaced, max 7 shown
  const points = data?.dataPoints ?? [];
  const labelStep = Math.max(1, Math.ceil(points.length / 7));
  const xLabels = points.filter((_, i) => i % labelStep === 0 || i === points.length - 1);

  // Y-axis grid lines (4 lines)
  const yGridCount = 4;
  const yGridLines = Array.from({ length: yGridCount + 1 }, (_, i) =>
    yMin + (i / yGridCount) * (yMax - yMin),
  );

  return (
    <Paper variant="outlined" sx={{ p: 2.5, borderRadius: 2 }}>
      {/* Header row */}
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2, flexWrap: 'wrap', gap: 1 }}>
        <Typography variant="subtitle1" fontWeight={600}>
          Rolling Trends
        </Typography>
        <ToggleButtonGroup
          value={period}
          exclusive
          onChange={handlePeriod}
          size="small"
          aria-label="Select trend period"
        >
          <ToggleButton value="7d" aria-label="7-day trend">7 days</ToggleButton>
          <ToggleButton value="30d" aria-label="30-day trend">30 days</ToggleButton>
        </ToggleButtonGroup>
      </Box>

      {/* Skeleton while loading */}
      {isLoading && (
        <Box>
          <Skeleton variant="rectangular" width="100%" height={SVG_H} sx={{ borderRadius: 1 }} />
        </Box>
      )}

      {/* Error / empty state */}
      {!isLoading && (isError || !data || !data.dataPoints.length) && (
        <Box
          sx={{
            height: SVG_H,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            bgcolor: 'grey.50',
            borderRadius: 1,
          }}
        >
          <Typography variant="body2" color="text.secondary">
            {isError ? 'Trend data unavailable' : 'No data for this period'}
          </Typography>
        </Box>
      )}

      {/* SVG chart */}
      {!isLoading && !isError && data && data.dataPoints.length > 0 && (
        <>
          <Box sx={{ overflowX: 'auto' }}>
            <svg
              viewBox={`0 0 ${SVG_W} ${SVG_H}`}
              role="img"
              aria-label={`Trend chart — ${period === '7d' ? '7-day' : '30-day'} rolling metrics`}
              style={{ width: '100%', minWidth: 320, height: 'auto' }}
            >
              {/* Y-axis grid lines */}
              {yGridLines.map((y, i) => {
                const svgY = PADDING.top + plotH - ((y - yMin) / (yMax - yMin)) * plotH;
                return (
                  <g key={i}>
                    <line
                      x1={PADDING.left}
                      y1={svgY}
                      x2={PADDING.left + plotW}
                      y2={svgY}
                      stroke="#e0e0e0"
                      strokeWidth={1}
                    />
                    <text
                      x={PADDING.left - 6}
                      y={svgY + 4}
                      textAnchor="end"
                      fontSize={10}
                      fill="#757575"
                    >
                      {Math.round(y)}
                    </text>
                  </g>
                );
              })}

              {/* X-axis labels */}
              {xLabels.map(pt => {
                const idx = points.indexOf(pt);
                const svgX = PADDING.left + (idx / Math.max(points.length - 1, 1)) * plotW;
                const dateLabel = pt.date.slice(5); // MM-DD
                return (
                  <text
                    key={pt.date}
                    x={svgX}
                    y={SVG_H - 8}
                    textAnchor="middle"
                    fontSize={10}
                    fill="#757575"
                  >
                    {dateLabel}
                  </text>
                );
              })}

              {/* Metric polylines */}
              {SERIES.map(({ key, color }) => (
                <polyline
                  key={key}
                  points={toPoints(points, key, yMin, yMax)}
                  fill="none"
                  stroke={color}
                  strokeWidth={2}
                  strokeLinejoin="round"
                  strokeLinecap="round"
                />
              ))}
            </svg>
          </Box>

          {/* Legend */}
          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 2, mt: 1.5 }}>
            {SERIES.map(({ label, color }) => (
              <Box key={label} sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                <Box sx={{ width: 16, height: 3, bgcolor: color, borderRadius: 1, flexShrink: 0 }} />
                <Typography variant="caption" color="text.secondary">
                  {label}
                </Typography>
              </Box>
            ))}
          </Box>
        </>
      )}
    </Paper>
  );
}
