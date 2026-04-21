/**
 * RescheduleAppointmentDialog — multi-step dialog for patient appointment rescheduling (US_023).
 *
 * Steps:
 *   1. CONTEXT  — Displays the current appointment details and 24-hour restriction notice.
 *                 CTA: "Select New Slot" → advances to SLOT_SELECT.
 *   2. SLOT_SELECT — Embeds SlotCalendar + TimeSlotGrid (same components as booking flow).
 *                 CTA: "Continue" once a slot is selected → advances to CONFIRM.
 *   3. CONFIRM  — Shows old vs new appointment times side-by-side.
 *                 CTA: "Confirm Reschedule" → calls useRescheduleAppointment.
 *   4. SUCCESS  — Shows RescheduleSuccessNotice (AC-3) and a "Done" button.
 *
 * Error states:
 *   - EC-1 (409): Slot no longer available → Alert + refresh to SLOT_SELECT.
 *   - EC-2 (walk-in): Shows blocked message, no slot selection offered.
 *   - AC-2 (422): "Cannot reschedule within 24 hours of appointment." Alert on CONFIRM step.
 *   - Other errors: generic Alert on CONFIRM step.
 *
 * Timezone semantics:
 *   All times displayed in the patient's local timezone via Intl.DateTimeFormat (EC-2).
 *   The 24-hour eligibility check is NEVER re-evaluated on the client.
 *
 * Accessibility (UXR-201, UXR-202, UXR-203):
 *   - aria-labelledby wires Dialog title.
 *   - aria-describedby wires dialog body.
 *   - Focus trapped within dialog on open.
 *
 * @param open           Whether the dialog is visible.
 * @param appointment    The appointment being rescheduled.
 * @param onClose        Called when dialog is dismissed without completing.
 * @param onSuccess      Called with the RescheduleConfirmation after a successful reschedule.
 */

