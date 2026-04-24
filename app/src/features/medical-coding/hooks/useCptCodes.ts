/**
 * useCptCodes — React Query fetch hook for pending CPT procedure codes (US_048 AC-2, AC-3).
 *
 * GET /api/coding/cpt/pending/{patientId}
 *
 * Cache: 5-minute staleTime per NFR-030.
 * Query key: ['cpt-codes', patientId]
 *
 * Returns:
 *   data        — CptMappingResponseDto | undefined
 *   isLoading   — true while initial fetch is in flight
 *   isFetching  — true during any background refetch
 *   isError     — true when the fetch failed
 *   error       — unknown (TanStack Query v4 contract)
 *   refetch     — manual refresh callback
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import type { CptMappingResponseDto } from '../types/cpt.types';

// ─── Query keys ───────────────────────────────────────────────────────────────

export const cptKeys = {
  pending: (patientId: string) => ['cpt-codes', patientId] as const,
};

// ─── Return type ──────────────────────────────────────────────────────────────

export interface UseCptCodesReturn {
  data: CptMappingResponseDto | undefined;
  isLoading: boolean;
  isFetching: boolean;
  isError: boolean;
  error: unknown;
  refetch: () => void;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useCptCodes(
  patientId: string | null | undefined,
): UseCptCodesReturn {
  const { data, isLoading, isFetching, isError, error, refetch } = useQuery({
    queryKey: cptKeys.pending(patientId ?? ''),
    queryFn: () =>
      apiGet<CptMappingResponseDto>(
        `/api/coding/cpt/pending/${patientId}`,
      ),
    enabled: !!patientId,
    staleTime: 5 * 60 * 1_000, // 5 min — matches NFR-030 / backend Redis TTL
  });

  return { data, isLoading, isFetching, isError, error, refetch };
}
