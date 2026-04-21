/**
 * useWaitlistOfferClaim — Query/mutation for claim-link validation (US_020, AC-3).
 *
 * When a patient clicks the notification link they land on SCR-006 with
 * `?claim=<token>` in the URL.  This hook:
 *   1. Queries  GET /api/waitlist/claim/{token}  to validate the token and retrieve
 *      the held slot details (AC-3 — slot held for 1 minute by backend).
 *   2. The returned `ClaimedOffer` carries slotId, date, startTime, providerName,
 *      and `isWithin24Hours` so the UI can surface the EC-2 notice.
 *
 * Design:
 *   - If no claim token is present the query is disabled (enabled: false).
 *   - On 404 / 410 (token invalid or expired) `claimError` is populated and the
 *     UI shows an inline error rather than a blank state.
 *   - On success the caller wires the `ClaimedOffer.slot` directly into the
 *     existing `useSlotHold` / `BookingConfirmationModal` flow.
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet, ApiError } from '@/lib/apiClient';
import type { AppointmentSlot } from './useAppointmentSlots';

// ─── Types ────────────────────────────────────────────────────────────────────

/**
 * Server response for a valid claim token (GET /api/waitlist/claim/{token}).
 * The backend has already acquired a 60-second slot hold on the patient's behalf (AC-3).
 */
export interface ClaimedOffer {
  /** The waitlist slot held for this patient. */
  slot: AppointmentSlot;
  /**
   * True when the appointment time is less than 24 hours from now (EC-2).
   * The UI shows a notice but does NOT block the patient from proceeding.
   */
  isWithin24Hours: boolean;
  /** UTC ISO-8601 — when the hold was acquired. */
  holdAcquiredAt: string;
  /** Human-readable provider name (may differ from slot.providerName for clarity). */
  providerName: string;
}

/** Normalised error for invalid / expired claim tokens. */
export interface ClaimError {
  kind: 'expired' | 'not_found' | 'error';
  message: string;
}

// ─── Messages ────────────────────────────────────────────────────────────────

export const CLAIM_MESSAGES = {
  EXPIRED: 'This offer has expired. The slot is no longer available.',
  NOT_FOUND: 'This offer link is not valid. Please return to the booking page.',
  GENERIC: 'Unable to retrieve the offer. Please try again or book normally.',
} as const;

// ─── API fn ───────────────────────────────────────────────────────────────────

async function fetchClaimedOffer(claimToken: string): Promise<ClaimedOffer> {
  try {
    return await apiGet<ClaimedOffer>(`/api/waitlist/claim/${claimToken}`);
  } catch (err) {
    if (err instanceof ApiError) {
      const claimError: ClaimError = {
        kind: err.status === 410 ? 'expired' : err.status === 404 ? 'not_found' : 'error',
        message:
          err.status === 410
            ? CLAIM_MESSAGES.EXPIRED
            : err.status === 404
              ? CLAIM_MESSAGES.NOT_FOUND
              : CLAIM_MESSAGES.GENERIC,
      };
      throw claimError;
    }
    const claimError: ClaimError = { kind: 'error', message: CLAIM_MESSAGES.GENERIC };
    throw claimError;
  }
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

/**
 * @param claimToken - URL claim token from `?claim=<token>`. Pass `null` to disable.
 */
export function useWaitlistOfferClaim(claimToken: string | null) {
  return useQuery<ClaimedOffer, ClaimError>({
    queryKey: ['waitlist-claim', claimToken],
    queryFn: () => fetchClaimedOffer(claimToken!),
    enabled: claimToken !== null && claimToken.length > 0,
    retry: false, // token errors (404/410) should not retry
    staleTime: Infinity, // offer data is immutable for the duration of the hold
  });
}
