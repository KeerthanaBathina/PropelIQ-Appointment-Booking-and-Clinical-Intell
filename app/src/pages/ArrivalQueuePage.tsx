/**
 * ArrivalQueuePage — SCR-011
 *
 * Real-time arrival queue dashboard for staff with no-show risk indicators
 * (US_026 AC-2, AC-3, EC-1, EC-2; UXR-103, UXR-403).
 *
 * Displays all queued patients for today with:
 *   - Queue position, patient name, appointment time, provider, type
 *   - Live wait timer (minutes since arrival)
 *   - No-show risk score chip with color banding (green/amber/red) (AC-2)
 *   - "Est." label for patients with <3 prior appointments (AC-3)
 *   - High-risk row highlight (red surface tint) for ≥70 scores (EC-2)
 *   - Skeleton loading placeholders (UXR-502)
 *   - Error and empty states (UXR-601)
 *
 * EC-1: staleTime=60s ensures updated scores appear on next staff refresh
 *       without manual browser recalculation.
 *
 * Accessibility (UXR-201, UXR-203):
 *   - Table has aria-label
 *   - Status Chips have aria-label
 *   - Risk badges include score, band, and estimated state in aria-label
 *   - Live region aria-live="polite" for queue count updates (UXR-206)
 *
 * Responsive (UXR-301, UXR-302): Table scrolls horizontally on mobile;
 *   sidebar collapses at <768px via AppBar + nav pattern.
 */

import AppBar from '@mui/material/AppBar';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Chip from '@mui/material/Chip';
import Container from '@mui/material/Container';
import Link from '@mui/material/Link';
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
import NoShowRiskBadge from '@/components/staff/NoShowRiskBadge';
import { useStaffAppointments } from '@/hooks/useStaffAppointments';
import type { StaffAppointment } from '@/hooks/useStaffAppointments';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatLocalTime(utcIso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    hour:   'numeric',
    minute: '2-digit',
  }).format(new Date(utcIso));
}

function formatWaitTime(minutes: number | null): string {
  if (minutes === null) return '—';
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return `${h}h ${m}m`;
}

/** Derive display label for queue status. */
function statusLabel(appt: StaffAppointment): string {
  switch (appt.status) {
    case 'Arrived':   return 'Waiting';
    case 'InVisit':   return 'In Visit';
    case 'NoShow':    return 'No-Show';
    case 'Scheduled': return 'Scheduled';
    default:          return appt.status;
  }
}

