/**
 * useRescheduleAppointment — React Query mutation for atomic appointment rescheduling (US_023).
 *
 * PUT /api/appointments/{appointmentId}/reschedule → RescheduleConfirmation (200 OK)
 *
 * Response outcomes handled:
 *   - 200 OK              : success — new appointment time confirmed (AC-1, AC-3)
 *   - 422 Unprocessable   : within-24-hour policy block (AC-2) — exact required message
 *   - 409 Conflict        : selected slot no longer available (EC-1)
 *   - 403 Forbidden       : walk-in restriction (EC-2) — backend-driven messaging
 *   - Other ApiError      : generic failure
 *
 * On success:
 *   - Invalidates 'patient-appointments' so the dashboard reflects the new time.
 *   - Invalidates 'appointmentSlots' so the booking page reflects released original slot.
 *
 * Timezone semantics:
 *   The `reschedulable` flag and the 24-hour eligibility evaluation are server-side.
 *   This hook NEVER re-evaluates time-window rules on the client.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPost, ApiError } from '@/lib/apiClient';
import type { AppointmentSlot } from './useAppointmentSlots';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface RescheduleRequest {
  /** UUID of the appointment being rescheduled. */
  appointmentId: string;
  /** Slot ID of the replacement slot. */
  slotId: string;
  /** Provider UUID for the new slot. */
  providerId: string;
  /** UTC appointment time of the new slot. */
  newAppointmentTime: string;
  /** Appointment type carried through from the original. */
  appointmentType: string;
}

/** Returned on 200 OK — contains both old and new times for the confirmation state (AC-3). */
export interface RescheduleConfirmation {
  /** UUID of the rescheduled appointment (same ID, updated record). */
  appointmentId: string;
  /** Booking reference (unchanged). */
  bookingReference: string;
  /** UTC ISO string of the old appointment time (for confirmation display). */
  oldAppointmentTime: string;
  /** UTC ISO string of the confirmed new appointment time. */
  newAppointmentTime: string;
  /** Provider display name. */
  providerName: string;
  /** Appointment type label. */
  appointmentType: string;
}

// ─── Outcome classification ────────────────────────────────────────────────────

export type RescheduleOutcome =
  | 'success'
  | 'policy_blocked'   // 422 — within 24 h of original (AC-2)
  | 'slot_unavailable' // 409 — selected slot taken (EC-1)
  | 'walkin_blocked'   // 403 — walk-in cannot be rescheduled (EC-2)
  | 'error';

export interface RescheduleError {
  outcome: Exclude<RescheduleOutcome, 'success'>;
  message: string;
  status: number;
  /** Alternative slots returned on 409, mirroring the BookingConflictDetail shape. */
  alternativeSlots?: AppointmentSlot[];
}

// ─── Message constants ────────────────────────────────────────────────────────

export const RESCHEDULE_MESSAGES = {
  POLICY_BLOCKED:     'Cannot reschedule within 24 hours of appointment.',
  SLOT_UNAVAILABLE:   'Slot no longer available. Please choose a different time.',
  WALKIN_BLOCKED:     'Walk-in appointments cannot be rescheduled by patients.',
  SUCCESS:            'Your appointment has been rescheduled.',
} as const;

// ─── API fn ───────────────────────────────────────────────────────────────────

async function rescheduleAppointment(
  req: RescheduleRequest,
): Promise<RescheduleConfirmation> {
  try {
    return await apiPost<RescheduleConfirmation>(
      `/api/appointments/${req.appointmentId}/reschedule`,
      {
        slotId:             req.slotId,
        providerId:         req.providerId,
        newAppointmentTime: req.newAppointmentTime,
        appointmentType:    req.appointmentType,
      },
    );
  } catch (err) {
    if (err instanceof ApiError) {
      if (err.status === 422) {
        const e: RescheduleError = {
          outcome:  'policy_blocked',
          message:  RESCHEDULE_MESSAGES.POLICY_BLOCKED,
          status:   err.status,
        };
        throw e;
      }
      if (err.status === 409) {
        let alternativeSlots: AppointmentSlot[] | undefined;
        try {
          const body = JSON.parse(err.message);
          alternativeSlots = body?.alternativeSlots;
        } catch {
          // body is not JSON — ignore
        }
        const e: RescheduleError = {
          outcome:          'slot_unavailable',
          message:          RESCHEDULE_MESSAGES.SLOT_UNAVAILABLE,
          status:           err.status,
          alternativeSlots,
        };
        throw e;
      }
      if (err.status === 403) {
        const e: RescheduleError = {
          outcome: 'walkin_blocked',
          message: RESCHEDULE_MESSAGES.WALKIN_BLOCKED,
          status:  err.status,
        };
        throw e;
      }
      const e: RescheduleError = {
        outcome: 'error',
        message: err.message || 'An unexpected error occurred. Please try again.',
        status:  err.status,
      };
      throw e;
    }
    const e: RescheduleError = {
      outcome: 'error',
      message: 'An unexpected error occurred. Please try again.',
      status:  0,
    };
    throw e;
  }
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useRescheduleAppointment() {
  const queryClient = useQueryClient();

  return useMutation<RescheduleConfirmation, RescheduleError, RescheduleRequest>({
    mutationFn: rescheduleAppointment,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['patient-appointments'] });
      void queryClient.invalidateQueries({ queryKey: ['appointmentSlots'] });
    },
  });
}
