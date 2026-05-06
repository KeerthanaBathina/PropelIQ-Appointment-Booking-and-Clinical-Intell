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
  providerId: string;
  appointmentTime: string; // UTC ISO-8601
  appointmentType: string;
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

// Raw shape returned by the API (field names differ from BookingConfirmation)
interface RawBookingResponse {
  appointmentId: string;
  bookingReference: string;
  appointmentDate: string;  // API uses appointmentDate, not date
  appointmentTime: string;  // API uses appointmentTime, not startTime
  providerName: string;
  appointmentType: string;
}

/** Add 30 minutes to a HH:mm string, return HH:mm. */
function addThirtyMinutes(time24: string): string {
  const [h, m] = time24.split(':').map(Number);
  const total = (h ?? 0) * 60 + (m ?? 0) + 30;
  return `${String(Math.floor(total / 60) % 24).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
}

async function bookAppointment(request: BookingRequest): Promise<BookingConfirmation> {
  let attempt = 0;
  // eslint-disable-next-line no-constant-condition
  while (true) {
    try {
      const raw = await apiPost<RawBookingResponse>('/api/appointments', request);
      return {
        bookingReference: raw.bookingReference,
        appointmentId:   raw.appointmentId,
        date:            raw.appointmentDate,
        startTime:       raw.appointmentTime,
        endTime:         addThirtyMinutes(raw.appointmentTime),
        providerName:    raw.providerName,
        appointmentType: raw.appointmentType,
      };
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
