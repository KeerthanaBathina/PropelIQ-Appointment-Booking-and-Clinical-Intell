/**
 * StaffDashboard — SCR-010
 *
 * Role-specific dashboard for Staff users (UXR-403 secondary accent treatment).
 *
 * US_022 AC-1: Walk-in Registration button opens WalkInRegistrationModal; the
 * workflow stays on this page (no navigation away).
 * US_022 EC-1: Patient-role users cannot reach this page (ProtectedRoute / RoleGuard).
 * US_026 AC-2: Today's Schedule table shows no-show risk score with color coding.
 * US_026 AC-3: Estimated scores display "Est." label for patients with <3 appointments.
 *
 * Displays LastLoginBanner (AC-4, US_016) below the top navigation bar.
 */

import AppBar from '@mui/material/AppBar';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Grid from '@mui/material/Grid';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Toolbar from '@mui/material/Toolbar';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import { useState } from 'react';
import LastLoginBanner from '@/components/auth/LastLoginBanner';
import WalkInRegistrationModal from '@/components/staff/WalkInRegistrationModal';
import NoShowRiskBadge from '@/components/staff/NoShowRiskBadge';
import AppointmentStatusBadge from '@/components/appointments/AppointmentStatusBadge';
import { useStaffAppointments } from '@/hooks/useStaffAppointments';
import type { StaffAppointmentStatus } from '@/hooks/useStaffAppointments';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatLocalTime(utcIso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    hour:   'numeric',
    minute: '2-digit',
  }).format(new Date(utcIso));
}

/** Only Scheduled/Arrived/InVisit/Completed/Cancelled/NoShow map to the patient badge. */
function toPatientStatus(s: StaffAppointmentStatus) {
  const map: Record<StaffAppointmentStatus, 'Scheduled' | 'Completed' | 'Cancelled' | 'NoShow'> = {
    Scheduled: 'Scheduled',
    Arrived:   'Scheduled',  // closest patient-visible analog
    InVisit:   'Scheduled',
    Completed: 'Completed',
    Cancelled: 'Cancelled',
    NoShow:    'NoShow',
  };
  return map[s];
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function StaffDashboard() {
  const [walkInOpen, setWalkInOpen] = useState(false);
  const { data: appointments, isLoading, isError } = useStaffAppointments();

  // Derive stat counts from loaded data
  const totalToday    = appointments?.length ?? 0;
  const inQueue       = appointments?.filter(a => a.queuePosition !== null).length ?? 0;
  const completedToday = appointments?.filter(a => a.status === 'Completed').length ?? 0;

  return (
    <Box sx={{ flexGrow: 1 }}>
      <AppBar position="static" sx={{ bgcolor: 'secondary.main' }}>
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            UPACIP — Staff Portal
          </Typography>
        </Toolbar>
      </AppBar>

      <LastLoginBanner />

      <Container maxWidth="lg" sx={{ mt: 4, mb: 6 }}>
        {/* ── Stats row (SCR-010 wireframe) ── */}
        <Grid container spacing={3} sx={{ mb: 4 }}>
          {[
            { label: "Today's Appointments", value: isLoading ? '—' : String(totalToday),     color: 'secondary.main' },
            { label: 'In Queue',              value: isLoading ? '—' : String(inQueue),        color: 'warning.main'   },
            { label: 'Pending Reviews',       value: '—',                                       color: 'info.main'      },
            { label: 'Completed Today',       value: isLoading ? '—' : String(completedToday), color: 'success.main'   },
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
          <Button variant="outlined" color="secondary" href="/staff/queue">
            View Queue
          </Button>
        </Box>

        {/* ── Today's Schedule (SCR-010, US_026) ── */}
        <Paper variant="outlined" sx={{ p: 3 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 2 }}>
            <Typography variant="h6">Today's Schedule</Typography>
            <Tooltip
              title="No-show risk scores indicate likelihood of patient absence. Green <30, Amber 30–69, Red ≥70."
              arrow
              placement="right"
            >
              <InfoOutlinedIcon
                fontSize="small"
                sx={{ color: 'text.secondary', cursor: 'help' }}
                aria-label="No-show risk legend"
              />
            </Tooltip>
          </Box>

          {/* ── Error state ── */}
          {isError && (
            <Alert severity="error" sx={{ mb: 2 }}>
              Unable to load today's schedule. Please refresh the page.
            </Alert>
          )}

          {/* ── Loading state (UXR-502 skeleton) ── */}
          {isLoading && (
            <Box aria-busy="true" aria-label="Loading today's schedule">
              {[0, 1, 2].map(i => (
                <Skeleton key={i} variant="rectangular" height={40} sx={{ mb: 1, borderRadius: 1 }} />
              ))}
            </Box>
          )}

          {/* ── Empty state ── */}
          {!isLoading && !isError && appointments?.length === 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ py: 2 }}>
              No appointments scheduled for today.
            </Typography>
          )}

          {/* ── Table (populated) ── */}
          {!isLoading && !isError && (appointments?.length ?? 0) > 0 && (
            <TableContainer>
              <Table
                size="small"
                aria-label="Today's appointment schedule with no-show risk scores"
              >
                <TableHead>
                  <TableRow>
                    <TableCell>Time</TableCell>
                    <TableCell>Patient</TableCell>
                    <TableCell>Type</TableCell>
                    <TableCell>Status</TableCell>
                    {/* No-Show Risk column (US_026 AC-2) */}
                    <TableCell>
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                        No-Show Risk
                      </Box>
                    </TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {appointments!.map(appt => (
                    <TableRow
                      key={appt.id}
                      sx={{
                        // Highlight high-risk rows for quick scanning (EC-2)
                        bgcolor: appt.noShowRiskScore !== null && appt.noShowRiskScore >= 70
                          ? 'error.50'
                          : undefined,
                        opacity: appt.status === 'NoShow' ? 0.6 : 1,
                      }}
                    >
                      <TableCell>{formatLocalTime(appt.appointmentTime)}</TableCell>
                      <TableCell>{appt.patientName}</TableCell>
                      <TableCell>{appt.appointmentType}</TableCell>
                      <TableCell>
                        <AppointmentStatusBadge status={toPatientStatus(appt.status)} />
                      </TableCell>
                      <TableCell>
                        <NoShowRiskBadge
                          score={appt.noShowRiskScore}
                          isEstimated={appt.isRiskEstimated}
                        />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}
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

