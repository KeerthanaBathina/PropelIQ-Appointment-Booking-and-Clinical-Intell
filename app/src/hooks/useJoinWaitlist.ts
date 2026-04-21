/**
 * useJoinWaitlist — React Query mutation for waitlist registration (US_020, AC-1).
 *
 * POST /api/waitlist → WaitlistRegistration (201 Created)
 *
 * Error shapes:
 *   - ApiError(409): patient is already on the waitlist for matching criteria.
 *   - ApiError(400): validation failure (invalid date / provider).
 *   - Other ApiError: generic failure.
 *
 * The backend task (task_002_be_waitlist_registration_orchestration) owns the endpoint.
 * The frontend defines the request/response shapes here so the UI can be fully implemented
 * and wired without waiting for backend completion.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPost, ApiError } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

/** Criteria submitted when joining the waitlist (AC-1). */
export interface WaitlistRequest {
  /** ISO-8601 preferred date (YYYY-MM-DD). */
  preferredDate: string;
  /**
   * Preferred start of the time range in HH:MM (24-hour).
   * The backend will match slots that start at or after this time.
   */
  preferredTimeStart: string;
  /**
   * Preferred end of the time range in HH:MM (24-hour).
   * The backend will match slots that start before this time.
   */
  preferredTimeEnd: string;
  /** Optional provider UUID; null means any provider. */
  preferredProviderId: string | null;
  /** Optional provider display name for confirmation copy. */
  preferredProviderName: string | null;
  /** Visit type label (e.g. "General Checkup"). */
  appointmentType: string;
}

/** Server response when waitlist registration succeeds (201). */
export interface WaitlistRegistration {
  /** Unique waitlist entry identifier. */
  waitlistId: string;
  /** Echo of the submitted preferred date (YYYY-MM-DD). */
  preferredDate: string;
  preferredTimeStart: string;
  preferredTimeEnd: string;
  preferredProviderName: string | null;
  appointmentType: string;
  /** UTC ISO-8601 timestamp of registration. */
  registeredAt: string;
}

/** Normalised error outcome for callers. */
export type WaitlistErrorKind = 'duplicate' | 'validation' | 'error';

export interface WaitlistError {
  kind: WaitlistErrorKind;
  message: string;
  status: number;
}

// ─── Messages ────────────────────────────────────────────────────────────────

export const WAITLIST_MESSAGES = {
  DUPLICATE: 'You are already on the waitlist for this date and time.',
  GENERIC_ERROR: 'Unable to join the waitlist. Please try again.',
} as const;

// ─── API call ─────────────────────────────────────────────────────────────────

async function joinWaitlist(request: WaitlistRequest): Promise<WaitlistRegistration> {
  try {
    return await apiPost<WaitlistRegistration>('/api/waitlist', request);
  } catch (err) {
    if (err instanceof ApiError) {
      const waitlistError: WaitlistError = {
        kind: err.status === 409 ? 'duplicate' : err.status === 400 ? 'validation' : 'error',
        message:
          err.status === 409
            ? WAITLIST_MESSAGES.DUPLICATE
            : err.message || WAITLIST_MESSAGES.GENERIC_ERROR,
        status: err.status,
      };
      throw waitlistError;
    }
    const waitlistError: WaitlistError = {
      kind: 'error',
      message: WAITLIST_MESSAGES.GENERIC_ERROR,
      status: 0,
    };
    throw waitlistError;
  }
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useJoinWaitlist() {
  const queryClient = useQueryClient();

  return useMutation<WaitlistRegistration, WaitlistError, WaitlistRequest>({
    mutationFn: joinWaitlist,
    onSuccess: () => {
      // Invalidate any active waitlist queries so the confirmation UI stays fresh.
      void queryClient.invalidateQueries({ queryKey: ['waitlist'] });
    },
  });
}