/** MUI Chip color variant for queue status. */
function statusChipColor(appt: StaffAppointment): 'default' | 'warning' | 'success' | 'error' {
  switch (appt.status) {
    case 'Arrived':   return 'warning';
    case 'InVisit':   return 'success';
    case 'NoShow':    return 'error';
    default:          return 'default';
  }
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ArrivalQueuePage() {
  const { data: appointments, isLoading, isError } = useStaffAppointments();

  // Filter to only queued/arrived patients (SCR-011 queue view)
  const queued = appointments?.filter(
    a => a.queuePosition !== null || a.status === 'Arrived' || a.status === 'InVisit',
  ) ?? [];

  const highRiskCount = queued.filter(
    a => a.noShowRiskScore !== null && a.noShowRiskScore >= 70,
  ).length;

  return (
    <Box sx={{ flexGrow: 1 }}>
      {/* ── AppBar — secondary accent (UXR-403) ── */}
      <AppBar position="static" sx={{ bgcolor: 'secondary.main' }}>
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            UPACIP — Staff Portal
          </Typography>
        </Toolbar>
      </AppBar>

      <Container maxWidth="lg" sx={{ mt: 3, mb: 6 }}>
        {/* ── Breadcrumb (UXR-003) ── */}
        <Breadcrumbs aria-label="Breadcrumb" sx={{ mb: 2 }}>
          <Link
            href="/staff/dashboard"
            underline="hover"
            color="inherit"
            sx={{ fontSize: '0.875rem' }}
          >
            Staff Dashboard
          </Link>
          <Typography color="text.primary" sx={{ fontSize: '0.875rem' }}>
            Arrival Queue
          </Typography>
        </Breadcrumbs>

        {/* ── Header row ── */}
        <Box
          sx={{
            display:        'flex',
            alignItems:     'center',
            justifyContent: 'space-between',
            mb:             3,
            flexWrap:       'wrap',
            gap:            1,
          }}
        >
          <Typography variant="h5" component="h1">
            Today's Queue
          </Typography>
          {/* Live region — screen readers announce count updates (UXR-206) */}
          <Typography
            variant="body2"
            color="text.secondary"
            aria-live="polite"
            aria-atomic="true"
          >
            {isLoading
              ? 'Loading…'
              : `${queued.length} patient${queued.length !== 1 ? 's' : ''}`}
          </Typography>
        </Box>

        {/* ── High-risk alert banner (EC-2) ── */}
        {!isLoading && !isError && highRiskCount > 0 && (
          <Alert severity="warning" sx={{ mb: 3 }} role="status">
            {highRiskCount} patient{highRiskCount !== 1 ? 's' : ''} in queue{' '}
            {highRiskCount !== 1 ? 'have' : 'has'} a high no-show risk score (≥70).
            Consider proactive outreach.
          </Alert>
        )}

        {/* ── Error state ── */}
        {isError && (
          <Alert severity="error" sx={{ mb: 3 }}>
            Unable to load the arrival queue. Please refresh the page.
          </Alert>
        )}

        <Paper variant="outlined">
          {/* ── Loading skeletons (UXR-502) ── */}
          {isLoading && (
            <Box sx={{ p: 3 }} aria-busy="true" aria-label="Loading arrival queue">
              {[0, 1, 2, 3].map(i => (
                <Skeleton key={i} variant="rectangular" height={44} sx={{ mb: 1, borderRadius: 1 }} />
              ))}
            </Box>
          )}

          {/* ── Empty state ── */}
          {!isLoading && !isError && queued.length === 0 && (
            <Box sx={{ p: 4, textAlign: 'center' }}>
              <Typography variant="body1" color="text.secondary">
                No patients currently in the queue.
              </Typography>
            </Box>
          )}

          {/* ── Queue table ── */}
          {!isLoading && !isError && queued.length > 0 && (
            <TableContainer>
              <Table
                size="small"
                aria-label="Arrival queue with no-show risk scores"
              >
                <TableHead>
                  <TableRow>
                    <TableCell>#</TableCell>
                    <TableCell>Patient</TableCell>
                    <TableCell>Appt Time</TableCell>
                    <TableCell>Provider</TableCell>
                    <TableCell>Type</TableCell>
                    <TableCell>Wait</TableCell>
                    {/* No-Show Risk column (US_026 AC-2) */}
                    <TableCell>
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                        No-Show Risk
                        <Tooltip
                          title="Green <30 (Low) · Amber 30–69 (Medium) · Red ≥70 (High). Est. = estimated for new patients."
                          arrow
                          placement="top"
                        >
                          <InfoOutlinedIcon
                            fontSize="small"
                            sx={{ color: 'text.secondary', cursor: 'help' }}
                            aria-label="No-show risk score legend"
                          />
                        </Tooltip>
                      </Box>
                    </TableCell>
                    <TableCell>Status</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {queued.map((appt, idx) => {
                    const isHighRisk =
                      appt.noShowRiskScore !== null && appt.noShowRiskScore >= 70;

                    return (
                      <TableRow
                        key={appt.id}
                        sx={{
                          // Red surface tint for high-risk rows (EC-2)
                          bgcolor:
                            isHighRisk ? '#FFEBEE' : undefined,
                          opacity: appt.status === 'NoShow' ? 0.6 : 1,
                          // Smooth transition so row color doesn't flash on re-render
                          transition: 'background-color 300ms',
                        }}
                      >
                        <TableCell>{appt.queuePosition ?? idx + 1}</TableCell>
                        <TableCell>
                          <Typography variant="body2" fontWeight={isHighRisk ? 600 : 400}>
                            {appt.patientName}
                          </Typography>
                        </TableCell>
                        <TableCell>{formatLocalTime(appt.appointmentTime)}</TableCell>
                        <TableCell>{appt.providerName}</TableCell>
                        <TableCell>{appt.appointmentType}</TableCell>
                        <TableCell>
                          <Typography
                            variant="body2"
                            color={appt.waitMinutes !== null && appt.waitMinutes > 30 ? 'error.main' : 'text.secondary'}
                            aria-label={`Wait time: ${formatWaitTime(appt.waitMinutes)}`}
                          >
                            {formatWaitTime(appt.waitMinutes)}
                          </Typography>
                        </TableCell>
                        <TableCell>
                          <NoShowRiskBadge
                            score={appt.noShowRiskScore}
                            isEstimated={appt.isRiskEstimated}
                          />
                        </TableCell>
                        <TableCell>
                          <Chip
                            label={statusLabel(appt)}
                            color={statusChipColor(appt)}
                            size="small"
                            aria-label={`Status: ${statusLabel(appt)}`}
                            sx={{ fontWeight: 600, fontSize: '0.7rem', height: 22 }}
                          />
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </Paper>
      </Container>
    </Box>
  );
}
