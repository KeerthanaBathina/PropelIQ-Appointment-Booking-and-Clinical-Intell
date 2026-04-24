/**
 * useVerificationProgress — React Query fetch hook for the patient-level
 * code verification progress summary (US_049 EC-2, FR-049).
 *
 * GET /api/patients/{patientId}/codes/verification-progress
 *
 * Returns counts and a derived status_label ("fully verified" | "partially verified"
 * | "pending review") used by VerificationProgressBar.
 *
 * Query key: ['verification-progress', patientId]
 * Stale time: 30 s — refreshed automatically on approve/override mutations.
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import { verificationKeys } from './useVerificationQueue';

// ─── DTOs ─────────────────────────────────────────────────────────────────────

export interface VerificationProgress {
  total_codes:      number;
  verified_count:   number;
  overridden_count: number;
  pending_count:    number;
  deprecated_count: number;
  /** "fully verified" | "partially verified" | "pending review" */
  status_label:     string;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseVerificationProgressReturn {
  progress:  VerificationProgress | undefined;
  isLoading: boolean;
}

export function useVerificationProgress(
  patientId: string | null | undefined,
): UseVerificationProgressReturn {
  const { data, isLoading } = useQuery({
    queryKey: verificationKeys.progress(patientId ?? ''),
    queryFn:  () =>
      apiGet<VerificationProgress>(
        `/api/patients/${patientId}/codes/verification-progress`,
      ),
    enabled:   !!patientId,
    staleTime: 30_000,
  });

  return { progress: data, isLoading };
}
