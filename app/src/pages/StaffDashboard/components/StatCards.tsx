/**
 * StatCards — Four summary statistic cards for the Staff Dashboard (US_057 AC-1, SCR-010).
 *
 * Displays: Today's Appointments, In Queue, Pending Reviews, Completed Today.
 * Colors follow design-system tokens (UXR-401): secondary, warning, info, success.
 * Responsive: 2-up on xs/sm, 4-up on md+ (wireframe card-grid layout).
 *
 * Usage:
 *   <StatCards stats={data.stats} isLoading={isLoading} />
 */

import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import type { DashboardStats } from '@/types/staffDashboard';

// ─── Card config (matches wireframe + design-system tokens) ───────────────────

interface CardDef {
  label: string;
  key: keyof DashboardStats;
  color: string; // MUI sx-compatible color path
}

const CARDS: CardDef[] = [
  { label: "Today's Appointments", key: 'todayAppointments', color: 'secondary.main' },
  { label: 'In Queue',              key: 'inQueue',           color: 'warning.main'   },
  { label: 'Pending Reviews',       key: 'pendingReviews',    color: 'info.main'      },
  { label: 'Completed Today',       key: 'completedToday',    color: 'success.main'   },
];

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  stats?: DashboardStats;
  isLoading: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function StatCards({ stats, isLoading }: Props) {
  return (
    <Grid container spacing={3} sx={{ mb: 4 }}>
      {CARDS.map(({ label, key, color }) => (
        <Grid item xs={6} sm={3} key={key}>
          <Paper
            variant="outlined"
            sx={{ p: 2, textAlign: 'center', borderRadius: 2 }}
            aria-label={label}
          >
            {isLoading ? (
              <>
                <Skeleton variant="text" width={40} height={48} sx={{ mx: 'auto' }} />
                <Skeleton variant="text" width={80} sx={{ mx: 'auto' }} />
              </>
            ) : (
              <>
                <Typography
                  variant="h4"
                  component="div"
                  sx={{ color, fontWeight: 300, lineHeight: 1.2 }}
                  aria-live="polite"
                >
                  {stats?.[key] ?? 0}
                </Typography>
                <Typography
                  variant="caption"
                  color="text.secondary"
                  sx={{ textTransform: 'uppercase', letterSpacing: '0.08em', display: 'block' }}
                >
                  {label}
                </Typography>
              </>
            )}
          </Paper>
        </Grid>
      ))}
    </Grid>
  );
}
