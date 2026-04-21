/**
 * AppointmentHistoryPage — SCR-007 Appointment History (US_024).
 *
 * Displays the patient's full appointment history with:
 *   - Sortable date column (AC-2): default newest-first (AC-1)
 *   - Pagination: 10 items per page with Previous/Next navigation (AC-3)
 *   - Status badges (Scheduled, Completed, Cancelled, No-Show) per UXR-401 (AC-4)
 *   - Cancelled rows visible with muted opacity (EC-2)
 *   - Breadcrumb: Dashboard > Appointment History
 *   - "Book New" CTA in header (wireframe SCR-007)
 *   - Empty state: "No appointments found. Book your first appointment!" with link to SCR-006 (EC-1)
 *   - Skeleton loading (UXR-502)
 *   - Error state (UXR-601)
 *   - Responsive: table on md+, card list on mobile (UXR-301)
 *   - Keyboard-navigable sortable header and pagination controls (UXR-202, UXR-203)
 *
 * History data is shared with the cancel/reschedule mutations via
 * queryKey ['patient-appointments'], so cancellations and reschedules on the
 * dashboard are immediately reflected here on next visit.
 */

import { useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Snackbar from '@mui/material/Snackbar';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import AppointmentHistoryTable from '@/components/appointments/AppointmentHistoryTable';
import CancelAppointmentDialog, {
  type AppointmentSummary,
} from '@/components/appointments/CancelAppointmentDialog';
import { useAppointmentHistory } from '@/hooks/useAppointmentHistory';
import { useCancelAppointment, CANCELLATION_MESSAGES } from '@/hooks/useCancelAppointment';
import type { CancellationOutcome } from '@/hooks/useCancelAppointment';
export default function AppointmentHistoryPage() {
  // ─── History data + pagination + sort ─────────────────────────────────────
  const {
    pagedAppointments,
    totalCount,
    totalPages,
    page,
    setPage,
    sortDirection,
    toggleSort,
    isLoading,
    isError,
  } = useAppointmentHistory();

  // ─── Cancellation dialog state ─────────────────────────────────────────────
  const [dialogOpen, setDialogOpen]               = useState(false);
  const [selectedAppointment, setSelectedAppointment] =
    useState<AppointmentSummary | null>(null);
  const [dialogOutcome, setDialogOutcome] = useState<
    Exclude<CancellationOutcome, 'success'> | null
  >(null);
  const [dialogOutcomeMessage, setDialogOutcomeMessage] = useState<string | null>(null);
  const [successToast, setSuccessToast]           = useState(false);

  const cancelMutation = useCancelAppointment();

  // ─── Cancel flow ──────────────────────────────────────────────────────────

  const handleDialogClose = () => {
    if (!cancelMutation.isLoading) {
      setDialogOpen(false);
      setSelectedAppointment(null);
      setDialogOutcome(null);
      setDialogOutcomeMessage(null);
      cancelMutation.reset();
    }
  };

  const handleConfirmCancel = async () => {
    if (!selectedAppointment) return;
    setDialogOutcome(null);
    setDialogOutcomeMessage(null);

    try {
      await cancelMutation.mutateAsync({ appointmentId: selectedAppointment.id });
      setDialogOpen(false);
      setSelectedAppointment(null);
      cancelMutation.reset();
      setSuccessToast(true);
    } catch (err) {
      const cancellationError = err as {
        outcome: Exclude<CancellationOutcome, 'success'>;
        message: string;
      };
      setDialogOutcome(cancellationError.outcome);
      setDialogOutcomeMessage(cancellationError.message);
    }
  };

  // ─── Render ───────────────────────────────────────────────────────────────

  return (
    <Box sx={{ flexGrow: 1 }}>
      <AppBar position="static" sx={{ bgcolor: 'primary.main' }}>
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            UPACIP — Patient Portal
          </Typography>
        </Toolbar>
      </AppBar>

      <Container maxWidth="lg" sx={{ mt: 3, pb: 6 }}>
        {/* Breadcrumb — wireframe SCR-007 */}
        <Breadcrumbs aria-label="Breadcrumb" sx={{ mb: 2 }}>
          <Link
            component={RouterLink}
            to="/patient/dashboard"
            underline="hover"
            color="inherit"
          >
            Dashboard
          </Link>
          <Typography color="text.primary">Appointment History</Typography>
        </Breadcrumbs>

        {/* Page heading + Book New CTA */}
        <Box
          sx={{
            display:        'flex',
            alignItems:     'center',
            justifyContent: 'space-between',
            mb:             3,
          }}
        >
          <Typography variant="h5" component="h1">
            Appointment History
          </Typography>
          <Button
            variant="contained"
            size="small"
            component={RouterLink}
            to="/patient/appointments/book"
            aria-label="Book a new appointment"
          >
            Book New
          </Button>
        </Box>

        {/* Loading skeletons — UXR-502 */}
        {isLoading && (
          <Box aria-busy="true" aria-label="Loading appointment history">
            {[1, 2, 3, 4].map((i) => (
              <Skeleton
                key={i}
                variant="rectangular"
                height={52}
                sx={{ mb: 1, borderRadius: 1 }}
              />
            ))}
          </Box>
        )}

        {/* Error state — UXR-601 */}
        {isError && !isLoading && (
          <Alert severity="error" role="alert">
            Unable to load your appointment history. Please refresh the page or try again later.
          </Alert>
        )}

        {/* Empty state — EC-1 */}
        {!isLoading && !isError && totalCount === 0 && (
          <Paper variant="outlined" sx={{ p: 4, textAlign: 'center' }}>
            <Typography variant="body1" color="text.secondary" gutterBottom>
              No appointments found.{' '}
              <Link
                component={RouterLink}
                to="/patient/appointments/book"
                underline="hover"
                aria-label="Book your first appointment"
              >
                Book your first appointment!
              </Link>
            </Typography>
          </Paper>
        )}

        {/* History table — populated state */}
        {!isLoading && !isError && totalCount > 0 && (
          <Paper variant="outlined">
            {/* Table heading row */}
            <Box
              sx={{
                display:        'flex',
                alignItems:     'center',
                justifyContent: 'space-between',
                px:             2,
                py:             1.5,
              }}
            >
              <Typography variant="subtitle1" fontWeight={600}>
                Past Appointments
              </Typography>
              <Typography variant="caption" color="text.secondary">
                {totalCount} total
              </Typography>
            </Box>

            <AppointmentHistoryTable
              pagedAppointments={pagedAppointments}
              totalPages={totalPages}
              page={page}
              sortDirection={sortDirection}
              onPageChange={setPage}
              onSortToggle={toggleSort}
            />
          </Paper>
        )}
      </Container>

      {/* Cancellation confirmation dialog — UXR-102 */}
      <CancelAppointmentDialog
        open={dialogOpen}
        appointment={selectedAppointment}
        onConfirm={() => void handleConfirmCancel()}
        onClose={handleDialogClose}
        isLoading={cancelMutation.isLoading}
        outcome={dialogOutcome}
        outcomeMessage={dialogOutcomeMessage}
      />

      {/* Success toast — cancellation */}
      <Snackbar
        open={successToast}
        autoHideDuration={5000}
        onClose={() => setSuccessToast(false)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
        aria-live="polite"
      >
        <Alert
          onClose={() => setSuccessToast(false)}
          severity="success"
          variant="filled"
          sx={{ width: '100%' }}
        >
          {CANCELLATION_MESSAGES.SUCCESS}
        </Alert>
      </Snackbar>
    </Box>
  );
}
