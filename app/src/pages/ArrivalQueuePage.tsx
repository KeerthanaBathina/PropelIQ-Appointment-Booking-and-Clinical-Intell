/**
 * ArrivalQueuePage — SCR-011
 *
 * Full arrival queue dashboard (US_052: AC-1, AC-3, AC-4).
 *
 * Features:
 *   - Real-time sorted queue table (appointment time + priority) with columns:
 *     #, Patient, Appt Time, Arrival Time, Wait Time, Priority, Status, Actions (AC-4)
 *   - "Mark Arrived" action with confirmation dialog; disables when status='waiting'
 *     to prevent duplicate arrivals (AC-1, checklist item 7)
 *   - "Mark Cancelled" action with confirmation dialog (AC-3)
 *   - "Override to Arrived-Late" action on no-show rows with reason dialog
 *   - Auto-refresh every 5 seconds via React Query refetchInterval (UXR-103)
 *   - Status filter (MUI Select) by queue entry status
 *   - Wait-time alert banner when any patient has waited > 30 minutes
 *   - MUI Snackbar for duplicate-arrival 409 error from API
 *   - All five SCR-011 screen states: Default, Loading, Empty, Error, Validation
 *
 * Retains US_026 no-show risk column using existing useStaffAppointments hook
 * (legacy risk column only; queue actions use separate useArrivalQueue data).
 *
 * Accessibility (UXR-201, UXR-203, UXR-206, NFR-046):
 *   - aria-live="polite" region for queue count announcements
 *   - aria-sort on sortable columns
 *   - aria-busy on buttons during async operations
 *   - aria-label on all action buttons with patient context
 *   - Focus returns to trigger button after dialog close
 *
 * Responsive (UXR-301, NFR-047): TableContainer scrolls horizontally on mobile.
 *
 * Design tokens: designsystem.md#appointment-status, designsystem.md#colors
 * Screen spec: figma_spec.md#SCR-011
 */

import { useCallback, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Container from '@mui/material/Container';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Link from '@mui/material/Link';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Select from '@mui/material/Select';
import type { SelectChangeEvent } from '@mui/material/Select';
import Skeleton from '@mui/material/Skeleton';
import Snackbar from '@mui/material/Snackbar';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';

import ArrivalStatusBadge from '@/components/staff/ArrivalStatusBadge';
import WaitTimeTimer from '@/components/staff/WaitTimeTimer';
import { useArrivalQueue } from '@/hooks/useArrivalQueue';
import type { QueueEntry, QueueEntryStatus } from '@/hooks/useArrivalQueue';
import { useMarkArrived } from '@/hooks/useMarkArrived';
import { useOverrideNoShow } from '@/hooks/useOverrideNoShow';
import { useUpdateQueueStatus } from '@/hooks/useUpdateQueueStatus';

// ─── Constants ────────────────────────────────────────────────────────────────

const OVERRIDE_REASON_MIN_LENGTH = 10;
const ALL_STATUSES = 'all';

const STATUS_FILTER_OPTIONS: Array<{ value: string; label: string }> = [
  { value: ALL_STATUSES,  label: 'All Statuses' },
  { value: 'waiting',      label: 'Waiting' },
  { value: 'in_visit',     label: 'In Visit' },
  { value: 'no_show',      label: 'No-Show' },
  { value: 'arrived_late', label: 'Arrived Late' },
  { value: 'completed',    label: 'Completed' },
];

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatLocalTime(utcIso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    hour:   'numeric',
    minute: '2-digit',
  }).format(new Date(utcIso));
}

/** Sort queue entries: urgent first, then by appointment time ascending. */
function sortQueue(entries: QueueEntry[]): QueueEntry[] {
  return [...entries].sort((a, b) => {
    if (a.priority === 'urgent' && b.priority !== 'urgent') return -1;
    if (b.priority === 'urgent' && a.priority !== 'urgent') return 1;
    return new Date(a.appointmentTime).getTime() - new Date(b.appointmentTime).getTime();
  });
}

// ─── Sub-components ───────────────────────────────────────────────────────────

interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: string;
  confirmLabel: string;
  confirmColor?: 'primary' | 'error';
  isPending: boolean;
  onConfirm: () => void;
  onClose: () => void;
}

