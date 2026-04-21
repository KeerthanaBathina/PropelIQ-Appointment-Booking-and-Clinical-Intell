/**
 * WalkInRegistrationModal — Staff modal for walk-in patient booking (US_022).
 *
 * Two-path flow:
 *   1. Search existing patient records by name, DOB, or phone (AC-2).
 *   2. Inline new-patient creation when no record is selected (AC-2 fallback).
 *
 * Same-day slot selection (AC-3):
 *   - Shows only today's slots (same-day filter enforced on the displayed list).
 *   - "No same-day slots available" + next-available guidance when the list is
 *     empty (AC-4).
 *
 * Urgent priority capture (EC-2):
 *   - An "Urgent" toggle always visible; when slots are full and urgent is
 *     flagged the supervisor-escalation guidance panel is shown.
 *
 * Staff-only restriction (EC-1):
 *   - 403 responses from search or booking surface a clear restriction message.
 *
 * Accessibility (WCAG 2.1 AA / UXR-202):
 *   - Focus trapped inside dialog via MUI Dialog.
 *   - All interactive controls reachable by keyboard.
 *   - Skeleton placeholders for search and slot loading states (UXR-502).
 *
 * Responsive (UXR-301): Dialog maxWidth="md" — content adapts from 375 px up.
 *
 * Visual treatment (UXR-403): secondary (purple) accent for staff portal.
 */

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Collapse from '@mui/material/Collapse';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Divider from '@mui/material/Divider';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Skeleton from '@mui/material/Skeleton';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { useAppointmentSlots } from '@/hooks/useAppointmentSlots';
import type { PatientSearchResult } from '@/hooks/useWalkInPatientSearch';
import { useWalkInPatientSearch, type PatientSearchField } from '@/hooks/useWalkInPatientSearch';
import type { NewPatientData, WalkInBookingRequest } from '@/hooks/useWalkInRegistration';
import { useWalkInRegistration } from '@/hooks/useWalkInRegistration';
import WalkInPatientSearchResults from './WalkInPatientSearchResults';

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Returns today's ISO-8601 date string (YYYY-MM-DD) in local time. */
function todayIso(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function formatTime(time24: string): string {
  const [hStr, mStr] = time24.split(':');
  const h = parseInt(hStr, 10);
  const period = h >= 12 ? 'PM' : 'AM';
  const hour12 = h % 12 === 0 ? 12 : h % 12;
  return `${hour12}:${mStr ?? '00'} ${period}`;
}

/** Derive a human-readable error from ApiError or generic Error. */
function deriveErrorMessage(err: unknown): string {
  if (!err) return '';
  if (err instanceof Error) {
    // Surface staff-only restriction clearly (EC-1)
    if ((err as { status?: number }).status === 403) {
      return 'Access denied. Walk-in registration is restricted to staff members only.';
    }
    if ((err as { status?: number }).status === 409) {
      return 'No same-day slots are available for the selected provider. Please try a different provider or use the urgent escalation pathway.';
    }
    return err.message || 'An unexpected error occurred. Please try again.';
  }
  return 'An unexpected error occurred. Please try again.';
}

// ─── Slot empty-state component (AC-4) ───────────────────────────────────────

interface NoSlotsProps {
  nextAvailableDate: string | null;
  isUrgent: boolean;
}

function NoSameDaySlotsPanel({ nextAvailableDate, isUrgent }: NoSlotsProps) {
  return (
    <Box
      role="status"
      sx={{
        p: 2,
        border: '1px solid',
        borderColor: isUrgent ? 'error.light' : 'divider',
        borderRadius: 1,
        bgcolor: isUrgent ? 'error.50' : 'grey.50',
      }}
    >
      <Typography variant="subtitle2" color={isUrgent ? 'error.main' : 'text.primary'} gutterBottom>
        No same-day slots available
      </Typography>
      {nextAvailableDate && (
        <Typography variant="body2" color="text.secondary">
          Next available: <strong>{nextAvailableDate}</strong>
        </Typography>
      )}
      {isUrgent && (
        <Alert severity="error" sx={{ mt: 1.5 }} role="alert">
          <Typography variant="body2" component="span">
            <strong>Urgent escalation required.</strong> All same-day capacity is exhausted.
            Please contact the supervising clinician or charge nurse to authorise an
            over-capacity walk-in or redirect the patient to an urgent-care facility.
          </Typography>
        </Alert>
      )}
    </Box>
  );
}

// ─── Same-day slot selector ────────────────────────────────────────────────────

interface SlotSelectorProps {
  today: string;
  selectedSlotId: string | null;
  onSelect: (slotId: string) => void;
  onNextAvailable: (date: string | null) => void;
}

function SameDaySlotSelector({
  today,
  selectedSlotId,
  onSelect,
  onNextAvailable,
}: SlotSelectorProps) {
  const { data, isLoading, error } = useAppointmentSlots({
    startDate: today,
    endDate: today,
  });

  const todaySlots = (data?.slots ?? []).filter((s) => s.date === today && s.available);

  // Expose next available date to parent for AC-4 message
  useEffect(() => {
    if (!isLoading && todaySlots.length === 0 && data?.slots) {
      // Find first available slot beyond today
      const future = data.slots.find((s) => s.date > today && s.available);
      onNextAvailable(future?.date ?? null);
    } else {
      onNextAvailable(null);
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isLoading, todaySlots.length, data]);

  if (isLoading) {
    return (
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: 'repeat(2, 1fr)', sm: 'repeat(3, 1fr)' },
          gap: 1,
        }}
        role="status"
        aria-label="Loading same-day slots…"
      >
        {Array.from({ length: 6 }).map((_, i) => (
          <Skeleton key={i} variant="rectangular" height={40} sx={{ borderRadius: 1 }} />
        ))}
      </Box>
    );
  }

  if (error) {
    return (
      <Alert severity="error" role="alert">
        {deriveErrorMessage(error)}
      </Alert>
    );
  }

  if (todaySlots.length === 0) {
    // Rendered via parent — return null here, parent shows NoSameDaySlotsPanel
    return null;
  }

  return (
    <Box
      role="radiogroup"
      aria-label="Select a same-day slot"
      sx={{
        display: 'grid',
        gridTemplateColumns: { xs: 'repeat(2, 1fr)', sm: 'repeat(3, 1fr)' },
        gap: 1,
      }}
    >
      {todaySlots.map((slot) => {
        const isSelected = slot.slotId === selectedSlotId;
        return (
          <Box
            key={slot.slotId}
            role="radio"
            aria-checked={isSelected}
            tabIndex={isSelected ? 0 : -1}
            onClick={() => onSelect(slot.slotId)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                onSelect(slot.slotId);
              }
            }}
            sx={{
              p: 1,
              borderRadius: 1,
              border: '2px solid',
              borderColor: isSelected ? 'secondary.500' : 'divider',
              bgcolor: isSelected ? 'secondary.50' : 'background.paper',
              cursor: 'pointer',
              textAlign: 'center',
              userSelect: 'none',
              '&:hover': {
                borderColor: 'secondary.300',
                bgcolor: isSelected ? 'secondary.50' : 'action.hover',
              },
              '&:focus-visible': {
                outline: '2px solid',
                outlineColor: 'secondary.500',
                outlineOffset: 2,
              },
            }}
          >
            <Typography variant="body2" fontWeight={isSelected ? 600 : 400}>
              {formatTime(slot.startTime)}
            </Typography>
            <Typography variant="caption" color="text.secondary" display="block">
              {slot.providerName}
            </Typography>
            {isSelected && (
              <Chip
                label="Selected"
                size="small"
                color="secondary"
                sx={{ mt: 0.5, height: 18, fontSize: '0.65rem' }}
              />
            )}
          </Box>
        );
      })}
    </Box>
  );
}

