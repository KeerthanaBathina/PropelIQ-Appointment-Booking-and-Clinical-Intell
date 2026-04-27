/**
 * QueueHistoryView — Historical queue analytics view (US_056 AC-3, AC-4, SCR-011).
 *
 * Layout (wireframe-aligned):
 *   ┌─ Date Range Controls ──────────────────────────────────────┐
 *   │  [Start Date]   [End Date]   [Export CSV button]          │
 *   └────────────────────────────────────────────────────────────┘
 *   ┌─ Summary Metric Cards ─────────────────────────────────────┐
 *   │  Avg Wait Time  │  No-Show Count  │  Patient Throughput    │
 *   └────────────────────────────────────────────────────────────┘
 *   ┌─ Daily Breakdown Table ─────────────────────────────────────┐
 *   │  Date │ Avg Wait │ No-Shows │ Throughput │ Total            │
 *   └─────────────────────────────────────────────────────────────┘
 *
 * Uses MUI TextField type="date" (HTML5 native, no @mui/x-date-pickers needed).
 * Empty states:
 *   - "No data available for the selected period" when metrics is empty (AC-3 edge case)
 *   - Query disabled when dates are invalid / missing
 *
 * Status badge colours per designsystem.md:
 *   appointment-status palette used in QueueStatusBadge component.
 */

import { useState } from 'react';

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CircularProgress from '@mui/material/CircularProgress';
import Divider from '@mui/material/Divider';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import AccessTimeIcon from '@mui/icons-material/AccessTime';
import PersonOffIcon from '@mui/icons-material/PersonOff';
import PeopleAltIcon from '@mui/icons-material/PeopleAlt';
import EventBusyIcon from '@mui/icons-material/EventBusy';

import { useQueueHistory } from '@/hooks/useQueueHistory';
import QueueHistoryExportButton from './QueueHistoryExportButton';

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Returns today's date as a YYYY-MM-DD string. */
function todayISO(): string {
  return new Date().toISOString().slice(0, 10);
}

/** Returns a date N days before today as YYYY-MM-DD. */
function daysAgoISO(n: number): string {
  const d = new Date();
  d.setDate(d.getDate() - n);
  return d.toISOString().slice(0, 10);
}

function formatMinutes(mins: number): string {
  if (mins < 60) return `${Math.round(mins)} min`;
  const h = Math.floor(mins / 60);
  const m = Math.round(mins % 60);
  return m > 0 ? `${h}h ${m}m` : `${h}h`;
}

function formatDate(iso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    month: 'short', day: 'numeric', year: 'numeric',
  }).format(new Date(iso + 'T00:00:00'));
}

// ─── Metric Card ──────────────────────────────────────────────────────────────

interface MetricCardProps {
  icon:    React.ReactNode;
  label:   string;
  value:   string;
  colour:  string;
  loading: boolean;
}

function MetricCard({ icon, label, value, colour, loading }: MetricCardProps) {
  return (
    <Card variant="outlined" sx={{ flex: 1, minWidth: 140 }}>
      <CardContent sx={{ display: 'flex', flexDirection: 'column', gap: 0.5, pb: '12px !important' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, color: colour }}>
          {icon}
          <Typography variant="caption" color="text.secondary" fontWeight={500}>
            {label}
          </Typography>
        </Box>
        {loading ? (
          <Skeleton width={64} height={32} />
        ) : (
          <Typography variant="h6" fontWeight={700} color={colour}>
            {value}
          </Typography>
        )}
      </CardContent>
    </Card>
  );
}

// ─── Component ────────────────────────────────────────────────────────────────

interface Props {
  /** Called when export fails so parent can display a snackbar. */
  onExportError?: (message: string) => void;
}

