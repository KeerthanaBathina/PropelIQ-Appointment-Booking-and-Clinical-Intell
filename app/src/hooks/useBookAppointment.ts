/**
 * useBookAppointment — React Query mutation for confirming appointment bookings (US_018).
 *
 * POST /api/appointments → BookingConfirmation (201 Created)
 *
 * Retry policy (EC-1): on 503 Service Unavailable, retries once automatically
 * before surfacing the error to the caller. All other HTTP errors (including 409
 * Conflict) are surfaced on the first attempt.
 *
 * Error shapes:
 *   - ApiError(409): slot conflict — message body contains BookingConflictDetail JSON
 *   - ApiError(503): service unavailable — shown with retry prompt (EC-1)
 *   - Other ApiError: generic failure
 */

import { useMutation } from '@tanstack/react-query';
import { apiPost, ApiError } from '@/lib/apiClient';
import type { AppointmentSlot } from './useAppointmentSlots';

// ─── DTOs ─────────────────────────────────────────────────────────────────────

export interface BookingRequest {
  slotId: string;
  visitType: string;
}

/** Returned by POST /api/appointments on 201 Created (AC-4). */
export interface BookingConfirmation {
  bookingReference: string;
  appointmentId: string;
  date: string;
  startTime: string;
  endTime: string;
  providerName: string;
  appointmentType: string;
}

/** Shape of the 409 Conflict response body. */
export interface BookingConflictDetail {
  message: string;
  alternativeSlots: AppointmentSlot[];
}

// ─── Constants ────────────────────────────────────────────────────────────────

const MAX_503_RETRIES = 1; // EC-1: retry once on service unavailable

// ─── Mutation fn ─────────────────────────────────────────────────────────────

async function bookAppointment(request: BookingRequest): Promise<BookingConfirmation> {
  let attempt = 0;
  // eslint-disable-next-line no-constant-condition
  while (true) {
    try {
      return await apiPost<BookingConfirmation>('/api/appointments', request);
    } catch (err) {
      if (err instanceof ApiError && err.status === 503 && attempt < MAX_503_RETRIES) {
        attempt++;
        continue;
      }
      throw err;
    }
  }
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useBookAppointment() {
  return useMutation<BookingConfirmation, ApiError, BookingRequest>({
    mutationFn: bookAppointment,
  });
}
