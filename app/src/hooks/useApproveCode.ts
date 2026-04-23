/**
 * useApproveCode — React Query mutation for approving an AI-suggested code
 * (US_049 AC-2, FR-049).
 *
 * PUT /api/codes/{codeId}/approve
 *   200 OK   → ApproveCodeResponse (code verified, audit log written)
 *   409 Conflict → DeprecatedCodeConflict (approval blocked; use DeprecatedCodeAlert)
 *
 * On 409: throws a DeprecatedCodeError with structured conflict data so the
 * caller can surface DeprecatedCodeAlert with replacement chips (EC-1).
 *
 * On success: invalidates verification-queue and verification-progress queries
 * for the patient so the UI reflects the new status without a manual reload.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut, ApiError } from '@/lib/apiClient';
import { verificationKeys } from './useVerificationQueue';

// ─── DTOs ─────────────────────────────────────────────────────────────────────

export interface ApproveCodeResponse {
  code_id:     string;
  status:      string;  // "verified"
  verified_at: string;
}

export interface DeprecatedCodeConflict {
  error_code:        string;  // "deprecated_code"
  message:           string;
  deprecated_notice: string;
  replacement_codes: string[];
  correlation_id:    string;
}

// ─── Custom error ─────────────────────────────────────────────────────────────

export class DeprecatedCodeError extends Error {
  constructor(public readonly conflict: DeprecatedCodeConflict) {
    super('deprecated_code');
    this.name = 'DeprecatedCodeError';
  }
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useApproveCode(patientId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (codeId: string): Promise<ApproveCodeResponse> => {
      try {
        return await apiPut<ApproveCodeResponse>(
          `/api/codes/${codeId}/approve`,
          {},
        );
      } catch (err) {
        if (err instanceof ApiError && err.status === 409) {
          let conflict: DeprecatedCodeConflict;
          try {
            conflict = JSON.parse(err.message) as DeprecatedCodeConflict;
          } catch {
            conflict = {
              error_code:        'deprecated_code',
              message:           err.message,
              deprecated_notice: 'This code has been deprecated.',
              replacement_codes: [],
              correlation_id:    '',
            };
          }
          throw new DeprecatedCodeError(conflict);
        }
        throw err;
      }
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: verificationKeys.queue(patientId) });
      queryClient.invalidateQueries({ queryKey: verificationKeys.progress(patientId) });
    },
  });
}