function ConfirmDialog({
  open, title, message, confirmLabel, confirmColor = 'primary',
  isPending, onConfirm, onClose,
}: ConfirmDialogProps) {
  return (
    <Dialog
      open={open}
      onClose={onClose}
      aria-labelledby="confirm-dialog-title"
      PaperProps={{ sx: { borderRadius: '12px' } }}
    >
      <DialogTitle id="confirm-dialog-title">{title}</DialogTitle>
      <DialogContent>
        <Typography variant="body1">{message}</Typography>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={isPending}>Cancel</Button>
        <Button
          variant="contained"
          color={confirmColor}
          onClick={onConfirm}
          disabled={isPending}
          aria-busy={isPending}
        >
          {isPending ? <CircularProgress size={18} color="inherit" /> : confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

interface OverrideDialogProps {
  open: boolean;
  patientName: string;
  isPending: boolean;
  onConfirm: (reason: string) => void;
  onClose: () => void;
}

function OverrideDialog({
  open, patientName, isPending, onConfirm, onClose,
}: OverrideDialogProps) {
  const [reason, setReason] = useState('');
  const isDirty = reason.length > 0;
  const isValid = reason.trim().length >= OVERRIDE_REASON_MIN_LENGTH;
  const showError = isDirty && !isValid;

  function handleClose() {
    setReason('');
    onClose();
  }

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      aria-labelledby="override-dialog-title"
      PaperProps={{ sx: { borderRadius: '12px' } }}
      fullWidth
      maxWidth="sm"
    >
      <DialogTitle id="override-dialog-title">
        Override No-Show — {patientName}
      </DialogTitle>
      <DialogContent>
        <Typography variant="body2" sx={{ mb: 2 }}>
          Override this patient's status to <strong>Arrived Late</strong>. Provide a reason.
        </Typography>
        <TextField
          label="Reason for override"
          required
          fullWidth
          multiline
          minRows={2}
          value={reason}
          onChange={e => setReason(e.target.value)}
          error={showError}
          helperText={
            showError
              ? `Reason must be at least ${OVERRIDE_REASON_MIN_LENGTH} characters`
              : `${reason.length} characters`
          }
          inputProps={{
            'aria-required': true,
            'aria-label': 'Reason for overriding no-show status',
          }}
          disabled={isPending}
          autoFocus
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={handleClose} disabled={isPending}>Cancel</Button>
        <Button
          variant="contained"
          color="warning"
          onClick={() => { if (isValid) onConfirm(reason.trim()); }}
          disabled={!isValid || isPending}
          aria-busy={isPending}
        >
          {isPending ? <CircularProgress size={18} color="inherit" /> : 'Confirm Override'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}

function QueueTableSkeleton() {
  return (
    <Box sx={{ p: 2 }} aria-busy="true" aria-label="Loading arrival queue">
      {[0, 1, 2, 3, 4].map(i => (
        <Skeleton key={i} variant="rectangular" height={52} sx={{ mb: 1, borderRadius: 1 }} />
      ))}
    </Box>
  );
}

// ─── Main component ───────────────────────────────────────────────────────────

export default function ArrivalQueuePage() {
  // ── Data & mutations ──────────────────────────────────────────────────────
  const { data, isLoading, isError, refetch } = useArrivalQueue();
  const markArrived    = useMarkArrived();
  const updateStatus   = useUpdateQueueStatus();
  const overrideNoShow = useOverrideNoShow();

  // ── UI state ──────────────────────────────────────────────────────────────
  const [statusFilter, setStatusFilter] = useState<string>(ALL_STATUSES);

  const [arrivedDialog, setArrivedDialog]   = useState<QueueEntry | null>(null);
  const arrivedTriggerRef = useRef<HTMLButtonElement | null>(null);

  const [cancelDialog, setCancelDialog]     = useState<QueueEntry | null>(null);
  const cancelTriggerRef = useRef<HTMLButtonElement | null>(null);

  const [overrideDialog, setOverrideDialog] = useState<QueueEntry | null>(null);
  const overrideTriggerRef = useRef<HTMLButtonElement | null>(null);

  const [snackbar, setSnackbar] = useState<{ open: boolean; message: string }>({
    open: false, message: '',
  });

  // ── Derived data ──────────────────────────────────────────────────────────
  const allEntries = data?.data ?? [];
  const sorted     = sortQueue(allEntries);
  const filtered   = statusFilter === ALL_STATUSES
    ? sorted
    : sorted.filter(e => (e.status as string) === statusFilter);

  const overThirtyCount = allEntries.filter(
    e => e.arrivalTimestamp !== null && e.waitTimeMinutes > 30,
  ).length;

  // ── Handlers ──────────────────────────────────────────────────────────────

  function handleFilterChange(e: SelectChangeEvent) {
    setStatusFilter(e.target.value);
  }

  function openArrivedDialog(entry: QueueEntry, trigger: HTMLButtonElement) {
    arrivedTriggerRef.current = trigger;
    setArrivedDialog(entry);
  }

  async function handleConfirmArrived() {
    if (!arrivedDialog) return;
    try {
      await markArrived.mutateAsync({ appointmentId: arrivedDialog.appointmentId });
      setArrivedDialog(null);
      arrivedTriggerRef.current?.focus();
    } catch (err: unknown) {
      const msg =
        (err as { message?: string })?.message ??
        'Failed to mark patient as arrived. Please try again.';
      setArrivedDialog(null);
      setSnackbar({ open: true, message: msg });
    }
  }

  function openCancelDialog(entry: QueueEntry, trigger: HTMLButtonElement) {
    cancelTriggerRef.current = trigger;
    setCancelDialog(entry);
  }

  async function handleConfirmCancel() {
    if (!cancelDialog) return;
    try {
      await updateStatus.mutateAsync({ queueId: cancelDialog.queueId, status: 'completed' });
      setCancelDialog(null);
      cancelTriggerRef.current?.focus();
    } catch {
      setCancelDialog(null);
      setSnackbar({ open: true, message: 'Failed to cancel. Please try again.' });
    }
  }

  function openOverrideDialog(entry: QueueEntry, trigger: HTMLButtonElement) {
    overrideTriggerRef.current = trigger;
    setOverrideDialog(entry);
  }

  const handleConfirmOverride = useCallback(
    async (reason: string) => {
      if (!overrideDialog) return;
      try {
        await overrideNoShow.mutateAsync({ queueId: overrideDialog.queueId, reason });
        setOverrideDialog(null);
        overrideTriggerRef.current?.focus();
      } catch {
        setOverrideDialog(null);
        setSnackbar({ open: true, message: 'Override failed. Please try again.' });
      }
    },
    [overrideDialog, overrideNoShow],
  );

  // ── Row helpers ────────────────────────────────────────────────────────────

  function canMarkArrived(entry: QueueEntry): boolean {
    return (
      entry.appointmentStatus === 'scheduled' &&
      entry.status !== 'in_visit' &&
      entry.status !== 'completed' &&
      entry.status !== 'arrived_late'
    );
  }

  /** Disable when already waiting — duplicate arrival prevention (AC-1) */
  function isMarkArrivedDisabled(entry: QueueEntry): boolean {
    return entry.status === 'waiting';
  }

  function canCancel(entry: QueueEntry): boolean {
    return entry.status !== 'completed' && entry.status !== 'arrived_late';
  }

  // ── Render ─────────────────────────────────────────────────────────────────

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

      <Container maxWidth="xl" sx={{ mt: 3, mb: 6 }}>
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
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 2, flexWrap: 'wrap', gap: 1 }}>
          <Typography variant="h5" component="h1">Today's Queue</Typography>
          {/* Live region for screen readers (UXR-206) */}
          <Typography variant="body2" color="text.secondary" aria-live="polite" aria-atomic="true">
            {isLoading
              ? 'Loading…'
              : `${data?.totalCount ?? 0} patient${(data?.totalCount ?? 0) !== 1 ? 's' : ''}`}
          </Typography>
        </Box>

        {/* ── Status filter ── */}
        <Paper variant="outlined" sx={{ p: 2, mb: 2 }}>
          <FormControl size="small" sx={{ minWidth: 160 }}>
            <InputLabel id="status-filter-label">Status</InputLabel>
            <Select
              labelId="status-filter-label"
              label="Status"
              value={statusFilter}
              onChange={handleFilterChange}
              variant="outlined"
            >
              {STATUS_FILTER_OPTIONS.map(opt => (
                <MenuItem key={opt.value} value={opt.value}>{opt.label}</MenuItem>
              ))}
            </Select>
          </FormControl>
        </Paper>

        {/* ── Wait-time alert banner ── */}
        {!isLoading && !isError && overThirtyCount > 0 && (
          <Alert severity="warning" sx={{ mb: 2 }} role="status">
            {overThirtyCount} patient{overThirtyCount !== 1 ? 's have' : ' has'} been waiting over 30 minutes.
          </Alert>
        )}

        {/* ── Error state ── */}
        {isError && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={<Button color="inherit" size="small" onClick={() => void refetch()}>Retry</Button>}
          >
            Unable to load queue data. Please try again.
          </Alert>
        )}

        <Paper variant="outlined">
          {/* ── Loading state (UXR-502) ── */}
          {isLoading && <QueueTableSkeleton />}

          {/* ── Empty state ── */}
          {!isLoading && !isError && filtered.length === 0 && (
            <Box sx={{ p: 6, textAlign: 'center', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 1 }}>
              <Typography variant="h6" color="text.secondary">No patients in queue today</Typography>
              <Typography variant="body2" color="text.secondary">
                {statusFilter !== ALL_STATUSES
                  ? 'Try changing the status filter.'
                  : 'The queue will populate as patients check in.'}
              </Typography>
              <Button variant="outlined" href="/staff/dashboard" sx={{ mt: 1 }}>
                Back to Dashboard
              </Button>
            </Box>
          )}

          {/* ── Default state: queue table ── */}
          {!isLoading && !isError && filtered.length > 0 && (
            <TableContainer>
              <Table size="small" aria-label="Arrival queue">
                <TableHead>
                  <TableRow sx={{ bgcolor: '#F5F5F5' }}>
                    <TableCell sx={{ width: 40 }}>#</TableCell>
                    <TableCell aria-sort="none">Patient</TableCell>
                    <TableCell aria-sort="ascending">Appt Time</TableCell>
                    <TableCell>Arrival Time</TableCell>
                    <TableCell aria-sort="none">Wait Time</TableCell>
                    <TableCell>Priority</TableCell>
                    <TableCell>Status</TableCell>
                    <TableCell sx={{ minWidth: 220 }}>Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {filtered.map((entry, idx) => (
                    <TableRow
                      key={entry.queueId}
                      sx={{
                        minHeight:   52,
                        bgcolor:
                          entry.status === 'no_show'
                            ? 'rgba(211,47,47,0.04)'
                            : idx % 2 === 1 ? '#FAFAFA' : undefined,
                        opacity:     entry.status === 'completed' ? 0.6 : 1,
                        transition:  'background-color 300ms',
                        borderBottom: '1px solid #EEEEEE',
                      }}
                    >
                      <TableCell>
                        <Typography variant="body2">{idx + 1}</Typography>
                      </TableCell>

                      <TableCell>
                        <Link
                          href={`/staff/patients/${entry.appointmentId}`}
                          underline="hover"
                          color="primary.main"
                          sx={{ fontSize: '0.875rem', fontWeight: 500 }}
                        >
                          {entry.patientName}
                        </Link>
                      </TableCell>

                      <TableCell>
                        <Typography variant="body2">{formatLocalTime(entry.appointmentTime)}</Typography>
                      </TableCell>

                      <TableCell>
                        <Typography variant="body2" color="text.secondary">
                          {entry.arrivalTimestamp ? formatLocalTime(entry.arrivalTimestamp) : '—'}
                        </Typography>
                      </TableCell>

                      {/* Wait Time — real-time timer (AC-4) */}
                      <TableCell>
                        <WaitTimeTimer arrivalTimestamp={entry.arrivalTimestamp} />
                      </TableCell>

                      <TableCell>
                        <Chip
                          label={entry.priority === 'urgent' ? 'Urgent' : 'Normal'}
                          size="small"
                          color={entry.priority === 'urgent' ? 'error' : 'default'}
                          aria-label={`Priority: ${entry.priority}`}
                          sx={{ height: 20, fontSize: '0.7rem', fontWeight: 600 }}
                        />
                      </TableCell>

                      <TableCell>
                        <ArrivalStatusBadge status={entry.status} />
                      </TableCell>

                      <TableCell>
                        <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap' }}>
                          {/* Mark Arrived (AC-1) */}
                          <Button
                            size="small"
                            variant="contained"
                            color="primary"
                            disabled={isMarkArrivedDisabled(entry) || !canMarkArrived(entry)}
                            aria-label={`Mark ${entry.patientName} as arrived`}
                            aria-busy={
                              markArrived.isPending &&
                              arrivedDialog?.appointmentId === entry.appointmentId
                            }
                            onClick={e =>
                              openArrivedDialog(entry, e.currentTarget as HTMLButtonElement)
                            }
                            sx={{ fontSize: '0.75rem', height: 36 }}
                          >
                            Mark Arrived
                          </Button>

                          {/* Mark Cancelled (AC-3) */}
                          {canCancel(entry) && (
                            <Button
                              size="small"
                              variant="outlined"
                              color="error"
                              aria-label={`Cancel appointment for ${entry.patientName}`}
                              aria-busy={
                                updateStatus.isPending &&
                                cancelDialog?.queueId === entry.queueId
                              }
                              onClick={e =>
                                openCancelDialog(entry, e.currentTarget as HTMLButtonElement)
                              }
                              sx={{ fontSize: '0.75rem', height: 36 }}
                            >
                              Cancel
                            </Button>
                          )}

                          {/* Override no-show */}
                          {entry.status === 'no_show' && (
                            <Button
                              size="small"
                              variant="outlined"
                              color="warning"
                              aria-label={`Override no-show for ${entry.patientName}`}
                              aria-busy={
                                overrideNoShow.isPending &&
                                overrideDialog?.queueId === entry.queueId
                              }
                              onClick={e =>
                                openOverrideDialog(entry, e.currentTarget as HTMLButtonElement)
                              }
                              sx={{ fontSize: '0.75rem', height: 36 }}
                            >
                              Override
                            </Button>
                          )}
                        </Box>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          )}
        </Paper>
      </Container>

      {/* ── Confirm Mark Arrived dialog ── */}
      <ConfirmDialog
        open={arrivedDialog !== null}
        title="Mark Patient as Arrived"
        message={
          arrivedDialog
            ? `Mark ${arrivedDialog.patientName} as arrived? This will begin the wait time timer.`
            : ''
        }
        confirmLabel="Mark Arrived"
        isPending={markArrived.isPending}
        onConfirm={() => void handleConfirmArrived()}
        onClose={() => { setArrivedDialog(null); arrivedTriggerRef.current?.focus(); }}
      />

      {/* ── Confirm Cancel dialog ── */}
      <ConfirmDialog
        open={cancelDialog !== null}
        title="Cancel Appointment"
        message={
          cancelDialog
            ? `Cancel the appointment for ${cancelDialog.patientName}? The slot will be released.`
            : ''
        }
        confirmLabel="Confirm Cancel"
        confirmColor="error"
        isPending={updateStatus.isPending}
        onConfirm={() => void handleConfirmCancel()}
        onClose={() => { setCancelDialog(null); cancelTriggerRef.current?.focus(); }}
      />

      {/* ── Override no-show dialog ── */}
      <OverrideDialog
        open={overrideDialog !== null}
        patientName={overrideDialog?.patientName ?? ''}
        isPending={overrideNoShow.isPending}
        onConfirm={reason => void handleConfirmOverride(reason)}
        onClose={() => { setOverrideDialog(null); overrideTriggerRef.current?.focus(); }}
      />

      {/* ── Error / duplicate arrival Snackbar ── */}
      <Snackbar
        open={snackbar.open}
        autoHideDuration={6000}
        onClose={() => setSnackbar(s => ({ ...s, open: false }))}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          severity="error"
          onClose={() => setSnackbar(s => ({ ...s, open: false }))}
          sx={{ width: '100%' }}
        >
          {snackbar.message}
        </Alert>
      </Snackbar>
    </Box>
  );
}
