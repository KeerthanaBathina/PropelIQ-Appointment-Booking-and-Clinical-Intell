/**
 * useVerificationStatus — React Query fetch hook for patient profile verification
 * lifecycle state (US_045 AC-4, FR-054).
 *
 * GET /api/patients/:patientId/profile/verification-status
 *
 * Query key: ['patient-verification-status', patientId]
 * Stale time: 30 s — updated after every resolve mutation via invalidation.
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import { resolutionKeys } from './useSelectValue';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface VerificationStatusDto {
  status: 'Unverified' | 'PartiallyVerified' | 'Verified';
  verifiedByUserId: string | null;
  verifiedByUserName: string | null;
  verifiedAt: string | null;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseVerificationStatusReturn {
  verificationStatus: VerificationStatusDto | undefined;
  isLoading: boolean;
  isError: boolean;
}

export function useVerificationStatus(
  patientId: string | null | undefined,
): UseVerificationStatusReturn {
  const { data, isLoading, isError } = useQuery({
    queryKey: resolutionKeys.verificationStatus(patientId ?? ''),
    queryFn: () =>
      apiGet<VerificationStatusDto>(
        `/api/patients/${patientId}/profile/verification-status`,
      ),
    enabled: !!patientId,
    staleTime: 30_000,
  });

  return { verificationStatus: data, isLoading, isError };
}
