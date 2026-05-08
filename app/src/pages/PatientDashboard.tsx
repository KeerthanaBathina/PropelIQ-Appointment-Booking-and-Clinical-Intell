/**
 * PatientDashboard — SCR-005
 *
 * Patient-facing dashboard showing upcoming appointments with:
 *   - Cancel action for eligible scheduled appointments (AC-1, US_019)
 *   - Reschedule action for eligible scheduled non-walk-in appointments (US_023)
 *   - Add to Calendar action for scheduled appointments to download .ics file (US_025, FR-025)
 *   - CancelAppointmentDialog for destructive-action confirmation (UXR-102)
 *   - RescheduleAppointmentDialog for multi-step slot swap (US_023, AC-1 through AC-3)
 *   - Immediate status refresh after cancellation / reschedule (React Query invalidation)
 *   - Skeleton loading states (UXR-502)
 *   - Appointment times displayed in patient local timezone (EC-2)
 *   - Color-coded status badges per UXR-401
 */

import { useState, useCallback } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Drawer from '@mui/material/Drawer';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemText from '@mui/material/ListItemText';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Snackbar from '@mui/material/Snackbar';
import Typography from '@mui/material/Typography';
import DashboardIcon from '@mui/icons-material/Dashboard';
import CalendarMonthIcon from '@mui/icons-material/CalendarMonth';
import HistoryIcon from '@mui/icons-material/History';
import AssignmentIcon from '@mui/icons-material/Assignment';
import LastLoginBanner from '@/components/auth/LastLoginBanner';
import CancelAppointmentDialog, {
  type AppointmentSummary,
} from '@/components/appointments/CancelAppointmentDialog';
import RescheduleAppointmentDialog from '@/components/appointments/RescheduleAppointmentDialog';
import { usePatientAppointments, type PatientAppointment } from '@/hooks/usePatientAppointments';
import { useCancelAppointment, CANCELLATION_MESSAGES } from '@/hooks/useCancelAppointment';
import type { CancellationOutcome } from '@/hooks/useCancelAppointment';
import { RESCHEDULE_MESSAGES } from '@/hooks/useRescheduleAppointment';
import type { RescheduleConfirmation } from '@/hooks/useRescheduleAppointment';
import { useAuthStore } from '@/hooks/useAuth';

const DRAWER_WIDTH = 220;

const STATUS_COLOR: Record<string, string> = {
  Scheduled: '#1976D2',
  Confirmed: '#0288D1',
  Completed: '#388E3C',
  Cancelled: '#757575',
  NoShow: '#D32F2F',
};