// ─── Form state types ─────────────────────────────────────────────────────────

interface NewPatientForm {
  fullName: string;
  dateOfBirth: string;
  phone: string;
  email: string;
}

type ModalStep = 'search' | 'slots' | 'success';

// ─── Main modal ───────────────────────────────────────────────────────────────

export interface WalkInRegistrationModalProps {
  open: boolean;
  onClose: () => void;
}

export default function WalkInRegistrationModal({
  open,
  onClose,
}: WalkInRegistrationModalProps) {
  const today = todayIso();
  const titleId = useId();
  const searchInputRef = useRef<HTMLInputElement>(null);

  // ── Step state ────────────────────────────────────────────────────────────
  const [step, setStep] = useState<ModalStep>('search');

  // ── Patient search state ──────────────────────────────────────────────────
  const [searchTerm, setSearchTerm]       = useState('');
  const [searchField, setSearchField]     = useState<PatientSearchField>('name');
  const [selectedPatient, setSelectedPatient] = useState<PatientSearchResult | null>(null);
  const [useNewPatient, setUseNewPatient] = useState(false);
  const [newPatientForm, setNewPatientForm] = useState<NewPatientForm>({
    fullName: '', dateOfBirth: '', phone: '', email: '',
  });

  // ── Slot state ────────────────────────────────────────────────────────────
  const [selectedSlotId, setSelectedSlotId] = useState<string | null>(null);
  const [visitType, setVisitType]           = useState('General Checkup');
  const [isUrgent, setIsUrgent]             = useState(false);
  const [nextAvailableDate, setNextAvailableDate] = useState<string | null>(null);

  // ── Search query ──────────────────────────────────────────────────────────
  const {
    data: searchResults = [],
    isLoading: isSearching,
    error: searchError,
  } = useWalkInPatientSearch({ term: searchTerm, field: searchField });

  // ── Booking mutation ──────────────────────────────────────────────────────
  const {
    mutate: submitWalkIn,
    isPending: isSubmitting,
    error: submitError,
    reset: resetMutation,
  } = useWalkInRegistration();

  // ── Reset on open/close ───────────────────────────────────────────────────
  useEffect(() => {
    if (!open) {
      setStep('search');
      setSearchTerm('');
      setSelectedPatient(null);
      setUseNewPatient(false);
      setNewPatientForm({ fullName: '', dateOfBirth: '', phone: '', email: '' });
      setSelectedSlotId(null);
      setVisitType('General Checkup');
      setIsUrgent(false);
      setNextAvailableDate(null);
      resetMutation();
    }
  }, [open, resetMutation]);

  // ── Focus management: return to search input when step changes ─────────────
  useEffect(() => {
    if (open && step === 'search') {
      setTimeout(() => searchInputRef.current?.focus(), 50);
    }
  }, [open, step]);

  // ── Handlers ──────────────────────────────────────────────────────────────

  const handlePatientSelect = useCallback((patient: PatientSearchResult) => {
    setSelectedPatient(patient);
    setUseNewPatient(false);
  }, []);

  const handleSearchSubmit = useCallback(
    (e: React.FormEvent) => {
      e.preventDefault();
    },
    [],
  );

  const canProceedToSlots =
    (selectedPatient !== null && !useNewPatient) ||
    (useNewPatient &&
      newPatientForm.fullName.trim() !== '' &&
      newPatientForm.dateOfBirth !== '' &&
      newPatientForm.phone.trim() !== '' &&
      newPatientForm.email.trim() !== '');

  const handleProceedToSlots = useCallback(() => {
    setStep('slots');
  }, []);

  const handleBack = useCallback(() => {
    setStep('search');
    setSelectedSlotId(null);
  }, []);

  const handleConfirmBooking = useCallback(() => {
    if (!selectedSlotId) return;

    const req: WalkInBookingRequest = {
      patientId:  selectedPatient?.patientId ?? null,
      newPatient: useNewPatient
        ? {
            fullName:    newPatientForm.fullName.trim(),
            dateOfBirth: newPatientForm.dateOfBirth,
            phone:       newPatientForm.phone.trim(),
            email:       newPatientForm.email.trim(),
          } as NewPatientData
        : undefined,
      slotId:    selectedSlotId,
      visitType,
      isUrgent,
    };

    submitWalkIn(req, {
      onSuccess: () => {
        setStep('success');
      },
    });
  }, [selectedSlotId, selectedPatient, useNewPatient, newPatientForm, visitType, isUrgent, submitWalkIn]);

  const handleClose = useCallback(() => {
    if (isSubmitting) return;
    onClose();
  }, [isSubmitting, onClose]);

  // ── Error messages ────────────────────────────────────────────────────────

  const searchErrorMsg =
    searchError && (searchError as { status?: number }).status === 403
      ? 'Access denied. Patient search is restricted to staff members only.'
      : null;

  const submitErrorMsg = submitError ? deriveErrorMessage(submitError) : null;

  // ─────────────────────────────────────────────────────────────────────────
  // Render
  // ─────────────────────────────────────────────────────────────────────────

  return (
    <Dialog
      open={open}
      onClose={handleClose}
      maxWidth="md"
      fullWidth
      aria-labelledby={titleId}
      disableEscapeKeyDown={isSubmitting}
    >
      {/* ── Header ─────────────────────────────────────────────────────── */}
      <DialogTitle
        id={titleId}
        sx={{
          bgcolor: 'secondary.500',
          color: 'white',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <Typography variant="h6" component="span">
          Walk-in Registration
        </Typography>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Chip
            label={step === 'search' ? 'Step 1 of 2 — Patient' : step === 'slots' ? 'Step 2 of 2 — Slot' : 'Complete'}
            size="small"
            sx={{ bgcolor: 'secondary.300', color: 'white', fontWeight: 500 }}
          />
          <FormControlLabel
            control={
              <Switch
                checked={isUrgent}
                onChange={(e) => setIsUrgent(e.target.checked)}
                color="warning"
                size="small"
                inputProps={{ 'aria-label': 'Mark as urgent' }}
              />
            }
            label={
              <Typography variant="body2" sx={{ color: isUrgent ? 'warning.200' : 'grey.300' }}>
                Urgent
              </Typography>
            }
            sx={{ mr: 0 }}
          />
        </Box>
      </DialogTitle>

      <DialogContent dividers sx={{ pt: 2, minHeight: 300 }}>

        {/* ── STEP 1: Patient Search / New Patient ──────────────────────── */}
        {step === 'search' && (
          <Box>
            {searchErrorMsg && (
              <Alert severity="error" sx={{ mb: 2 }} role="alert">
                {searchErrorMsg}
              </Alert>
            )}

            {/* Search controls */}
            <Box
              component="form"
              onSubmit={handleSearchSubmit}
              sx={{ display: 'flex', gap: 1.5, mb: 2, flexWrap: 'wrap' }}
              aria-label="Patient search form"
            >
              <FormControl size="small" sx={{ minWidth: 110 }}>
                <InputLabel id="search-field-label">Search by</InputLabel>
                <Select
                  labelId="search-field-label"
                  label="Search by"
                  value={searchField}
                  onChange={(e) => setSearchField(e.target.value as PatientSearchField)}
                >
                  <MenuItem value="name">Name</MenuItem>
                  <MenuItem value="dob">Date of Birth</MenuItem>
                  <MenuItem value="phone">Phone</MenuItem>
                </Select>
              </FormControl>

              <TextField
                inputRef={searchInputRef}
                size="small"
                label={
                  searchField === 'name'
                    ? 'Search by name'
                    : searchField === 'dob'
                    ? 'Date of birth (YYYY-MM-DD)'
                    : 'Phone number'
                }
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                sx={{ flexGrow: 1, minWidth: 200 }}
                inputProps={{
                  'aria-label': 'Patient search term',
                  autoComplete: 'off',
                }}
              />
            </Box>

            {/* Search results */}
            <WalkInPatientSearchResults
              results={searchResults}
              selectedPatientId={selectedPatient?.patientId ?? null}
              onSelect={handlePatientSelect}
              loading={isSearching}
              searchTerm={searchTerm}
            />

            {/* Selected patient indicator */}
            {selectedPatient && !useNewPatient && (
              <Alert severity="success" sx={{ mt: 1.5 }}>
                Selected: <strong>{selectedPatient.fullName}</strong> — DOB:{' '}
                {selectedPatient.dateOfBirth} · {selectedPatient.phone}
              </Alert>
            )}

            {/* New patient toggle */}
            <Divider sx={{ my: 2 }} />
            <FormControlLabel
              control={
                <Switch
                  checked={useNewPatient}
                  onChange={(e) => {
                    setUseNewPatient(e.target.checked);
                    if (e.target.checked) setSelectedPatient(null);
                  }}
                  color="secondary"
                  inputProps={{ 'aria-label': 'Create new patient record' }}
                />
              }
              label={
                <Typography variant="body2">
                  New patient — no existing record found
                </Typography>
              }
            />

            {/* Inline new-patient form */}
            <Collapse in={useNewPatient}>
              <Box
                sx={{
                  display: 'grid',
                  gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' },
                  gap: 2,
                  mt: 2,
                }}
                aria-label="New patient details"
              >
                <TextField
                  label="Full name"
                  size="small"
                  required
                  value={newPatientForm.fullName}
                  onChange={(e) =>
                    setNewPatientForm((f) => ({ ...f, fullName: e.target.value }))
                  }
                  inputProps={{ 'aria-label': 'New patient full name' }}
                />
                <TextField
                  label="Date of birth"
                  size="small"
                  required
                  type="date"
                  value={newPatientForm.dateOfBirth}
                  onChange={(e) =>
                    setNewPatientForm((f) => ({ ...f, dateOfBirth: e.target.value }))
                  }
                  InputLabelProps={{ shrink: true }}
                  inputProps={{ 'aria-label': 'New patient date of birth' }}
                />
                <TextField
                  label="Phone"
                  size="small"
                  required
                  value={newPatientForm.phone}
                  onChange={(e) =>
                    setNewPatientForm((f) => ({ ...f, phone: e.target.value }))
                  }
                  inputProps={{ 'aria-label': 'New patient phone number' }}
                />
                <TextField
                  label="Email"
                  size="small"
                  required
                  type="email"
                  value={newPatientForm.email}
                  onChange={(e) =>
                    setNewPatientForm((f) => ({ ...f, email: e.target.value }))
                  }
                  inputProps={{ 'aria-label': 'New patient email address' }}
                />
              </Box>
            </Collapse>
          </Box>
        )}

        {/* ── STEP 2: Same-day Slot Selection ──────────────────────────── */}
        {step === 'slots' && (
          <Box>
            {/* Patient summary */}
            <Box
              sx={{
                p: 1.5,
                mb: 2,
                border: '1px solid',
                borderColor: 'secondary.200',
                borderRadius: 1,
                bgcolor: 'secondary.50',
              }}
            >
              <Typography variant="body2">
                <strong>Patient:</strong>{' '}
                {useNewPatient ? newPatientForm.fullName : (selectedPatient?.fullName ?? '—')}
              </Typography>
            </Box>

            {/* Visit type */}
            <FormControl size="small" fullWidth sx={{ mb: 2 }}>
              <InputLabel id="visit-type-label">Visit type</InputLabel>
              <Select
                labelId="visit-type-label"
                label="Visit type"
                value={visitType}
                onChange={(e) => setVisitType(e.target.value)}
              >
                {['General Checkup', 'Follow-up', 'Consultation', 'Urgent Care'].map((v) => (
                  <MenuItem key={v} value={v}>
                    {v}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            {/* Slot grid or no-slots panel */}
            <Typography variant="subtitle2" sx={{ mb: 1 }}>
              Same-day slots — {today}
            </Typography>

            {nextAvailableDate !== null ? (
              <NoSameDaySlotsPanel
                nextAvailableDate={nextAvailableDate}
                isUrgent={isUrgent}
              />
            ) : (
              <SameDaySlotSelector
                today={today}
                selectedSlotId={selectedSlotId}
                onSelect={setSelectedSlotId}
                onNextAvailable={setNextAvailableDate}
              />
            )}

            {/* Booking error */}
            {submitErrorMsg && (
              <Alert severity="error" sx={{ mt: 2 }} role="alert">
                {submitErrorMsg}
              </Alert>
            )}
          </Box>
        )}

        {/* ── SUCCESS ──────────────────────────────────────────────────── */}
        {step === 'success' && (
          <Box sx={{ textAlign: 'center', py: 4 }}>
            <Typography variant="h5" gutterBottom sx={{ color: 'secondary.500' }}>
              Walk-in Registered
            </Typography>
            <Typography variant="body1" color="text.secondary">
              The patient has been booked and added to the arrival queue.
            </Typography>
          </Box>
        )}
      </DialogContent>

      {/* ── Actions ──────────────────────────────────────────────────────── */}
      <DialogActions sx={{ px: 3, py: 2, gap: 1 }}>
        {step !== 'success' && (
          <Button
            onClick={handleClose}
            disabled={isSubmitting}
            variant="outlined"
            color="inherit"
          >
            Cancel
          </Button>
        )}

        {step === 'search' && (
          <Button
            variant="contained"
            color="secondary"
            disabled={!canProceedToSlots}
            onClick={handleProceedToSlots}
          >
            Select Slot
          </Button>
        )}

        {step === 'slots' && (
          <>
            <Button variant="text" onClick={handleBack} disabled={isSubmitting}>
              Back
            </Button>
            <Button
              variant="contained"
              color="secondary"
              disabled={!selectedSlotId || isSubmitting || nextAvailableDate !== null}
              onClick={handleConfirmBooking}
              startIcon={isSubmitting ? <CircularProgress size={16} color="inherit" /> : null}
              aria-label={isSubmitting ? 'Confirming booking…' : 'Confirm walk-in booking'}
            >
              {isSubmitting ? 'Booking…' : 'Confirm Booking'}
            </Button>
          </>
        )}

        {step === 'success' && (
          <Button variant="contained" color="secondary" onClick={handleClose}>
            Close
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
