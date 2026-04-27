/**
 * MetricsTrendChart — SVG line chart for AI accuracy metric time-series (US_072 edge case).
 *
 * Features:
 * - ToggleButtonGroup: Daily / Weekly / Monthly granularity
 * - Date range selectors (start / end date inputs)
 * - SVG line chart with data points and target threshold dashed line
 * - Tooltip on hover showing date + value
 * - Dropdown to select metric type (CodingAgreement / ExtractionPrecision / ExtractionRecall)
 * - Skeleton loading state (UXR-502)
 */

import Box from '@mui/material/Box';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import Skeleton from '@mui/material/Skeleton';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Typography from '@mui/material/Typography';
import { useCallback, useMemo, useRef, useState } from 'react';
import { useAiMetricsTimeSeries } from '../hooks/useAiMetrics';
import type { AccuracyMetricType, Granularity } from '../types';

// ─── SVG chart constants ──────────────────────────────────────────────────────

const CHART_HEIGHT    = 220;
const CHART_PADDING   = { top: 16, right: 16, bottom: 32, left: 44 };

// ─── Helpers ──────────────────────────────────────────────────────────────────

function defaultStartDate(): string {
  const d = new Date();
  d.setDate(d.getDate() - 30);
  return d.toISOString().slice(0, 10);
}

function defaultEndDate(): string {
  return new Date().toISOString().slice(0, 10);
}

// ─── Tooltip state ────────────────────────────────────────────────────────────

