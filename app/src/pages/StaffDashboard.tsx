/**
 * StaffDashboard — SCR-010
 *
 * Role-specific dashboard for Staff users (UXR-403 secondary accent treatment).
 *
 * US_022 AC-1: Walk-in Registration button opens WalkInRegistrationModal; the
 * workflow stays on this page (no navigation away).
 * US_022 EC-1: Patient-role users cannot reach this page (ProtectedRoute / RoleGuard).
 *
 * Displays LastLoginBanner (AC-4, US_016) below the top navigation bar.
 */

import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import LastLoginBanner from '@/components/auth/LastLoginBanner';
import WalkInRegistrationModal from '@/components/staff/WalkInRegistrationModal';

export default function StaffDashboard() {
  const [walkInOpen, setWalkInOpen] = useState(false);

  return (
    <Box sx={{ flexGrow: 1 }}>
      <AppBar position="static" sx={{ bgcolor: 'secondary.500' }}>
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            UPACIP — Staff Portal
          </Typography>
        </Toolbar>
      </AppBar>

      <LastLoginBanner />

      <Container maxWidth="lg" sx={{ mt: 4, mb: 6 }}>
        {/* ── Stats row (mirrors SCR-010 wireframe) ── */}
        <Grid container spacing={3} sx={{ mb: 4 }}>
          {[
            { label: "Today's Appointments", value: '—', color: 'secondary.500' },
            { label: 'In Queue',              value: '—', color: 'warning.main'  },
            { label: 'Pending Reviews',       value: '—', color: 'info.main'     },
            { label: 'Completed Today',       value: '—', color: 'success.main'  },
          ].map(({ label, value, color }) => (
            <Grid item xs={6} sm={3} key={label}>
              <Paper variant="outlined" sx={{ p: 2, textAlign: 'center' }}>
                <Typography variant="h4" component="div" sx={{ color, fontWeight: 300 }}>
                  {value}
                </Typography>
                <Typography variant="caption" color="text.secondary" sx={{ textTransform: 'uppercase', letterSpacing: '0.08em' }}>
                  {label}
                </Typography>
              </Paper>
            </Grid>
          ))}
        </Grid>

        {/* ── Quick actions (AC-1: Walk-in Registration CTA) ── */}
        <Box sx={{ display: 'flex', gap: 2, mb: 4, flexWrap: 'wrap' }}>
          <Button
            id="walkin-btn"
            variant="contained"
            color="secondary"
            onClick={() => setWalkInOpen(true)}
            aria-haspopup="dialog"
          >
            Walk-in Registration
          </Button>
          <Button variant="outlined" color="secondary" href="#queue">
            View Queue
          </Button>
        </Box>

        {/* ── Content placeholder until future queue/task panels land ── */}
        <Paper variant="outlined" sx={{ p: 3 }}>
          <Typography variant="h6" gutterBottom>
            Welcome to the Staff Portal
          </Typography>
          <Typography variant="body1" color="text.secondary">
            Patient queue, clinical documents, and workflow tools will appear here.
          </Typography>
        </Paper>
      </Container>

      {/* ── Walk-in Registration Modal (US_022 AC-1) ── */}
      <WalkInRegistrationModal
        open={walkInOpen}
        onClose={() => setWalkInOpen(false)}
      />
    </Box>
  );
}