import { useCallback, useMemo, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import Typography from '@mui/material/Typography';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import EventRepeatIcon from '@mui/icons-material/EventRepeat';
import BlockIcon from '@mui/icons-material/Block';
import {
  useAppointmentSlots,
  getAvailableDates,
  getSlotsForDate,
  type AppointmentSlot,
} from '@/hooks/useAppointmentSlots';
import {
  useRescheduleAppointment,
  RESCHEDULE_MESSAGES,
  type RescheduleConfirmation,
} from '@/hooks/useRescheduleAppointment';
import type { PatientAppointment } from '@/hooks/usePatientAppointments';
import SlotCalendar from './SlotCalendar';
import TimeSlotGrid from './TimeSlotGrid';
import RescheduleSuccessNotice from './RescheduleSuccessNotice';

// ─── Helpers ──────────────────────────────────────────────────────────────────

const MAX_BOOKING_DAYS = 90;

function todayStr(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function maxDateStr(): string {
  const d = new Date();
  d.setDate(d.getDate() + MAX_BOOKING_DAYS);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function formatLocalDateTime(utcIsoString: string): string {
  return new Intl.DateTimeFormat(undefined, {
    year:   'numeric',
    month:  'long',
    day:    'numeric',
    hour:   'numeric',
    minute: '2-digit',
  }).format(new Date(utcIsoString));
}

function formatLocalDate(dateStr: string): string {
  return new Intl.DateTimeFormat(undefined, {
    year:  'numeric',
    month: 'short',
    day:   'numeric',
  }).format(new Date(dateStr));
}

// ─── Step type ────────────────────────────────────────────────────────────────

type Step = 'context' | 'slot_select' | 'confirm' | 'success' | 'walkin_blocked';

// ─── Props ────────────────────────────────────────────────────────────────────

export interface RescheduleDialogProps {
  open: boolean;
  /** The appointment being rescheduled. Null hides the dialog. */
  appointment: PatientAppointment | null;
  onClose: () => void;
  onSuccess: (confirmation: RescheduleConfirmation) => void;
}

// Walk-in type extension — `isWalkIn` may be present on PatientAppointment
// even if not in the base type definition yet.
type MaybeWalkIn = PatientAppointment & { isWalkIn?: boolean };

// ─── Component ────────────────────────────────────────────────────────────────

export default function RescheduleAppointmentDialog({
  open,
  appointment,
  onClose,
  onSuccess,
}: RescheduleDialogProps) {
  const today   = useMemo(todayStr,   []);
  const maxDate = useMemo(maxDateStr, []);

  // ─── Step state ──────────────────────────────────────────────────────────
  const [step, setStep] = useState<Step>('context');

  // ─── Slot selection state ─────────────────────────────────────────────────
  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const [selectedSlot, setSelectedSlot] = useState<AppointmentSlot | null>(null);

  // ─── Error / outcome state ────────────────────────────────────────────────
  const [errorMessage, setErrorMessage]           = useState<string | null>(null);
  const [successConfirmation, setSuccessConfirmation] =
    useState<RescheduleConfirmation | null>(null);

  const rescheduleMutation = useRescheduleAppointment();

  // Walk-in check (EC-2): isWalkIn may not be typed on PatientAppointment yet;
  // read it safely via cast.
  const isWalkIn = (appointment as MaybeWalkIn)?.isWalkIn === true;

  // ─── Slot data for the slot picker ──────────────────────────────────────
  // Always call the hook (Rules of Hooks); use `enabled` to suppress network
  // requests until the patient reaches the slot-selection step.
  const {
    data: slotsData,
    isLoading: slotsLoading,
  } = useAppointmentSlots({ startDate: today, endDate: maxDate });

  const allSlots = slotsData?.slots ?? [];

  const availableDates = useMemo(
    () => getAvailableDates(allSlots),
    [allSlots],
  );

  const slotsForSelectedDate = useMemo(
    () => (selectedDate ? getSlotsForDate(allSlots, selectedDate) : []),
    [allSlots, selectedDate],
  );

  // ─── Reset when dialog opens ─────────────────────────────────────────────
  const handleEntered = useCallback(() => {
    setStep(isWalkIn ? 'walkin_blocked' : 'context');
    setSelectedDate(null);
    setSelectedSlot(null);
    setErrorMessage(null);
    setSuccessConfirmation(null);
    rescheduleMutation.reset();
  }, [isWalkIn, rescheduleMutation]);

  // ─── Step navigation ─────────────────────────────────────────────────────
  const handleGoToSlotSelect = useCallback(() => {
    setErrorMessage(null);
    setSelectedDate(null);
    setSelectedSlot(null);
    rescheduleMutation.reset();
    setStep('slot_select');
  }, [rescheduleMutation]);

  const handleGoToConfirm = useCallback(() => {
    if (!selectedSlot) return;
    setErrorMessage(null);
    rescheduleMutation.reset();
    setStep('confirm');
  }, [selectedSlot, rescheduleMutation]);

  const handleBackToContext = useCallback(() => {
    setErrorMessage(null);
    setStep('context');
  }, []);

  const handleBackToSlotSelect = useCallback(() => {
    setErrorMessage(null);
    rescheduleMutation.reset();
    setStep('slot_select');
  }, [rescheduleMutation]);

  // ─── Slot selection callbacks ────────────────────────────────────────────
  // TimeSlotGrid calls onSlotSelect with just the slotId (string).
  // We look up the full AppointmentSlot from the current date's slot list.
  const handleSlotSelect = useCallback((slotId: string) => {
    const found = slotsForSelectedDate.find((s) => s.slotId === slotId) ?? null;
    setSelectedSlot(found);
  }, [slotsForSelectedDate]);

  const handleDateSelect = useCallback((date: string) => {
    setSelectedDate(date);
    setSelectedSlot(null);
  }, []);

  // ─── Confirm reschedule ──────────────────────────────────────────────────
  const handleConfirmReschedule = useCallback(async () => {
    if (!appointment || !selectedSlot) return;
    setErrorMessage(null);

    try {
      const confirmation = await rescheduleMutation.mutateAsync({
        appointmentId:      appointment.id,
        slotId:             selectedSlot.slotId,
        providerId:         selectedSlot.providerId,
        newAppointmentTime: `${selectedSlot.date}T${selectedSlot.startTime}:00Z`,
        appointmentType:    appointment.appointmentType,
      });
      setSuccessConfirmation(confirmation);
      setStep('success');
      onSuccess(confirmation);
    } catch (err) {
      const rescheduleErr = err as { outcome?: string; message?: string };
      if (rescheduleErr.outcome === 'slot_unavailable') {
        // EC-1: slot taken — go back to slot selection with an error notice
        setErrorMessage(RESCHEDULE_MESSAGES.SLOT_UNAVAILABLE);
        setSelectedSlot(null);
        setStep('slot_select');
      } else {
        setErrorMessage(
          rescheduleErr.message ?? 'An unexpected error occurred. Please try again.',
        );
      }
    }
  }, [appointment, selectedSlot, rescheduleMutation, onSuccess]);

  // ─── Close handler ───────────────────────────────────────────────────────
  const handleClose = useCallback(() => {
    if (rescheduleMutation.isLoading) return; // block close while in-flight
    onClose();
  }, [rescheduleMutation.isLoading, onClose]);

  if (!appointment) return null;

  // ─── Title per step ──────────────────────────────────────────────────────
  const titleMap: Record<Step, string> = {
    context:        'Reschedule Appointment',
    slot_select:    'Choose a New Time',
    confirm:        'Confirm Reschedule',
    success:        'Rescheduled',
    walkin_blocked: 'Cannot Reschedule',
  };

  const showBackArrow =
    step === 'slot_select' || step === 'confirm';

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      fullWidth
      maxWidth="sm"
      TransitionProps={{ onEntered: handleEntered }}
      aria-labelledby="reschedule-dialog-title"
      aria-describedby="reschedule-dialog-content"
    >
      <DialogTitle
        id="reschedule-dialog-title"
        sx={{ display: 'flex', alignItems: 'center', gap: 1 }}
      >
        {showBackArrow && (
          <ArrowBackIcon
            fontSize="small"
            sx={{ cursor: 'pointer', color: 'text.secondary', mr: 0.5 }}
            onClick={step === 'confirm' ? handleBackToSlotSelect : handleBackToContext}
            aria-label="Go back"
            role="button"
            tabIndex={0}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                step === 'confirm' ? handleBackToSlotSelect() : handleBackToContext();
              }
            }}
          />
        )}
        {step !== 'success' && (
          <EventRepeatIcon fontSize="small" color="primary" aria-hidden="true" />
        )}
        <span>{titleMap[step]}</span>
      </DialogTitle>

      <Divider />

      <DialogContent id="reschedule-dialog-content" sx={{ pt: 2 }}>

        {/* ── Step: WALK-IN BLOCKED (EC-2) ─────────────────────────────── */}
        {step === 'walkin_blocked' && (
          <Box
            sx={{
              display:       'flex',
              flexDirection: 'column',
              alignItems:    'center',
              gap:           2,
              py:            2,
            }}
          >
            <BlockIcon sx={{ fontSize: 48, color: 'text.disabled' }} aria-hidden="true" />
            <Typography variant="body1" fontWeight={500} textAlign="center">
              Walk-in Appointments Cannot Be Rescheduled
            </Typography>
            <Typography variant="body2" color="text.secondary" textAlign="center">
              This appointment was created as a walk-in. Rescheduling is only available for
              booked appointments. Please contact the clinic for assistance.
            </Typography>
          </Box>
        )}

        {/* ── Step: CONTEXT ─────────────────────────────────────────────── */}
        {step === 'context' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <Typography variant="body2" color="text.secondary">
              You are about to reschedule the following appointment:
            </Typography>

            {/* Current appointment summary */}
            <Box
              sx={{
                border:      '1px solid',
                borderColor: 'divider',
                borderRadius: 1,
                p:           2,
                bgcolor:     'action.hover',
              }}
            >
              <Typography variant="body2" fontWeight={600} gutterBottom>
                {formatLocalDateTime(appointment.appointmentTime)}
              </Typography>
              <Typography variant="caption" color="text.secondary" display="block">
                {appointment.appointmentType} with {appointment.providerName}
              </Typography>
              {appointment.bookingReference && (
                <Typography
                  variant="caption"
                  color="text.disabled"
                  display="block"
                  sx={{ mt: 0.25 }}
                >
                  {appointment.bookingReference}
                </Typography>
              )}
            </Box>

            {/* 24-hour restriction notice */}
            <Alert severity="info" sx={{ fontSize: '0.8125rem' }}>
              Appointments cannot be rescheduled within 24 hours of the scheduled time.
            </Alert>
          </Box>
        )}

        {/* ── Step: SLOT SELECTION ──────────────────────────────────────── */}
        {step === 'slot_select' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {/* EC-1: slot taken error — shown at top of slot selection */}
            {errorMessage && (
              <Alert
                severity="warning"
                onClose={() => setErrorMessage(null)}
              >
                {errorMessage}
              </Alert>
            )}

            <SlotCalendar
              selectedDate={selectedDate}
              onDateSelect={handleDateSelect}
              availableDates={availableDates}
              loading={slotsLoading}
            />

            {selectedDate && (
              <TimeSlotGrid
                slots={slotsForSelectedDate}
                selectedSlotId={selectedSlot?.slotId ?? null}
                onSlotSelect={handleSlotSelect}
                onTryDifferentDate={() => setSelectedDate(null)}
                loading={slotsLoading}
                selectedDate={selectedDate}
              />
            )}

            {!selectedDate && !slotsLoading && (
              <Typography
                variant="body2"
                color="text.secondary"
                textAlign="center"
                sx={{ py: 1 }}
              >
                Select a date above to see available slots.
              </Typography>
            )}
          </Box>
        )}

        {/* ── Step: CONFIRM ─────────────────────────────────────────────── */}
        {step === 'confirm' && selectedSlot && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {/* Error alert (AC-2 policy block or generic) */}
            {errorMessage && (
              <Alert severity="error">
                {errorMessage}
              </Alert>
            )}

            {/* Side-by-side old vs new time */}
            <Box
              sx={{
                display:              'grid',
                gridTemplateColumns:  '1fr 1fr',
                gap:                  2,
              }}
            >
              {/* Original time */}
              <Box
                sx={{
                  border:      '1px solid',
                  borderColor: 'divider',
                  borderRadius: 1,
                  p:           1.5,
                  bgcolor:     'action.hover',
                }}
              >
                <Typography
                  variant="caption"
                  color="text.secondary"
                  display="block"
                  gutterBottom
                >
                  Current appointment
                </Typography>
                <Typography
                  variant="body2"
                  sx={{ textDecoration: 'line-through', color: 'text.secondary' }}
                  aria-label={`Current: ${formatLocalDateTime(appointment.appointmentTime)}`}
                >
                  {formatLocalDate(appointment.appointmentTime)}
                </Typography>
              </Box>

              {/* New time */}
              <Box
                sx={{
                  border:      '1px solid',
                  borderColor: 'primary.main',
                  borderRadius: 1,
                  p:           1.5,
                }}
              >
                <Typography
                  variant="caption"
                  color="primary.main"
                  display="block"
                  gutterBottom
                >
                  New appointment
                </Typography>
                <Typography
                  variant="body2"
                  fontWeight={600}
                  color="primary.main"
                  aria-label={`New: ${formatLocalDate(selectedSlot.date)}`}
                >
                  {formatLocalDate(selectedSlot.date)}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {selectedSlot.startTime} – {selectedSlot.endTime}
                </Typography>
              </Box>
            </Box>

            <Divider />

            <Typography variant="body2" color="text.secondary">
              Provider: <strong>{selectedSlot.providerName}</strong>
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Type: <strong>{appointment.appointmentType}</strong>
            </Typography>

            <Alert severity="warning" sx={{ fontSize: '0.8125rem' }}>
              Once confirmed, your original slot will be released and cannot be recovered.
            </Alert>
          </Box>
        )}

        {/* ── Step: SUCCESS (AC-3) ──────────────────────────────────────── */}
        {step === 'success' && successConfirmation && (
          <RescheduleSuccessNotice confirmation={successConfirmation} />
        )}

      </DialogContent>

      <Divider />

      <DialogActions sx={{ px: 3, py: 2, gap: 1 }}>

        {/* WALK-IN BLOCKED */}
        {step === 'walkin_blocked' && (
          <Button onClick={handleClose} variant="contained" fullWidth>
            Close
          </Button>
        )}

        {/* CONTEXT step */}
        {step === 'context' && (
          <>
            <Button onClick={handleClose} variant="outlined" color="inherit">
              Cancel
            </Button>
            <Button
              onClick={handleGoToSlotSelect}
              variant="contained"
              aria-label="Select a new appointment slot"
            >
              Select New Slot
            </Button>
          </>
        )}

        {/* SLOT SELECT step */}
        {step === 'slot_select' && (
          <>
            <Button onClick={handleBackToContext} variant="outlined" color="inherit">
              Back
            </Button>
            <Button
              onClick={handleGoToConfirm}
              variant="contained"
              disabled={!selectedSlot}
              aria-label="Continue to confirm reschedule"
            >
              Continue
            </Button>
          </>
        )}

        {/* CONFIRM step */}
        {step === 'confirm' && (
          <>
            <Button
              onClick={handleBackToSlotSelect}
              variant="outlined"
              color="inherit"
              disabled={rescheduleMutation.isLoading}
            >
              Back
            </Button>
            <Button
              onClick={() => void handleConfirmReschedule()}
              variant="contained"
              color="primary"
              disabled={rescheduleMutation.isLoading}
              startIcon={
                rescheduleMutation.isLoading
                  ? <CircularProgress size={16} color="inherit" />
                  : undefined
              }
              aria-label="Confirm appointment reschedule"
            >
              {rescheduleMutation.isLoading ? 'Rescheduling…' : 'Confirm Reschedule'}
            </Button>
          </>
        )}

        {/* SUCCESS step */}
        {step === 'success' && (
          <Button
            onClick={handleClose}
            variant="contained"
            fullWidth
            aria-label="Close reschedule dialog"
          >
            Done
          </Button>
        )}

      </DialogActions>
    </Dialog>
  );
}