interface TooltipState {
  x: number;
  y: number;
  label: string;
  value: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function MetricsTrendChart() {
  const [metricType, setMetricType]   = useState<AccuracyMetricType>('CodingAgreement');
  const [granularity, setGranularity] = useState<Granularity>('daily');
  const [startDate, setStartDate]     = useState(defaultStartDate);
  const [endDate, setEndDate]         = useState(defaultEndDate);
  const [tooltip, setTooltip]         = useState<TooltipState | null>(null);
  const svgRef                        = useRef<SVGSVGElement>(null);

  const { data, isLoading } = useAiMetricsTimeSeries(metricType, startDate, endDate, granularity);

  // ── SVG layout ──────────────────────────────────────────────────────────────
  const svgWidth = 600; // intrinsic; scales via viewBox
  const innerW   = svgWidth - CHART_PADDING.left - CHART_PADDING.right;
  const innerH   = CHART_HEIGHT - CHART_PADDING.top - CHART_PADDING.bottom;

  const points     = data?.dataPoints ?? [];
  const targetVal  = data?.targetValue ?? 0;

  const { pathD, dotData, targetY, xTicks, yTicks } = useMemo(() => {
    if (points.length === 0) {
      return { pathD: '', dotData: [], targetY: 0, xTicks: [], yTicks: [] };
    }

    const values    = points.map(p => p.value);
    const minVal    = Math.max(0, Math.min(...values, targetVal) - 5);
    const maxVal    = Math.min(100, Math.max(...values, targetVal) + 5);
    const valRange  = maxVal - minVal || 1;

    const toX = (i: number) => CHART_PADDING.left + (i / (points.length - 1 || 1)) * innerW;
    const toY = (v: number) => CHART_PADDING.top + innerH - ((v - minVal) / valRange) * innerH;

    const pathD = points
      .map((p, i) => `${i === 0 ? 'M' : 'L'} ${toX(i)} ${toY(p.value)}`)
      .join(' ');

    const dotData = points.map((p, i) => ({
      cx:    toX(i),
      cy:    toY(p.value),
      label: new Date(p.date).toLocaleDateString(),
      value: `${p.value.toFixed(1)}%`,
    }));

    const tgtY = toY(targetVal);

    // X axis ticks (show up to 6 evenly spaced)
    const step = Math.max(1, Math.floor(points.length / 6));
    const xTicks = points
      .filter((_, i) => i % step === 0 || i === points.length - 1)
      .map((p) => {
        const idx = points.indexOf(p);
        return {
          x:     toX(idx),
          label: new Date(p.date).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
        };
      });

    // Y axis ticks (5 levels)
    const yTicks = Array.from({ length: 5 }, (_, i) => {
      const v = minVal + (valRange / 4) * i;
      return { y: toY(v), label: `${v.toFixed(0)}%` };
    });

    return { pathD, dotData, targetY: tgtY, xTicks, yTicks };
  }, [points, targetVal, innerH, innerW]);

  const handleMouseEnter = useCallback((dot: typeof dotData[0]) => {
    setTooltip({ x: dot.cx, y: dot.cy, label: dot.label, value: dot.value });
  }, []);

  const METRIC_LABELS: Record<AccuracyMetricType, string> = {
    CodingAgreement:   'Coding Agreement',
    ExtractionPrecision: 'Extraction Precision',
    ExtractionRecall:  'Extraction Recall',
  };

  return (
    <Paper variant="outlined" sx={{ borderRadius: 2, p: 2 }}>
      <Typography variant="subtitle2" fontWeight={600} gutterBottom>
        Accuracy Trend
      </Typography>

      {/* Controls */}
      <Box
        sx={{
          display:        'flex',
          flexWrap:       'wrap',
          gap:            2,
          mb:             2,
          alignItems:     'flex-end',
        }}
      >
        {/* Metric type selector */}
        <FormControl size="small" sx={{ minWidth: 200 }}>
          <InputLabel id="metric-type-label">Metric</InputLabel>
          <Select
            labelId="metric-type-label"
            value={metricType}
            label="Metric"
            onChange={e => setMetricType(e.target.value as AccuracyMetricType)}
          >
            {(Object.keys(METRIC_LABELS) as AccuracyMetricType[]).map(k => (
              <MenuItem key={k} value={k}>{METRIC_LABELS[k]}</MenuItem>
            ))}
          </Select>
        </FormControl>

        {/* Date range */}
        <TextField
          label="From"
          type="date"
          size="small"
          value={startDate}
          onChange={e => setStartDate(e.target.value)}
          inputProps={{ 'aria-label': 'Start date' }}
          InputLabelProps={{ shrink: true }}
        />
        <TextField
          label="To"
          type="date"
          size="small"
          value={endDate}
          onChange={e => setEndDate(e.target.value)}
          inputProps={{ 'aria-label': 'End date' }}
          InputLabelProps={{ shrink: true }}
        />

        {/* Granularity toggle */}
        <ToggleButtonGroup
          value={granularity}
          exclusive
          onChange={(_, v: Granularity | null) => { if (v) setGranularity(v); }}
          size="small"
          aria-label="Chart granularity"
        >
          <ToggleButton value="daily"   aria-label="Daily">Daily</ToggleButton>
          <ToggleButton value="weekly"  aria-label="Weekly">Weekly</ToggleButton>
          <ToggleButton value="monthly" aria-label="Monthly">Monthly</ToggleButton>
        </ToggleButtonGroup>
      </Box>

      {/* Chart area */}
      {isLoading ? (
        <Skeleton variant="rectangular" height={CHART_HEIGHT} sx={{ borderRadius: 1 }} />
      ) : points.length === 0 ? (
        <Box
          sx={{
            height:         CHART_HEIGHT,
            display:        'flex',
            alignItems:     'center',
            justifyContent: 'center',
            border:         '1px dashed',
            borderColor:    'divider',
            borderRadius:   1,
          }}
        >
          <Typography variant="body2" color="text.secondary">
            No data available for the selected range
          </Typography>
        </Box>
      ) : (
        <Box sx={{ position: 'relative', width: '100%' }}>
          <svg
            ref={svgRef}
            viewBox={`0 0 ${svgWidth} ${CHART_HEIGHT}`}
            style={{ width: '100%', height: 'auto', display: 'block', overflow: 'visible' }}
            aria-label={`${METRIC_LABELS[metricType]} trend chart`}
            onMouseLeave={() => setTooltip(null)}
          >
            {/* Y axis ticks */}
            {yTicks.map(t => (
              <g key={t.label}>
                <line
                  x1={CHART_PADDING.left}
                  y1={t.y}
                  x2={svgWidth - CHART_PADDING.right}
                  y2={t.y}
                  stroke="#e0e0e0"
                  strokeWidth={1}
                />
                <text
                  x={CHART_PADDING.left - 4}
                  y={t.y + 4}
                  textAnchor="end"
                  fontSize={10}
                  fill="#9e9e9e"
                >
                  {t.label}
                </text>
              </g>
            ))}

            {/* X axis ticks */}
            {xTicks.map(t => (
              <text
                key={t.label}
                x={t.x}
                y={CHART_HEIGHT - 4}
                textAnchor="middle"
                fontSize={10}
                fill="#9e9e9e"
              >
                {t.label}
              </text>
            ))}

            {/* Target threshold dashed line */}
            {targetVal > 0 && (
              <>
                <line
                  x1={CHART_PADDING.left}
                  y1={targetY}
                  x2={svgWidth - CHART_PADDING.right}
                  y2={targetY}
                  stroke="#4caf50"
                  strokeWidth={1.5}
                  strokeDasharray="6,4"
                />
                <text
                  x={svgWidth - CHART_PADDING.right + 2}
                  y={targetY + 4}
                  fontSize={10}
                  fill="#4caf50"
                >
                  {targetVal}%
                </text>
              </>
            )}

            {/* Data line */}
            <path
              d={pathD}
              fill="none"
              stroke="#1976d2"
              strokeWidth={2}
              strokeLinejoin="round"
            />

            {/* Data dots */}
            {dotData.map(dot => (
              <circle
                key={dot.label}
                cx={dot.cx}
                cy={dot.cy}
                r={4}
                fill="#1976d2"
                stroke="#fff"
                strokeWidth={1.5}
                style={{ cursor: 'pointer' }}
                onMouseEnter={() => handleMouseEnter(dot)}
                aria-label={`${dot.label}: ${dot.value}`}
              />
            ))}

            {/* Tooltip */}
            {tooltip && (
              <g>
                <rect
                  x={tooltip.x + 8}
                  y={tooltip.y - 28}
                  width={90}
                  height={36}
                  rx={4}
                  fill="#fff"
                  stroke="#bdbdbd"
                  strokeWidth={1}
                />
                <text x={tooltip.x + 12} y={tooltip.y - 14} fontSize={10} fill="#424242">
                  {tooltip.label}
                </text>
                <text x={tooltip.x + 12} y={tooltip.y + 2} fontSize={11} fontWeight="bold" fill="#1976d2">
                  {tooltip.value}
                </text>
              </g>
            )}
          </svg>
        </Box>
      )}
    </Paper>
  );
}