export default function QueueHistoryView({ onExportError }: Props) {
  const [startDate, setStartDate] = useState<string>(daysAgoISO(7));
  const [endDate,   setEndDate]   = useState<string>(todayISO());

  const { data, isLoading, isError, error } = useQueueHistory(startDate, endDate);

  const summary   = data?.summary;
  const metrics   = data?.metrics ?? [];
  const isEmpty   = !isLoading && !isError && metrics.length === 0;
  const dateValid = startDate.length > 0 && endDate.length > 0 && startDate <= endDate;

  return (
    <Box>
      {/* ── Date range controls ─────────────────────────────────────────── */}
      <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
        <Box
          sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', alignItems: 'flex-end' }}
          role="group"
          aria-label="Queue history date range"
        >
          <TextField
            label="Start Date"
            type="date"
            size="small"
            value={startDate}
            onChange={(e) => setStartDate(e.target.value)}
            InputLabelProps={{ shrink: true }}
            inputProps={{
              max: endDate || todayISO(),
              'aria-label': 'History start date',
            }}
            sx={{ minWidth: 150 }}
          />
          <TextField
            label="End Date"
            type="date"
            size="small"
            value={endDate}
            onChange={(e) => setEndDate(e.target.value)}
            InputLabelProps={{ shrink: true }}
            inputProps={{
              min: startDate,
              max: todayISO(),
              'aria-label': 'History end date',
            }}
            sx={{ minWidth: 150 }}
          />
          <QueueHistoryExportButton
            startDate={startDate}
            endDate={endDate}
            onError={onExportError}
          />
        </Box>

        {/* Date validation warning */}
        {startDate && endDate && startDate > endDate && (
          <Alert severity="warning" sx={{ mt: 1.5 }} role="alert">
            Start date must be on or before end date.
          </Alert>
        )}
      </Paper>

      {/* ── Loading skeleton ─────────────────────────────────────────────── */}
      {isLoading && (
        <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
          <CircularProgress aria-label="Loading queue history" />
        </Box>
      )}

      {/* ── API error ────────────────────────────────────────────────────── */}
      {isError && (
        <Alert severity="error" role="alert">
          {(error as Error)?.message ?? 'Failed to load queue history. Please try again.'}
        </Alert>
      )}

      {/* ── Empty state — no data for selected period (AC-3 edge case) ───── */}
      {isEmpty && dateValid && (
        <Paper
          variant="outlined"
          sx={{
            p: 4, textAlign: 'center',
            display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 1.5,
          }}
          role="status"
          aria-live="polite"
        >
          <EventBusyIcon sx={{ fontSize: 48, color: 'text.disabled' }} />
          <Typography variant="h6" color="text.secondary">
            No data available for the selected period
          </Typography>
          <Typography variant="body2" color="text.disabled">
            Try selecting a different date range. Queue history is available from the
            date the system was deployed.
          </Typography>
        </Paper>
      )}

      {/* ── Metric cards ─────────────────────────────────────────────────── */}
      {(!isEmpty || isLoading) && dateValid && !isError && (
        <>
          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', mb: 2 }}>
            <MetricCard
              icon={<AccessTimeIcon fontSize="small" />}
              label="Avg Wait Time"
              value={summary ? formatMinutes(summary.avgWaitTimeMinutes) : '—'}
              colour="#1976D2"
              loading={isLoading}
            />
            <MetricCard
              icon={<PersonOffIcon fontSize="small" />}
              label="No-Show Count"
              value={summary ? String(summary.noShowCount) : '—'}
              colour="#D32F2F"
              loading={isLoading}
            />
            <MetricCard
              icon={<PeopleAltIcon fontSize="small" />}
              label="Patient Throughput"
              value={summary ? String(summary.patientThroughput) : '—'}
              colour="#2E7D32"
              loading={isLoading}
            />
          </Box>

          <Divider sx={{ mb: 2 }} />

          {/* ── Daily breakdown table ───────────────────────────────────── */}
          {!isLoading && metrics.length > 0 && (
            <TableContainer component={Paper} variant="outlined">
              <Table size="small" aria-label="Queue history daily breakdown">
                <TableHead>
                  <TableRow>
                    <TableCell><strong>Date</strong></TableCell>
                    <TableCell align="right"><strong>Avg Wait</strong></TableCell>
                    <TableCell align="right"><strong>No-Shows</strong></TableCell>
                    <TableCell align="right"><strong>Throughput</strong></TableCell>
                    <TableCell align="right"><strong>Total Entries</strong></TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {metrics.map((row) => (
                    <TableRow key={row.date} hover>
                      <TableCell>{formatDate(row.date)}</TableCell>
                      <TableCell align="right">{formatMinutes(row.avgWaitTimeMinutes)}</TableCell>
                      <TableCell align="right">
                        <Typography
                          component="span"
                          variant="body2"
                          color={row.noShowCount > 0 ? 'error.main' : 'text.primary'}
                          fontWeight={row.noShowCount > 0 ? 600 : 400}
                        >
                          {row.noShowCount}
                        </Typography>
                      </TableCell>
                      <TableCell align="right">{row.patientThroughput}</TableCell>
                      <TableCell align="right">{row.totalEntries}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </>
      )}
    </Box>
  );
}
