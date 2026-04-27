/**
 * ScheduleTable — Today's appointment schedule table (US_057 AC-2, SCR-010).
 *
 * Columns: Time | Patient (linked to SCR-013) | Type | Status (color-coded Chip) | No-Show Risk
 *
 * Status colors (appointment-status design tokens, UXR-401):
 *   Scheduled  → Blue   #1976D2
 *   Arrived    → Info   #0288D1
 *   Waiting    → Info   #0288D1
 *   InVisit    → Purple #7B1FA2
 *   Completed  → Green  #388E3C
 *   Cancelled  → Gray   #757575
 *   NoShow     → Red    #D32F2F
 *
 * Loading: MUI Skeleton rows (UXR-502).
 * Empty:   "No appointments today" with link to appointment management (edge case).
 * Error:   MUI Alert shown in parent; component renders null when isError.
 *
 * Usage:
 *   <ScheduleTable rows={data.schedule} isLoading={isLoading} isError={isError} />
 */

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Link from '@mui/material/Link';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import { Link as RouterLink } from 'react-router-dom';
import NoShowRiskBadge from '@/components/staff/NoShowRiskBadge';
import type { ScheduleAppointment, ScheduleStatus } from '@/types/staffDashboard';

// ─── Status badge config (UXR-401, design-system appointment-status-colors) ──

const STATUS_META: Record<ScheduleStatus, { label: string; color: string }> = {
  Scheduled: { label: 'Scheduled', color: '#1976D2' },
  Arrived:   { label: 'Arrived',   color: '#0288D1' },
  Waiting:   { label: 'Waiting',   color: '#0288D1' },
  InVisit:   { label: 'In Visit',  color: '#7B1FA2' },
  Completed: { label: 'Completed', color: '#388E3C' },
  Cancelled: { label: 'Cancelled', color: '#757575' },
  NoShow:    { label: 'No-Show',   color: '#D32F2F' },
};

function StatusChip({ status }: { status: ScheduleStatus }) {
  const { label, color } = STATUS_META[status] ?? STATUS_META.Scheduled;
  return (
    <Chip
      label={label}
      size="small"
      aria-label={`Status: ${label}`}
      sx={{
        bgcolor:    `${color}1A`,
        color,
        fontWeight: 600,
        fontSize:   '0.7rem',
        height:     22,
      }}
    />
  );
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatLocalTime(utcIso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    hour:   'numeric',
    minute: '2-digit',
  }).format(new Date(utcIso));
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  rows?: ScheduleAppointment[];
  isLoading: boolean;
  isError: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ScheduleTable({ rows, isLoading, isError }: Props) {
  if (isError) {
    return (
      <Alert severity="error" sx={{ mt: 1 }}>
        Unable to load today's schedule. Please refresh the page.
      </Alert>
    );
  }

  if (isLoading) {
    return (
      <Box aria-busy="true" aria-label="Loading today's schedule">
        {[0, 1, 2, 3].map(i => (
          <Skeleton key={i} variant="rectangular" height={40} sx={{ mb: 1, borderRadius: 1 }} />
        ))}
      </Box>
    );
  }

  if (!rows || rows.length === 0) {
    return (
      <Box sx={{ py: 3, textAlign: 'center' }}>
        <Typography variant="body2" color="text.secondary" gutterBottom>
          No appointments scheduled for today.
        </Typography>
        <Link component={RouterLink} to="/patient/appointments/book" variant="body2">
          View appointment management
        </Link>
      </Box>
    );
  }

  return (
    <TableContainer>
      <Table size="small" aria-label="Today's appointment schedule with no-show risk scores">
        <TableHead>
          <TableRow>
            <TableCell>Time</TableCell>
            <TableCell>Patient</TableCell>
            <TableCell>Type</TableCell>
            <TableCell>Status</TableCell>
            <TableCell>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                No-Show Risk
                <Tooltip
                  title="Green <30 · Amber 30–69 · Red ≥70. 'Est.' shown when fewer than 3 prior visits."
                  arrow
                  placement="right"
                >
                  <InfoOutlinedIcon
                    fontSize="small"
                    sx={{ color: 'text.secondary', cursor: 'help' }}
                    aria-label="No-show risk score legend"
                  />
                </Tooltip>
              </Box>
            </TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {rows.map(appt => (
            <TableRow
              key={appt.id}
              sx={{
                bgcolor: appt.noShowRiskScore !== null && appt.noShowRiskScore >= 70
                  ? 'error.50'
                  : undefined,
                opacity: appt.status === 'NoShow' ? 0.6 : 1,
                '&:hover': { bgcolor: 'action.hover' },
              }}
            >
              <TableCell>{formatLocalTime(appt.appointmentTime)}</TableCell>
              <TableCell>
                <Link
                  component={RouterLink}
                  to={`/staff/patients/${appt.patientId}`}
                  color="primary"
                  underline="hover"
                >
                  {appt.patientName}
                </Link>
              </TableCell>
              <TableCell>{appt.appointmentType}</TableCell>
              <TableCell>
                <StatusChip status={appt.status} />
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
  );
}
