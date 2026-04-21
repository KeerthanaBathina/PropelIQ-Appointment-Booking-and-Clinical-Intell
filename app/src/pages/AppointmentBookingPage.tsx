/**
 * AppointmentBookingPage — SCR-006 Appointment Booking Confirmation Flow (US_018, US_020).
 *
 * Extends the slot-viewing page (US_017) with the full booking confirmation flow:
 *
 *   1. Slot selection → immediate "Reserved" hold badge (UXR-503 optimistic UI)
 *      + POST /api/appointments/hold (60-second countdown timer).
 *   2. "Confirm Booking" button → BookingConfirmationModal (UXR-102) with countdown.
 *   3. Confirm click → POST /api/appointments (useBookAppointment, retry on 503 – EC-1).
 *   4. 201 Created → BookingSuccessView with reference number (AC-4).
 *   5. 409 Conflict → SlotConflictModal with 3 alternative slots (UXR-602, AC-2).
 *   6. Hold expiry (60 s) → modal closes, Snackbar "Hold expired" toast (AC-3).
 *   7. 503 / generic error → Alert inside BookingConfirmationModal (EC-1).
 *
 * US_020 Waitlist flow:
 *   - When no slots are available: "Join Waitlist" CTA in TimeSlotGrid empty state (AC-1).
 *   - ?claim=TOKEN in URL: validates claim token, pre-selects slot, opens confirm modal
 *     with 1-minute countdown (AC-3); shows within-24h notice when applicable (EC-2).
 *
 * Responsive: 2-col layout on md+, single-col on xs/sm (matches wireframe breakpoint).
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link as RouterLink, useSearchParams } from 'react-router-dom';
import Alert from '@mui/material/Alert';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Container from '@mui/material/Container';
import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import Link from '@mui/material/Link';
import MenuItem from '@mui/material/MenuItem';
import Select, { type SelectChangeEvent } from '@mui/material/Select';
import Snackbar from '@mui/material/Snackbar';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import {
  useAppointmentSlots,
  getAvailableDates,
  getSlotsForDate,
  type AppointmentSlot,
} from '@/hooks/useAppointmentSlots';
import { useBookAppointment, type BookingConflictDetail } from '@/hooks/useBookAppointment';
import { useSlotHold } from '@/hooks/useSlotHold';
import { useJoinWaitlist, type WaitlistRequest } from '@/hooks/useJoinWaitlist';
import { useWaitlistOfferClaim } from '@/hooks/useWaitlistOfferClaim';
import BookingConfirmationModal from '@/components/appointments/BookingConfirmationModal';
import BookingSuccessView from '@/components/appointments/BookingSuccessView';
import JoinWaitlistDialog from '@/components/appointments/JoinWaitlistDialog';
import ProviderFilter from '@/components/appointments/ProviderFilter';
import SlotCalendar from '@/components/appointments/SlotCalendar';
import SlotConflictModal from '@/components/appointments/SlotConflictModal';
import TimeSlotGrid from '@/components/appointments/TimeSlotGrid';
import WaitlistConfirmationNotice, {
  type WaitlistCriteria,
} from '@/components/appointments/WaitlistConfirmationNotice';
import { ApiError } from '@/lib/apiClient';

// ─── Helpers ──────────────────────────────────────────────────────────────────

const VISIT_TYPES = ['General Checkup', 'Follow-up', 'Consultation'];
const MAX_BOOKING_DAYS = 90;

type BookingStep = 'idle' | 'confirming' | 'success' | 'conflict';

function todayStr(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function maxDateStr(): string {
  const d = new Date();
  d.setDate(d.getDate() + MAX_BOOKING_DAYS);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Seconds remaining on a hold that was acquired at the given UTC ISO timestamp. */
