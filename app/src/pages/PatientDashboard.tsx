/**
 * PatientDashboard — SCR-005
 *
 * Patient-facing dashboard showing upcoming appointments with:
 *   - Cancel action for eligible scheduled appointments (AC-1, US_019)
 *   - Reschedule action for eligible scheduled non-walk-in appointments (US_023)
 *   - CancelAppointmentDialog for destructive-action confirmation (UXR-102)
 *   - RescheduleAppointmentDialog for multi-step slot swap (US_023, AC-1 through AC-3)
 *   - Immediate status refresh after cancellation / reschedule (React Query invalidation)
 *   - Skeleton loading states (UXR-502)
 *   - Appointment times displayed in patient's local timezone (EC-2)
 *   - Color-coded status badges per UXR-401
 */

import { useState, useCallback } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Container from '@mui/material/Container';
import Divider from '@mui/material/Divider';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Snackbar from '@mui/material/Snackbar';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import LastLoginBanner from '@/components/auth/LastLoginBanner';
import AppointmentCard from '@/components/appointments/AppointmentCard';
import CancelAppointmentDialog, {
  type AppointmentSummary,
} from '@/components/appointments/CancelAppointmentDialog';
import RescheduleAppointmentDialog from '@/components/appointments/RescheduleAppointmentDialog';
import { usePatientAppointments, type PatientAppointment } from '@/hooks/usePatientAppointments';
import { useCancelAppointment, CANCELLATION_MESSAGES } from '@/hooks/useCancelAppointment';
import type { CancellationOutcome } from '@/hooks/useCancelAppointment';
import { RESCHEDULE_MESSAGES } from '@/hooks/useRescheduleAppointment';
import type { RescheduleConfirmation } from '@/hooks/useRescheduleAppointment';

export default function PatientDashboard() {
  // ─── Cancellation dialog state ─────────────────────────────────────────────
  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedAppointment, setSelectedAppointment] = useState<AppointmentSummary | null>(null);
  const [dialogOutcome, setDialogOutcome] = useState<
    Exclude<CancellationOutcome, 'success'> | null
  >(null);
  const [dialogOutcomeMessage, setDialogOutcomeMessage] = useState<string | null>(null);
  const [successToast, setSuccessToast] = useState(false);

  // ─── Reschedule dialog state (US_023) ─────────────────────────────────────
  const [rescheduleOpen, setRescheduleOpen] = useState(false);
  const [rescheduleAppointment, setRescheduleAppointment] =
    useState<PatientAppointment | null>(null);
  const [rescheduleSuccessToast, setRescheduleSuccessToast] = useState(false);

  // ─── Hooks ────────────────────────────────────────────────────────────────
  const { data: appointments, isLoading, isError } = usePatientAppointments();
  const cancelMutation = useCancelAppointment();

  // ─── Upcoming scheduled appointments ─────────────────────────────────────
  const upcomingAppointments =
    appointments?.filter(
      (a) => a.status === 'Scheduled' && new Date(a.appointmentTime) >= new Date(),
    ) ?? [];

  // ─── Cancel flow ──────────────────────────────────────────────────────────

  const handleCancelClick = useCallback((appointment: PatientAppointment) => {
    setSelectedAppointment({
      id: appointment.id,
      appointmentTime: appointment.appointmentTime,
      providerName: appointment.providerName,
      appointmentType: appointment.appointmentType,
      bookingReference: appointment.bookingReference,
    });
    setDialogOutcome(null);
    setDialogOutcomeMessage(null);
    setDialogOpen(true);
  }, []);

  const handleDialogClose = useCallback(() => {
    if (!cancelMutation.isLoading) {
      setDialogOpen(false);
      setSelectedAppointment(null);
      setDialogOutcome(null);
      setDialogOutcomeMessage(null);
      cancelMutation.reset();
    }
  }, [cancelMutation]);

  const handleConfirmCancel = useCallback(async () => {
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
  }, [selectedAppointment, cancelMutation]);

  // ─── Reschedule flow (US_023) ──────────────────────────────────────────────

  const handleRescheduleClick = useCallback((appointment: PatientAppointment) => {
    setRescheduleAppointment(appointment);
    setRescheduleOpen(true);
  }, []);

  const handleRescheduleClose = useCallback(() => {
    setRescheduleOpen(false);
    setRescheduleAppointment(null);
  }, []);

  const handleRescheduleSuccess = useCallback((_confirmation: RescheduleConfirmation) => {
    setRescheduleSuccessToast(true);
  }, []);

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

      <LastLoginBanner />

      <Container maxWidth="lg" sx={{ mt: 4, pb: 6 }}>
        <Typography variant="h5" component="h1" gutterBottom>
          Welcome to your Patient Portal
        </Typography>

        {/* Quick actions (wireframe SCR-005) */}
        <Box sx={{ display: 'flex', gap: 2, mb: 4, flexWrap: 'wrap' }}>
          <Button
            variant="contained"
            component={RouterLink}
            to="/patient/appointments/book"
            aria-label="Book a new appointment"
          >
            Book Appointment
          </Button>
          <Button
            variant="outlined"
            component={RouterLink}
            to="/patient/appointments/history"
            aria-label="View appointment history"
          >
            View History
          </Button>
        </Box>

        {/* Upcoming Appointments panel */}
        <Paper variant="outlined" sx={{ p: 3 }}>
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              mb: 2,
            }}
          >
            <Typography variant="h6" component="h2">
              Upcoming Appointments
            </Typography>
            <Button
              variant="text"
              size="small"
              component={RouterLink}
              to="/patient/appointments/history"
              aria-label="View full appointment history"
            >
              View History
            </Button>
          </Box>

          <Divider sx={{ mb: 2 }} />

          {/* Loading skeleton — UXR-502 */}
          {isLoading && (
            <Box aria-busy="true" aria-label="Loading your appointments">
              {[1, 2].map((i) => (
                <Skeleton
                  key={i}
                  variant="rectangular"
                  height={72}
                  sx={{ mb: 1, borderRadius: 1 }}
                />
              ))}
            </Box>
          )}

          {/* Error state — UXR-601 */}
          {isError && !isLoading && (
            <Alert severity="error" role="alert">
              Unable to load your appointments. Please refresh the page or try again later.
            </Alert>
          )}

          {/* Empty state */}
          {!isLoading && !isError && upcomingAppointments.length === 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ py: 2 }}>
              You have no upcoming appointments.{' '}
              <RouterLink to="/patient/appointments/book">Book one now.</RouterLink>
            </Typography>
          )}

          {/* Appointment cards with cancel + reschedule actions */}
          {!isLoading &&
            !isError &&
            upcomingAppointments.map((appt) => (
              <AppointmentCard
                key={appt.id}
                appointment={appt}
                onCancel={handleCancelClick}
                onReschedule={handleRescheduleClick}
              />
            ))}
        </Paper>
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

      {/* Reschedule dialog (US_023) */}
      <RescheduleAppointmentDialog
        open={rescheduleOpen}
        appointment={rescheduleAppointment}
        onClose={handleRescheduleClose}
        onSuccess={handleRescheduleSuccess}
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

      {/* Success toast — reschedule (US_023) */}
      <Snackbar
        open={rescheduleSuccessToast}
        autoHideDuration={6000}
        onClose={() => setRescheduleSuccessToast(false)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
        aria-live="polite"
      >
        <Alert
          onClose={() => setRescheduleSuccessToast(false)}
          severity="success"
          variant="filled"
          sx={{ width: '100%' }}
        >
          {RESCHEDULE_MESSAGES.SUCCESS}
        </Alert>
      </Snackbar>
    </Box>
  );
}
