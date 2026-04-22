/**
 * useResolutionProgress — React Query fetch hook for conflict resolution progress
 * counts and verification status (US_045 EC-1, AC-4, FR-054).
 *
 * GET /api/patients/:patientId/conflicts/resolution-progress
 *
 * Query key: ['conflict-resolution-progress', patientId]
 * Stale time: 30 s — progress data is updated after every mutation invalidation.
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import { resolutionKeys } from './useSelectValue';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ResolutionProgressDto {
  patientId: string;
  totalConflicts: number;
  resolvedCount: number;
  remainingCount: number;
  percentComplete: number;
  verificationStatus: string;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseResolutionProgressReturn {
  progress: ResolutionProgressDto | undefined;
  isLoading: boolean;
  isError: boolean;
}

export function useResolutionProgress(
  patientId: string | null | undefined,
): UseResolutionProgressReturn {
  const { data, isLoading, isError } = useQuery({
    queryKey: resolutionKeys.progress(patientId ?? ''),
    queryFn: () =>
      apiGet<ResolutionProgressDto>(
        `/api/patients/${patientId}/conflicts/resolution-progress`,
      ),
    enabled: !!patientId,
    staleTime: 30_000,
  });

  return { progress: data, isLoading, isError };
}
