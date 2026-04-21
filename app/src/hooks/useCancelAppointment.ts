/**
 * useCancelAppointment — React Query mutation for appointment cancellation (US_019).
 *
 * DELETE /api/appointments/{id} → 204 No Content on success.
 *
 * Response shapes handled:
 *   - 204 No Content : success (AC-1)
 *   - 422 Unprocessable Entity : within-24-hour policy block (AC-2)
 *   - 409 Conflict   : appointment already cancelled (EC-1)
 *   - 503            : transient service failure — surfaced to caller as retryable error
 *
 * Timezone semantics (EC-2):
 *   The eligibility `cancellable` flag comes from the backend (UTC-evaluated).
 *   This hook NEVER re-computes the 24-hour rule on the client.
 *
 * Usage:
 *   const cancel = useCancelAppointment();
 *   await cancel.mutateAsync({ appointmentId: 'uuid' });
 *   // check cancel.error for CancellationError shape
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiDelete, ApiError } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

export type CancellationOutcome = 'success' | 'policy_blocked' | 'already_cancelled' | 'error';

export interface CancellationError {
  outcome: Exclude<CancellationOutcome, 'success'>;
  message: string;
  status: number;
}

interface CancelVariables {
  appointmentId: string;
}

// ─── Message constants (AC-2, EC-1) ──────────────────────────────────────────

export const CANCELLATION_MESSAGES = {
  POLICY_BLOCKED:
    'Cancellations within 24 hours are not permitted. Please contact the clinic.',
  ALREADY_CANCELLED: 'This appointment has already been cancelled.',
  SUCCESS: 'Your appointment has been successfully cancelled.',
} as const;

// ─── API call ─────────────────────────────────────────────────────────────────

async function cancelAppointment({ appointmentId }: CancelVariables): Promise<void> {
  try {
    await apiDelete<void>(`/api/appointments/${appointmentId}`);
  } catch (err) {
    if (err instanceof ApiError) {
      // Map HTTP status → typed cancellation error (AC-2, EC-1)
      if (err.status === 422) {
        const cancellationError: CancellationError = {
          outcome: 'policy_blocked',
          message: CANCELLATION_MESSAGES.POLICY_BLOCKED,
          status: err.status,
        };
        throw cancellationError;
      }
      if (err.status === 409) {
        const cancellationError: CancellationError = {
          outcome: 'already_cancelled',
          message: CANCELLATION_MESSAGES.ALREADY_CANCELLED,
          status: err.status,
        };
        throw cancellationError;
      }
      // Other HTTP errors (503, 500, etc.)
      const cancellationError: CancellationError = {
        outcome: 'error',
        message: err.message || 'An unexpected error occurred. Please try again.',
        status: err.status,
      };
      throw cancellationError;
    }
    // Non-ApiError (network failure, etc.)
    const cancellationError: CancellationError = {
      outcome: 'error',
      message: 'Unable to reach the server. Please check your connection and try again.',
      status: 0,
    };
    throw cancellationError;
  }
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useCancelAppointment() {
  const queryClient = useQueryClient();

  return useMutation<void, CancellationError, CancelVariables>({
    mutationFn: cancelAppointment,
    onSuccess: () => {
      // Invalidate appointment queries so dashboard and history refresh (AC-1)
      void queryClient.invalidateQueries({ queryKey: ['patient-appointments'] });
    },
  });
}
