/**
 * useIcd10Codes — React Query fetch hook for pending ICD-10 codes (US_047 AC-1, AC-2, AC-4).
 *
 * GET /api/coding/icd10/pending?patientId={id}
 *
 * Cache: 5-minute staleTime per NFR-030 (mirrors backend Redis cache TTL).
 * Query key: ['icd10-codes', patientId]
 *
 * Returns:
 *   data        — Icd10MappingResponseDto | undefined
 *   isLoading   — true while initial fetch is in flight
 *   isFetching  — true during any background refetch
 *   isError     — true when the fetch failed
 *   error       — ApiError | null
 *   refetch     — manual refresh callback
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Response types (mirror src/UPACIP.Api/Models/CodingModels.cs) ──────────

export interface Icd10CodeDto {
  medicalCodeId: string | null;
  codeValue: string;
  description: string;
  confidenceScore: number;
  justification: string;
  relevanceRank: number | null;
  validationStatus: string | null;
  libraryVersion: string | null;
  requiresReview: boolean;
}

export interface Icd10MappingResponseDto {
  patientId: string;
  codes: Icd10CodeDto[];
  unmappedDiagnosisIds: string[];
  lastCodingRunAt: string | null;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const icd10Keys = {
  pending: (patientId: string) => ['icd10-codes', patientId] as const,
};

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseIcd10CodesReturn {
  data: Icd10MappingResponseDto | undefined;
  isLoading: boolean;
  isFetching: boolean;
  isError: boolean;
  error: unknown;
  refetch: () => void;
}

export function useIcd10Codes(
  patientId: string | null | undefined,
): UseIcd10CodesReturn {
  const { data, isLoading, isFetching, isError, error, refetch } = useQuery({
    queryKey: icd10Keys.pending(patientId ?? ''),
    queryFn: () =>
      apiGet<Icd10MappingResponseDto>(
        `/api/coding/icd10/pending?patientId=${patientId}`,
      ),
    enabled: !!patientId,
    staleTime: 5 * 60 * 1_000, // 5 min — matches NFR-030 / backend Redis TTL
  });

  return { data, isLoading, isFetching, isError, error, refetch };
}
