/**
 * useWalkInRegistration — React Query mutation for staff walk-in booking (US_022 AC-3).
 *
 * POST /api/staff/walkin → WalkInBookingConfirmation (201 Created)
 *
 * Error shapes:
 *   - ApiError(400): validation failure (invalid date, missing fields).
 *   - ApiError(403): caller is not staff — EC-1 staff-only restriction.
 *   - ApiError(409): slot already taken (no same-day slots left).
 *   - Other ApiError: generic failure.
 *
 * On success the hook invalidates:
 *   - 'walkInPatientSearch' — so stale search results do not show ghost patients.
 *   - 'staffQueue'          — so the staff dashboard queue count refreshes (AC-3).
 *   - 'appointmentSlots'    — so the slot grid reflects the newly booked slot.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPost, ApiError } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

/** Walk-in booking request payload (AC-3). */
export interface WalkInBookingRequest {
  /**
   * UUID of an existing patient record, or null when staff is creating a new
   * patient inline.  When null, newPatient must be provided.
   */
  patientId: string | null;
  /**
   * Inline new-patient data when no existing record is found (AC-2 fallback).
   * Required when patientId is null.
   */
  newPatient?: NewPatientData;
  /** Slot ID selected by staff from the same-day slot grid. */
  slotId: string;
  /** Visit-type label (e.g. "General Checkup"). */
  visitType: string;
  /** True when staff marks this as urgent (EC-2). */
  isUrgent: boolean;
}

/** Minimal new-patient data for inline walk-in creation. */
export interface NewPatientData {
  fullName: string;
  /** ISO-8601 (YYYY-MM-DD). */
  dateOfBirth: string;
  phone: string;
  email: string;
}

/** Response returned on successful walk-in booking (201). */
export interface WalkInBookingConfirmation {
  /** Booking reference code. */
  bookingReference: string;
  /** UUID of the created appointment. */
  appointmentId: string;
  /** Patient UUID (new or existing). */
  patientId: string;
  /** Patient full name. */
  patientName: string;
  /** ISO-8601 date. */
  date: string;
  /** HH:MM start time. */
  startTime: string;
  /** HH:MM end time. */
  endTime: string;
  /** Provider full name. */
  providerName: string;
  /** Visit type label. */
  appointmentType: string;
  /** True if the appointment was tagged urgent. */
  isUrgent: boolean;
  /** True — walk-in appointments always carry this flag. */
  isWalkIn: boolean;
}

// ─── Mutation fn ─────────────────────────────────────────────────────────────

async function submitWalkIn(
  request: WalkInBookingRequest,
): Promise<WalkInBookingConfirmation> {
  return apiPost<WalkInBookingConfirmation>('/api/staff/walkin', request);
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useWalkInRegistration() {
  const queryClient = useQueryClient();

  return useMutation<WalkInBookingConfirmation, ApiError, WalkInBookingRequest>({
    mutationFn: submitWalkIn,
    onSuccess: () => {
      // Refresh queue and slot availability so staff dashboard shows updated state.
      void queryClient.invalidateQueries({ queryKey: ['staffQueue'] });
      void queryClient.invalidateQueries({ queryKey: ['appointmentSlots'] });
      void queryClient.invalidateQueries({ queryKey: ['walkInPatientSearch'] });
    },
  });
}
