/**
 * ActiveAlertsPanel — MUI List of unacknowledged AI metric alerts (US_072 AC-4).
 *
 * - Each item shows: metric name, current vs target value, trend direction icon, timestamp
 * - Acknowledge button calls PUT /api/admin/ai-metrics/alerts/{id}/acknowledge
 * - Empty state shows Alert severity="info" when no active alerts
 * - Skeleton loading state (UXR-502)
 * - Error state with retry (UXR-601)
 */

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import TrendingDownIcon from '@mui/icons-material/TrendingDown';
import TrendingFlatIcon from '@mui/icons-material/TrendingFlat';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import { useAcknowledgeAlert, useAiMetricAlerts } from '../hooks/useAiMetrics';
import type { AiMetricAlert, TrendDirection } from '../types';

// ─── Helpers ──────────────────────────────────────────────────────────────────

const METRIC_LABELS: Record<string, string> = {
  CodingAgreement:    'Coding Agreement',
  ExtractionPrecision: 'Extraction Precision',
  ExtractionRecall:   'Extraction Recall',
};

function TrendIcon({ direction }: { direction: TrendDirection }) {
  if (direction === 'Up')   return <TrendingUpIcon   sx={{ color: 'success.main', fontSize: 18 }} />;
  if (direction === 'Down') return <TrendingDownIcon sx={{ color: 'error.main',   fontSize: 18 }} />;
  return <TrendingFlatIcon sx={{ color: 'text.secondary', fontSize: 18 }} />;
}

// ─── Alert row ────────────────────────────────────────────────────────────────

interface AlertRowProps {
  alert: AiMetricAlert;
  onAcknowledge: (id: string) => void;
  acknowledging: boolean;
}

function AlertRow({ alert, onAcknowledge, acknowledging }: AlertRowProps) {
  const label = METRIC_LABELS[alert.metricName] ?? alert.metricName;
  const generatedAt = new Date(alert.generatedAt).toLocaleString();

  return (
    <ListItem
      alignItems="flex-start"
      disablePadding
      sx={{ py: 1.5, px: 2 }}
    >
      <Box sx={{ display: 'flex', width: '100%', gap: 1.5, alignItems: 'flex-start' }}>
        {/* Warning icon */}
        <WarningAmberIcon sx={{ color: 'error.main', mt: 0.25, flexShrink: 0 }} fontSize="small" />

        {/* Details */}
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, flexWrap: 'wrap' }}>
            <Typography variant="body2" fontWeight={600}>
              {label}
            </Typography>
            <TrendIcon direction={alert.trendDirection} />
          </Box>
          <Typography variant="caption" color="text.secondary" display="block">
            Current:{' '}
            <Typography
              component="span"
              variant="caption"
              fontWeight={600}
              sx={{ color: 'error.main' }}
            >
              {alert.currentValue.toFixed(1)}%
            </Typography>
            {' '}/ Target: {alert.targetValue.toFixed(1)}%
          </Typography>
          <Typography variant="caption" color="text.disabled">
            Generated: {generatedAt}
          </Typography>
        </Box>

        {/* Acknowledge button */}
        <Button
          size="small"
          variant="outlined"
          color="inherit"
          disabled={acknowledging}
          onClick={() => onAcknowledge(alert.alertId)}
          aria-label={`Acknowledge ${label} alert`}
          sx={{ flexShrink: 0, fontSize: '0.7rem' }}
        >
          {acknowledging ? <CircularProgress size={14} /> : 'Acknowledge'}
        </Button>
      </Box>
    </ListItem>
  );
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ActiveAlertsPanel() {
  const { data: alerts, isLoading, isError, refetch } = useAiMetricAlerts();
  const { mutate: acknowledge, variables: acknowledging } = useAcknowledgeAlert();

  return (
    <Paper variant="outlined" sx={{ borderRadius: 2, overflow: 'hidden' }}>
      <Box sx={{ px: 2, py: 1.5, borderBottom: '1px solid', borderColor: 'divider' }}>
        <Typography variant="subtitle2" fontWeight={600}>
          Active Alerts
          {alerts && alerts.length > 0 && (
            <Typography
              component="span"
              variant="caption"
              sx={{
                ml: 1,
                bgcolor: 'error.main',
                color:   'error.contrastText',
                borderRadius: '10px',
                px: 0.75,
                py: 0.25,
              }}
            >
              {alerts.length}
            </Typography>
          )}
        </Typography>
      </Box>

      {isLoading && (
        <Box sx={{ p: 2 }}>
          {Array.from({ length: 2 }).map((_, i) => (
            <Skeleton key={i} variant="rectangular" height={56} sx={{ borderRadius: 1, mb: 1 }} />
          ))}
        </Box>
      )}

      {isError && (
        <Box sx={{ p: 2 }}>
          <Alert
            severity="error"
            action={
              <Button size="small" onClick={() => refetch()}>
                Retry
              </Button>
            }
          >
            Failed to load alerts.
          </Alert>
        </Box>
      )}

      {!isLoading && !isError && alerts?.length === 0 && (
        <Box sx={{ p: 2 }}>
          <Alert severity="info" icon={false}>
            No active alerts — all metrics are within target thresholds.
          </Alert>
        </Box>
      )}

      {!isLoading && !isError && alerts && alerts.length > 0 && (
        <List disablePadding>
          {alerts.map((alert, i) => (
            <Box key={alert.alertId}>
              <AlertRow
                alert={alert}
                onAcknowledge={id => acknowledge(id)}
                acknowledging={acknowledging === alert.alertId}
              />
              {i < alerts.length - 1 && <Divider component="li" />}
            </Box>
          ))}
        </List>
      )}
    </Paper>
  );
}