export default function PatientDashboard() {
  const navigate = useNavigate();
  const email = useAuthStore((s) => s.email);
  const displayName = email
    ? email.split('@')[0].replace(/[._-]/g, ' ').replace(/\b\w/g, (c: string) => c.toUpperCase())
    : 'Patient';
  const initials = displayName
    .split(' ')
    .map((w: string) => w[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();

  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedAppointment, setSelectedAppointment] = useState<AppointmentSummary | null>(null);
  const [dialogOutcome, setDialogOutcome] = useState<Exclude<CancellationOutcome, 'success'> | null>(null);
  const [dialogOutcomeMessage, setDialogOutcomeMessage] = useState<string | null>(null);
  const [successToast, setSuccessToast] = useState(false);
  const [rescheduleOpen, setRescheduleOpen] = useState(false);
  const [rescheduleAppointment, setRescheduleAppointment] = useState<PatientAppointment | null>(null);
  const [rescheduleSuccessToast, setRescheduleSuccessToast] = useState(false);

  const { data: appointments, isLoading, isError } = usePatientAppointments();
  const cancelMutation = useCancelAppointment();

  const upcomingAppointments =
    appointments?.filter(
      (a) => a.status === 'Scheduled' && new Date(a.appointmentTime) >= new Date(),
    ) ?? [];
  const completedCount = appointments?.filter((a) => a.status === 'Completed').length ?? 0;
  const pendingIntakeCount = upcomingAppointments.length > 0 ? 1 : 0;

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
      const e = err as { outcome: Exclude<CancellationOutcome, 'success'>; message: string };
      setDialogOutcome(e.outcome);
      setDialogOutcomeMessage(e.message);
    }
  }, [selectedAppointment, cancelMutation]);

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

  const navItems = [
    { label: 'Dashboard', icon: <DashboardIcon fontSize="small" />, path: '/patient/dashboard' },
    { label: 'Book Appointment', icon: <CalendarMonthIcon fontSize="small" />, path: '/patient/appointments/book' },
    { label: 'History', icon: <HistoryIcon fontSize="small" />, path: '/patient/appointments/history' },
    { label: 'Intake', icon: <AssignmentIcon fontSize="small" />, path: '/patient/intake/ai' },
  ];

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: '#f5f5f5' }}>
      {/* Sidebar */}
      <Drawer
        variant="permanent"
        sx={{
          width: DRAWER_WIDTH,
          flexShrink: 0,
          '& .MuiDrawer-paper': {
            width: DRAWER_WIDTH,
            boxSizing: 'border-box',
            bgcolor: '#fff',
            borderRight: '1px solid #e0e0e0',
          },
        }}
      >
        <Box sx={{ px: 3, py: 2.5 }}>
          <Typography variant="h6" fontWeight={700} color="primary" sx={{ letterSpacing: 1 }}>
            UPACIP
          </Typography>
        </Box>
        <Divider />
        <List disablePadding sx={{ mt: 1 }}>
          {navItems.map((item) => {
            const active = window.location.pathname === item.path;
            return (
              <ListItemButton
                key={item.label}
                component={RouterLink}
                to={item.path}
                selected={active}
                sx={{
                  mx: 1,
                  borderRadius: 1,
                  mb: 0.5,
                  '&.Mui-selected': {
                    bgcolor: '#e3f2fd',
                    '& .MuiListItemText-primary': { fontWeight: 600, color: 'primary.main' },
                  },
                }}
              >
                <Box sx={{ mr: 1.5, display: 'flex', color: active ? 'primary.main' : 'text.secondary' }}>
                  {item.icon}
                </Box>
                <ListItemText primary={item.label} primaryTypographyProps={{ fontSize: 14 }} />
              </ListItemButton>
            );
          })}
        </List>
      </Drawer>

      {/* Main content */}
      <Box sx={{ flexGrow: 1, display: 'flex', flexDirection: 'column' }}>
        {/* Top header */}
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            px: 4,
            py: 2,
            bgcolor: '#fff',
            borderBottom: '1px solid #e0e0e0',
          }}
        >
          <Typography variant="h5" fontWeight={600}>
            Patient Dashboard
          </Typography>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
            <Typography variant="body2" color="text.secondary">{displayName}</Typography>
            <Avatar sx={{ width: 36, height: 36, bgcolor: 'primary.main', fontSize: 14, fontWeight: 700 }}>
              {initials}
            </Avatar>
          </Box>
        </Box>

        <LastLoginBanner />

        <Box sx={{ p: 4, flexGrow: 1 }}>
          {/* Stats row */}
          <Box sx={{ display: 'flex', gap: 3, mb: 4 }}>
            <Paper variant="outlined" sx={{ flex: 1, p: 3, textAlign: 'center', borderRadius: 2 }}>
              {isLoading ? <Skeleton variant="text" width={40} height={48} sx={{ mx: 'auto' }} /> : (
                <Typography variant="h3" fontWeight={700} color="#1976D2">{upcomingAppointments.length}</Typography>
              )}
              <Typography variant="caption" color="text.secondary" sx={{ letterSpacing: 1 }}>UPCOMING APPOINTMENTS</Typography>
            </Paper>
            <Paper variant="outlined" sx={{ flex: 1, p: 3, textAlign: 'center', borderRadius: 2 }}>
              {isLoading ? <Skeleton variant="text" width={40} height={48} sx={{ mx: 'auto' }} /> : (
                <Typography variant="h3" fontWeight={700} color="#F57C00">{pendingIntakeCount}</Typography>
              )}
              <Typography variant="caption" color="text.secondary" sx={{ letterSpacing: 1 }}>PENDING INTAKE</Typography>
            </Paper>
            <Paper variant="outlined" sx={{ flex: 1, p: 3, textAlign: 'center', borderRadius: 2 }}>
              {isLoading ? <Skeleton variant="text" width={40} height={48} sx={{ mx: 'auto' }} /> : (
                <Typography variant="h3" fontWeight={700} color="#388E3C">{completedCount}</Typography>
              )}
              <Typography variant="caption" color="text.secondary" sx={{ letterSpacing: 1 }}>COMPLETED VISITS</Typography>
            </Paper>
          </Box>

          {/* Action buttons */}
          <Box sx={{ display: 'flex', gap: 2, mb: 4 }}>
            <Button variant="contained" component={RouterLink} to="/patient/appointments/book">
              BOOK APPOINTMENT
            </Button>
            <Button variant="outlined" component={RouterLink} to="/patient/intake/ai">
              COMPLETE INTAKE
            </Button>
          </Box>

          {/* Upcoming Appointments */}
          <Paper variant="outlined" sx={{ p: 3, borderRadius: 2, mb: 4 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
              <Typography variant="h6" fontWeight={600}>Upcoming Appointments</Typography>
              <Button
                variant="text"
                size="small"
                component={RouterLink}
                to="/patient/appointments/history"
                sx={{ fontWeight: 600, fontSize: 12 }}
              >
                VIEW HISTORY
              </Button>
            </Box>

            {isLoading && (
              <Box aria-busy="true">
                {[1, 2].map((i) => (
                  <Skeleton key={i} variant="rectangular" height={72} sx={{ mb: 1, borderRadius: 1 }} />
                ))}
              </Box>
            )}

            {isError && !isLoading && (
              <Alert severity="error">Unable to load your appointments. Please refresh the page.</Alert>
            )}

            {!isLoading && !isError && upcomingAppointments.length === 0 && (
              <Typography variant="body2" color="text.secondary" sx={{ py: 2 }}>
                You have no upcoming appointments.{' '}
                <RouterLink to="/patient/appointments/book">Book one now.</RouterLink>
              </Typography>
            )}

            {!isLoading && !isError && upcomingAppointments.map((appt) => {
              const statusColor = STATUS_COLOR[appt.status] ?? '#757575';
              const apptDate = new Date(appt.appointmentTime);
              const dateStr = apptDate.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
              const timeStr = apptDate.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
              return (
                <Box
                  key={appt.id}
                  sx={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    borderLeft: `4px solid ${statusColor}`,
                    pl: 2,
                    py: 1.5,
                    mb: 1.5,
                    bgcolor: '#fafafa',
                    borderRadius: '0 6px 6px 0',
                  }}
                >
                  <Box>
                    <Typography variant="body2" fontWeight={600}>{dateStr} — {timeStr}</Typography>
                    <Typography variant="body2" color="text.secondary">
                      {appt.providerName} • {appt.appointmentType}
                    </Typography>
                  </Box>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
                    <Chip
                      label={appt.status.toUpperCase()}
                      size="small"
                      sx={{ bgcolor: statusColor, color: '#fff', fontWeight: 700, fontSize: 11, height: 24 }}
                    />
                    {appt.cancellable && (
                      <Button
                        size="small"
                        color="error"
                        sx={{ fontWeight: 600, fontSize: 12, textTransform: 'uppercase', minWidth: 0 }}
                        onClick={() => handleCancelClick(appt)}
                      >
                        CANCEL
                      </Button>
                    )}
                    <Button
                      size="small"
                      sx={{ fontWeight: 600, fontSize: 12, textTransform: 'uppercase', minWidth: 0 }}
                      onClick={() => handleRescheduleClick(appt)}
                    >
                      RESCHEDULE
                    </Button>
                  </Box>
                </Box>
              );
            })}
          </Paper>

          {/* Intake Status */}
          <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
            <Typography variant="h6" fontWeight={600} mb={2}>Intake Status</Typography>
            {!isLoading && upcomingAppointments.length === 0 ? (
              <Typography variant="body2" color="text.secondary">No pending intake forms.</Typography>
            ) : (
              <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                <Typography variant="body2" color="text.secondary">
                  You have a pending health intake form to complete before your next visit.
                </Typography>
                <Button variant="outlined" size="small" onClick={() => navigate('/patient/intake/ai')} sx={{ ml: 2, whiteSpace: 'nowrap' }}>
                  Complete Now
                </Button>
              </Box>
            )}
          </Paper>
        </Box>
      </Box>

      <CancelAppointmentDialog
        open={dialogOpen}
        appointment={selectedAppointment}
        onConfirm={() => void handleConfirmCancel()}
        onClose={handleDialogClose}
        isLoading={cancelMutation.isLoading}
        outcome={dialogOutcome}
        outcomeMessage={dialogOutcomeMessage}
      />

      <RescheduleAppointmentDialog
        open={rescheduleOpen}
        appointment={rescheduleAppointment}
        onClose={handleRescheduleClose}
        onSuccess={handleRescheduleSuccess}
      />

      <Snackbar
        open={successToast}
        autoHideDuration={5000}
        onClose={() => setSuccessToast(false)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
        aria-live="polite"
      >
        <Alert onClose={() => setSuccessToast(false)} severity="success" variant="filled" sx={{ width: '100%' }}>
          {CANCELLATION_MESSAGES.SUCCESS}
        </Alert>
      </Snackbar>

      <Snackbar
        open={rescheduleSuccessToast}
        autoHideDuration={6000}
        onClose={() => setRescheduleSuccessToast(false)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
        aria-live="polite"
      >
        <Alert onClose={() => setRescheduleSuccessToast(false)} severity="success" variant="filled" sx={{ width: '100%' }}>
          {RESCHEDULE_MESSAGES.SUCCESS}
        </Alert>
      </Snackbar>
    </Box>
  );
}
