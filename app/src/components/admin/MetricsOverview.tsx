/**
 * MetricsOverview — Responsive 5-card KPI grid for the Admin Dashboard (US_058 AC-1, SCR-015).
 *
 * Desktop (md+): 5 cards in a single row (xs=12 sm=6 md=2.4 each).
 * Mobile (<sm):  Cards collapse into vertically stacked MUI Accordion items (edge case).
 *
 * Skeleton loading (UXR-502) is forwarded to each MetricCard.
 * Stale-data indicator displayed when isStale=true (edge case).
 *
 * Accent color follows UXR-403 admin visual treatment: error.main.
 */

import Accordion from '@mui/material/Accordion';
import AccordionDetails from '@mui/material/AccordionDetails';
import AccordionSummary from '@mui/material/AccordionSummary';
import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Typography from '@mui/material/Typography';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { useMediaQuery, useTheme } from '@mui/material';
import MetricCard from './MetricCard';
import type { AdminMetricsSnapshot } from '@/types/adminMetrics';

// ─── Card definitions ─────────────────────────────────────────────────────────

interface CardDef {
  label: string;
  key: keyof AdminMetricsSnapshot;
  unit?: string;
}

const METRIC_CARDS: CardDef[] = [
  { label: 'Active Users',       key: 'activeUsers'       },
  { label: 'Daily Appointments', key: 'dailyAppointments' },
  { label: 'No-Show Rate',       key: 'noShowRate',        unit: '%' },
  { label: 'AI Agreement Rate',  key: 'aiAgreementRate',   unit: '%' },
  { label: 'Uptime',             key: 'uptimePercent',     unit: '%' },
];

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  metrics?: AdminMetricsSnapshot;
  isLoading: boolean;
  isStale?: boolean;
  staleAt?: string;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function MetricsOverview({ metrics, isLoading, isStale, staleAt }: Props) {
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('sm'));

  if (isMobile) {
    // Collapsible accordion on mobile (edge case requirement)
    return (
      <Box aria-label="System metrics" sx={{ mb: 3 }}>
        {METRIC_CARDS.map(({ label, key, unit }) => (
          <Accordion key={key} disableGutters elevation={0} sx={{ border: '1px solid', borderColor: 'divider', mb: 1, borderRadius: '8px !important' }}>
            <AccordionSummary
              expandIcon={<ExpandMoreIcon />}
              aria-controls={`metric-${key}-content`}
              id={`metric-${key}-header`}
            >
              <Typography variant="subtitle2">{label}</Typography>
              {!isLoading && metrics && (
                <Typography variant="subtitle2" sx={{ ml: 'auto', mr: 1, color: 'error.main', fontWeight: 600 }}>
                  {metrics[key]}{unit}
                </Typography>
              )}
            </AccordionSummary>
            <AccordionDetails sx={{ pt: 0 }}>
              <MetricCard
                label={label}
                value={metrics?.[key]}
                unit={unit}
                isLoading={isLoading}
                isStale={isStale}
                staleAt={staleAt}
                accentColor="error.main"
              />
            </AccordionDetails>
          </Accordion>
        ))}
      </Box>
    );
  }

  return (
    <Grid container spacing={2} sx={{ mb: 3 }} aria-label="System metrics">
      {METRIC_CARDS.map(({ label, key, unit }) => (
        <Grid item xs={12} sm={6} md={2.4} key={key}>
          <MetricCard
            label={label}
            value={metrics?.[key]}
            unit={unit}
            isLoading={isLoading}
            isStale={isStale}
            staleAt={staleAt}
            accentColor="error.main"
          />
        </Grid>
      ))}
    </Grid>
  );
}
