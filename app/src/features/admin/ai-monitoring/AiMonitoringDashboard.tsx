/**
 * AiMonitoringDashboard — Container for the AI accuracy monitoring dashboard (US_072 SCR-015).
 *
 * Layout (responsive MUI Grid2):
 *   Top row: 3 AccuracyMetricCards — Coding Agreement, Extraction Precision, Extraction Recall
 *   Middle:  LatencyMetricsTable (full width)
 *   Bottom:  MetricsTrendChart (left) + ActiveAlertsPanel (right) — stacked on mobile
 *
 * States:
 *   Loading → Skeleton in each sub-component (UXR-502)
 *   Error   → Alert with retry button (UXR-601)
 *   Empty   → handled per sub-component
 *
 * Breadcrumb context: "Admin > AI Monitoring" (UXR-003)
 */

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Grid from '@mui/material/Unstable_Grid2';
import Typography from '@mui/material/Typography';
import RefreshIcon from '@mui/icons-material/Refresh';
import { useAiMetricsSummary } from './hooks/useAiMetrics';
import AccuracyMetricCard from './components/AccuracyMetricCard';
import ActiveAlertsPanel from './components/ActiveAlertsPanel';
import LatencyMetricsTable from './components/LatencyMetricsTable';
import MetricsTrendChart from './components/MetricsTrendChart';

// ─── Component ────────────────────────────────────────────────────────────────

export default function AiMonitoringDashboard() {
  const { data: summary, isLoading, isError, refetch } = useAiMetricsSummary();

  const minSampleSize = summary?.minSampleSize ?? 30;

  // ── Last calculated timestamp ──────────────────────────────────────────────
  const lastCalcLabel = summary?.lastCalculatedAt
    ? `Last updated: ${new Date(summary.lastCalculatedAt).toLocaleString()}`
    : null;

  return (
    <Box role="region" aria-label="AI Monitoring Dashboard">

      {/* Section header */}
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
        <Box>
          <Typography variant="h6" component="h2" fontWeight={600}>
            AI Performance Monitoring
          </Typography>
          {lastCalcLabel && (
            <Typography variant="caption" color="text.secondary">
              {lastCalcLabel}
            </Typography>
          )}
        </Box>
        <Button
          size="small"
          variant="outlined"
          startIcon={<RefreshIcon />}
          onClick={() => refetch()}
          aria-label="Refresh AI metrics"
        >
          Refresh
        </Button>
      </Box>

      {/* Error state (UXR-601) */}
      {isError && (
        <Alert
          severity="error"
          sx={{ mb: 2 }}
          action={
            <Button size="small" color="inherit" onClick={() => refetch()}>
              Retry
            </Button>
          }
        >
          Failed to load AI metrics. Data may be temporarily unavailable.
        </Alert>
      )}

      {/* ── Top row: Accuracy metric cards ── */}
      <Grid container spacing={2} sx={{ mb: 2 }}>
        <Grid xs={12} sm={4}>
          <AccuracyMetricCard
            title="Coding Agreement Rate"
            value={summary?.codingAgreementRate ?? 0}
            target={summary?.codingAgreementTarget ?? 98}
            sampleSize={summary?.codingSampleSize ?? 0}
            minSampleSize={minSampleSize}
            trend={summary?.codingAgreementTrend ?? 'Stable'}
            isLoading={isLoading}
          />
        </Grid>
        <Grid xs={12} sm={4}>
          <AccuracyMetricCard
            title="Extraction Precision"
            value={summary?.extractionPrecision ?? 0}
            target={summary?.extractionTarget ?? 95}
            sampleSize={summary?.extractionSampleSize ?? 0}
            minSampleSize={minSampleSize}
            trend={summary?.extractionPrecisionTrend ?? 'Stable'}
            isLoading={isLoading}
          />
        </Grid>
        <Grid xs={12} sm={4}>
          <AccuracyMetricCard
            title="Extraction Recall"
            value={summary?.extractionRecall ?? 0}
            target={summary?.extractionTarget ?? 95}
            sampleSize={summary?.extractionSampleSize ?? 0}
            minSampleSize={minSampleSize}
            trend={summary?.extractionRecallTrend ?? 'Stable'}
            isLoading={isLoading}
          />
        </Grid>
      </Grid>

      {/* ── Middle: Latency table ── */}
      <Box sx={{ mb: 2 }}>
        <Typography
          variant="subtitle2"
          color="text.secondary"
          sx={{ mb: 1, textTransform: 'uppercase', letterSpacing: '0.06em', fontSize: '0.7rem' }}
        >
          Latency (P50 / P95)
        </Typography>
        <LatencyMetricsTable
          latencies={summary?.latencies ?? []}
          isLoading={isLoading}
        />
      </Box>

      {/* ── Bottom: Trend chart + Alerts ── */}
      <Grid container spacing={2}>
        <Grid xs={12} md={7}>
          <MetricsTrendChart />
        </Grid>
        <Grid xs={12} md={5}>
          <ActiveAlertsPanel />
        </Grid>
      </Grid>
    </Box>
  );
}