function computeClaimSecondsRemaining(holdAcquiredAt: string): number {
  const HOLD_DURATION_SECONDS = 60;
  const elapsed = Math.floor((Date.now() - new Date(holdAcquiredAt).getTime()) / 1000);
  return Math.max(0, HOLD_DURATION_SECONDS - elapsed);
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function AppointmentBookingPage() {
  const today = useMemo(todayStr, []);
  const maxDate = useMemo(maxDateStr, []);

  // ─── Filter state ─────────────────────────────────────────────────────────
  const [selectedProvider, setSelectedProvider] = useState('');
  const [selectedVisitType, setSelectedVisitType] = useState(VISIT_TYPES[0]);

  // ─── Calendar + slot selection state ─────────────────────────────────────
  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const [selectedSlotId, setSelectedSlotId] = useState<string | null>(null);

  // ─── Booking flow state ───────────────────────────────────────────────────
  const [bookingStep, setBookingStep] = useState<BookingStep>('idle');
  const [conflictAlternatives, setConflictAlternatives] = useState<AppointmentSlot[]>([]);
  const [holdExpiredToast, setHoldExpiredToast] = useState(false);

  // ─── US_020: Waitlist state ───────────────────────────────────────────────
  const [waitlistDialogOpen, setWaitlistDialogOpen] = useState(false);
  const [waitlistConfirmed, setWaitlistConfirmed] = useState(false);
  const [waitlistCriteria, setWaitlistCriteria] = useState<WaitlistCriteria | null>(null);
  /** True when the current booking originates from a waitlist claim link (AC-3). */
  const [isWaitlistOffer, setIsWaitlistOffer] = useState(false);
  /** True when the waitlist offer slot is within 24 hours of now (EC-2). */
  const [offerWithin24Hours, setOfferWithin24Hours] = useState(false);
  /** Countdown seconds for claim-based holds (backend already holds the slot). */
  const [claimSecondsRemaining, setClaimSecondsRemaining] = useState(0);
  const claimCountdownRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // ─── Hooks ────────────────────────────────────────────────────────────────
  const [searchParams] = useSearchParams();
  const claimToken = searchParams.get('claim');

  const slotHold = useSlotHold();
  const bookMutation = useBookAppointment();
  const joinWaitlistMutation = useJoinWaitlist();
  const {
    data: claimOffer,
    isLoading: isClaimLoading,
    isError: isClaimError,
    error: claimError,
  } = useWaitlistOfferClaim(claimToken);

  // ─── Data fetch ───────────────────────────────────────────────────────────
  const { data, isLoading, isError, error } = useAppointmentSlots({
    startDate: today,
    endDate: maxDate,
    providerId: selectedProvider || undefined,
  });

  const availableDates = useMemo(
    () => getAvailableDates(data?.slots ?? []),
    [data?.slots],
  );

  const slotsForDate = useMemo(
    () => (selectedDate ? getSlotsForDate(data?.slots ?? [], selectedDate) : []),
    [data?.slots, selectedDate],
  );

  const selectedSlot = useMemo(
    () => slotsForDate.find((s) => s.slotId === selectedSlotId) ?? null,
    [slotsForDate, selectedSlotId],
  );

  // ─── Hold expiry reaction ─────────────────────────────────────────────────
  // When hold countdown reaches zero: close confirmation modal, show toast (AC-3)
  useEffect(() => {
    if (slotHold.holdStatus === 'expired') {
      setBookingStep((prev) => (prev === 'confirming' ? 'idle' : prev));
      setSelectedSlotId(null);
      setHoldExpiredToast(true);
      slotHold.clearExpiredNotification();
    }
  }, [slotHold.holdStatus, slotHold.clearExpiredNotification]);

  // ─── US_020 AC-3: Claim offer reaction ────────────────────────────────────
  // When a valid claim offer loads, pre-select the slot and open the confirm modal.
  // The backend already holds the slot; start a local countdown from holdAcquiredAt.
  useEffect(() => {
    if (!claimOffer) return;

    const { slot, isWithin24Hours, holdAcquiredAt } = claimOffer;

    setSelectedDate(slot.date);
    setSelectedProvider(slot.providerId);
    setSelectedSlotId(slot.slotId);
    setIsWaitlistOffer(true);
    setOfferWithin24Hours(isWithin24Hours);
    setBookingStep('confirming');

    // Start local countdown from remaining seconds on the backend-held slot
    const initialSeconds = computeClaimSecondsRemaining(holdAcquiredAt);
    setClaimSecondsRemaining(initialSeconds);

    if (claimCountdownRef.current !== null) {
      clearInterval(claimCountdownRef.current);
    }
    claimCountdownRef.current = setInterval(() => {
      setClaimSecondsRemaining((prev) => {
        const next = prev - 1;
        if (next <= 0) {
          if (claimCountdownRef.current !== null) {
            clearInterval(claimCountdownRef.current);
            claimCountdownRef.current = null;
          }
          // Offer expired — close modal, show toast
          setBookingStep('idle');
          setSelectedSlotId(null);
          setIsWaitlistOffer(false);
          setHoldExpiredToast(true);
          return 0;
        }
        return next;
      });
    }, 1000);
  }, [claimOffer]);

  // Cleanup claim countdown on unmount
  useEffect(() => {
    return () => {
      if (claimCountdownRef.current !== null) {
        clearInterval(claimCountdownRef.current);
      }
    };
  }, []);

  // ─── Handlers ─────────────────────────────────────────────────────────────

  const handleDateSelect = useCallback((date: string) => {
    setSelectedDate(date);
    setSelectedSlotId(null);
    void slotHold.releaseHold();
    bookMutation.reset();
  }, [slotHold, bookMutation]);

  const handleTryDifferentDate = useCallback(() => {
    setSelectedDate(null);
    setSelectedSlotId(null);
    void slotHold.releaseHold();
    bookMutation.reset();
  }, [slotHold, bookMutation]);

  const handleProviderChange = useCallback((providerId: string) => {
    setSelectedProvider(providerId);
    setSelectedSlotId(null);
    void slotHold.releaseHold();
    bookMutation.reset();
  }, [slotHold, bookMutation]);

  const handleVisitTypeChange = useCallback((e: SelectChangeEvent<string>) => {
    setSelectedVisitType(e.target.value);
  }, []);

  // ─── US_020 AC-1: Waitlist handlers ──────────────────────────────────────

  const handleJoinWaitlist = useCallback(() => {
    setWaitlistDialogOpen(true);
    joinWaitlistMutation.reset();
  }, [joinWaitlistMutation]);

  const handleWaitlistDialogClose = useCallback(() => {
    if (joinWaitlistMutation.isLoading) return;
    setWaitlistDialogOpen(false);
    joinWaitlistMutation.reset();
  }, [joinWaitlistMutation]);

  const handleWaitlistSubmit = useCallback(() => {
    if (!selectedDate) return;

    const request: WaitlistRequest = {
      preferredDate: selectedDate,
      preferredTimeStart: '08:00',
      preferredTimeEnd: '17:00',
      preferredProviderId: selectedProvider || null,
      preferredProviderName:
        data?.providers.find((p) => p.providerId === selectedProvider)?.providerName ?? null,
      appointmentType: selectedVisitType,
    };

    joinWaitlistMutation.mutate(request, {
      onSuccess: (registration) => {
        setWaitlistDialogOpen(false);
        setWaitlistConfirmed(true);
        setWaitlistCriteria({
          preferredDate: registration.preferredDate,
          preferredTimeStart: registration.preferredTimeStart,
          preferredTimeEnd: registration.preferredTimeEnd,
          providerName: registration.preferredProviderName,
          visitType: registration.appointmentType,
        });
        joinWaitlistMutation.reset();
      },
    });
  }, [selectedDate, selectedProvider, selectedVisitType, data?.providers, joinWaitlistMutation]);

  // UXR-503: slot selection triggers optimistic hold + visual "Reserved" badge
  const handleSlotSelect = useCallback(
    (slotId: string) => {
      if (slotId === slotHold.heldSlotId) return; // same slot re-clicked, no-op

      if (slotHold.heldSlotId) {
        void slotHold.releaseHold();
      }

      setSelectedSlotId(slotId);
      bookMutation.reset();

      void slotHold.startHold(slotId).then((success) => {
        if (!success) setSelectedSlotId(null); // rollback on hold failure
      });
    },
    [slotHold, bookMutation],
  );

  const handleConfirmClick = useCallback(() => {
    if (!selectedSlotId) return;
    bookMutation.reset();
    setBookingStep('confirming');
  }, [selectedSlotId, bookMutation]);

  const handleModalClose = useCallback(() => {
    if (bookMutation.isLoading) return;
    setBookingStep('idle');
    void slotHold.releaseHold();
    setSelectedSlotId(null);
    // US_020: reset waitlist offer context on modal close
    if (isWaitlistOffer) {
      setIsWaitlistOffer(false);
      setOfferWithin24Hours(false);
      if (claimCountdownRef.current !== null) {
        clearInterval(claimCountdownRef.current);
        claimCountdownRef.current = null;
      }
      setClaimSecondsRemaining(0);
    }
    bookMutation.reset();
  }, [bookMutation, slotHold, isWaitlistOffer]);

  const handleBookingConfirm = useCallback(() => {
    if (!selectedSlotId) return;

    bookMutation.mutate(
      { slotId: selectedSlotId, visitType: selectedVisitType },
      {
        onSuccess: () => {
          void slotHold.releaseHold();
          setBookingStep('success');
        },
        onError: (err: ApiError) => {
          if (err.status === 409) {
            let alternatives: AppointmentSlot[] = [];
            try {
              const body = JSON.parse(err.message) as BookingConflictDetail;
              alternatives = body.alternativeSlots ?? [];
            } catch {
              // body not parseable — show empty alternatives
            }
            setConflictAlternatives(alternatives);
            setBookingStep('conflict');
            setSelectedSlotId(null);
            void slotHold.releaseHold();
          }
          // 503 / generic errors: stay in 'confirming', error shown in modal (EC-1)
        },
      },
    );
  }, [selectedSlotId, selectedVisitType, bookMutation, slotHold]);

  // User picks an alternative slot from the conflict modal (UXR-602)
  const handlePickAlternative = useCallback(
    (slot: AppointmentSlot) => {
      setSelectedDate(slot.date);
      setSelectedSlotId(slot.slotId);
      setConflictAlternatives([]);
      setBookingStep('idle');
      void slotHold.startHold(slot.slotId).then((success) => {
        if (!success) setSelectedSlotId(null);
      });
    },
    [slotHold],
  );

  const handleConflictClose = useCallback(() => {
    setBookingStep('idle');
    setConflictAlternatives([]);
  }, []);

  // Derive error message for the confirmation modal
  const bookingErrorMessage = useMemo(() => {
    if (!bookMutation.error) return null;
    if (bookMutation.error.status === 503) {
      return 'Service temporarily unavailable. Please try again.';
    }
    if (bookMutation.error.status === 409) return null; // handled by conflict modal
    return 'Booking failed. Please try again.';
  }, [bookMutation.error]);

  // ─── Render ───────────────────────────────────────────────────────────────

  return (
    <Box sx={{ flexGrow: 1 }}>
      {/* Header */}
      <AppBar position="static" sx={{ bgcolor: 'primary.main' }}>
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            UPACIP — Patient Portal
          </Typography>
        </Toolbar>
      </AppBar>

      <Container maxWidth="lg" sx={{ mt: 3, mb: 6 }}>
        {/* Breadcrumb */}
        <Breadcrumbs aria-label="breadcrumb" sx={{ mb: 3 }}>
          <Link
            component={RouterLink}
            to="/patient/dashboard"
            underline="hover"
            color="text.secondary"
          >
            Dashboard
          </Link>
          <Typography color="text.primary">Book Appointment</Typography>
        </Breadcrumbs>

        <Typography variant="h5" component="h1" gutterBottom sx={{ fontWeight: 600 }}>
          Book Appointment
        </Typography>

        {/* API-level error banner */}
        {isError && (
          <Alert severity="error" sx={{ mb: 3 }}>
            {(error as ApiError)?.message ?? 'Failed to load available slots. Please refresh.'}
          </Alert>
        )}

        {/* US_020 AC-3: claim token error banner */}
        {isClaimError && claimError && (
          <Alert severity="error" sx={{ mb: 3 }}>
            {claimError.message}
          </Alert>
        )}

        {/* US_020 AC-3: loading indicator while claim is being validated */}
        {isClaimLoading && (
          <Alert severity="info" sx={{ mb: 3 }}>
            Loading your waitlist offer…
          </Alert>
        )}

        {/* US_020 AC-1: confirmation notice after successful waitlist registration (EC-1) */}
        {waitlistConfirmed && waitlistCriteria && (
          <WaitlistConfirmationNotice
            criteria={waitlistCriteria}
            onDismiss={() => setWaitlistConfirmed(false)}
          />
        )}

        {bookingStep === 'success' && bookMutation.data ? (
          // ── Success view replaces the booking form (AC-4) ─────────────────
          <BookingSuccessView {...bookMutation.data} />
        ) : (
          <>
            {/* ── Filter row ─────────────────────────────────────────────── */}
            <Card sx={{ mb: 3 }}>
              <CardContent>
                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 2, alignItems: 'center' }}>
                  <ProviderFilter
                    providers={data?.providers ?? []}
                    value={selectedProvider}
                    onChange={handleProviderChange}
                    disabled={isLoading}
                  />
                  <FormControl size="small" sx={{ minWidth: 200 }}>
                    <InputLabel id="visit-type-label">Visit Type</InputLabel>
                    <Select
                      labelId="visit-type-label"
                      id="visit-type"
                      value={selectedVisitType}
                      label="Visit Type"
                      onChange={handleVisitTypeChange}
                      inputProps={{ 'aria-label': 'Select visit type' }}
                    >
                      {VISIT_TYPES.map((vt) => (
                        <MenuItem key={vt} value={vt}>
                          {vt}
                        </MenuItem>
                      ))}
                    </Select>
                  </FormControl>
                </Box>
              </CardContent>
            </Card>

            {/* ── Two-column booking layout ─────────────────────────────── */}
            <Box
              sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' },
                gap: 3,
              }}
            >
              {/* Left — Calendar */}
              <Card>
                <CardContent>
                  <SlotCalendar
                    selectedDate={selectedDate}
                    onDateSelect={handleDateSelect}
                    availableDates={availableDates}
                    loading={isLoading}
                  />
                </CardContent>
              </Card>

              {/* Right — Time slot grid + action buttons */}
              <Card>
                <CardContent>
                  <TimeSlotGrid
                    slots={slotsForDate}
                    selectedSlotId={selectedSlotId}
                    heldSlotId={slotHold.heldSlotId}
                    onSlotSelect={handleSlotSelect}
                    onTryDifferentDate={handleTryDifferentDate}
                    onJoinWaitlist={selectedDate ? handleJoinWaitlist : undefined}
                    loading={isLoading}
                    selectedDate={selectedDate}
                  />

                  {/* UXR-503: inline error when hold acquisition fails */}
                  {slotHold.holdStatus === 'error' && slotHold.holdError && (
                    <Alert severity="warning" sx={{ mt: 2 }}>
                      {slotHold.holdError}
                    </Alert>
                  )}

                  {/* Action buttons — shown when slot is held */}
                  {slotHold.heldSlotId && (
                    <Box sx={{ display: 'flex', gap: 1.5, mt: 3 }}>
                      <Button
                        id="confirm-booking"
                        variant="contained"
                        onClick={handleConfirmClick}
                      >
                        Confirm Booking
                      </Button>
                      <Button variant="outlined" onClick={handleModalClose}>
                        Cancel
                      </Button>
                    </Box>
                  )}
                </CardContent>
              </Card>
            </Box>
          </>
        )}
      </Container>

      {/* ── Booking Confirmation Modal (UXR-102) ──────────────────────────── */}
      <BookingConfirmationModal
        open={bookingStep === 'confirming'}
        slot={selectedSlot}
        visitType={selectedVisitType}
        secondsRemaining={isWaitlistOffer ? claimSecondsRemaining : slotHold.secondsRemaining}
        onConfirm={handleBookingConfirm}
        onCancel={handleModalClose}
        isLoading={bookMutation.isLoading}
        bookingError={bookingErrorMessage}
        isWaitlistOffer={isWaitlistOffer}
        offerWithin24Hours={offerWithin24Hours}
      />

      {/* ── Slot Conflict Modal (UXR-602) ──────────────────────────────────── */}
      <SlotConflictModal
        open={bookingStep === 'conflict'}
        alternatives={conflictAlternatives}
        onSelectAlternative={handlePickAlternative}
        onClose={handleConflictClose}
      />

      {/* ── Hold expired toast (AC-3) ──────────────────────────────────────── */}
      <Snackbar
        open={holdExpiredToast}
        autoHideDuration={6000}
        onClose={() => setHoldExpiredToast(false)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert
          severity="warning"
          onClose={() => setHoldExpiredToast(false)}
          role="status"
          aria-live="polite"
        >
          Hold expired — slot released. Please select another time.
        </Alert>
      </Snackbar>
      {/* ── US_020 AC-1: Join Waitlist Dialog ───────────────────────── */}
      <JoinWaitlistDialog
        open={waitlistDialogOpen}
        onClose={handleWaitlistDialogClose}
        onConfirm={handleWaitlistSubmit}
        isLoading={joinWaitlistMutation.isLoading}
        errorKind={joinWaitlistMutation.error?.kind ?? null}
        errorMessage={joinWaitlistMutation.error?.message ?? null}
        preferredDate={selectedDate ?? ''}
        preferredTimeStart="08:00"
        preferredTimeEnd="17:00"
        visitType={selectedVisitType}
        providerName={
          data?.providers.find((p) => p.providerId === selectedProvider)?.providerName ?? null
        }
      />    </Box>
  );
}
