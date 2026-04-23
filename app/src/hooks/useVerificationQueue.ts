/**
 * useVerificationQueue — React Query fetch hook for the code verification queue
 * (US_049 AC-1, FR-049).
 *
 * GET /api/patients/{patientId}/codes/verification-queue?codeType=icd10|cpt
 *
 * Returns all codes (ICD-10 and CPT) awaiting staff approval/override.
 * Optional codeType filter narrows results to a single code type.
 *
 * Query key: ['verification-queue', patientId, codeTypeFilter]
 * Stale time: 30 s — refreshed automatically on approve/override mutations.
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── DTOs (match VerificationModels.cs [JsonPropertyName] snake_case) ─────────

export interface VerificationQueueItem {
  /** Guid of the MedicalCode record. */
  code_id: string;
  /** "ICD10" or "CPT". */
  code_type: string;
  /** Current code value (e.g. "E11.9"). */
  code_value: string;
  description: string;
  /** AI-generated justification text. */
  justification: string;
  /** AI confidence score in [0.0, 1.0]. */
  ai_confidence_score: number;
  /** "Pending" | "Verified" | "Overridden" | "Deprecated". */
  status: string;
  suggested_by_ai: boolean;
  is_deprecated: boolean;
  created_at: string;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const verificationKeys = {
  queue:      (patientId: string, codeType?: string) =>
    ['verification-queue', patientId, codeType ?? 'all'] as const,
  progress:   (patientId: string) =>
    ['verification-progress', patientId] as const,
  auditTrail: (codeId: string) =>
    ['code-audit-trail', codeId] as const,
  search:     (query: string, codeType: string) =>
    ['code-search', query, codeType] as const,
};

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseVerificationQueueReturn {
  items:     VerificationQueueItem[];
  isLoading: boolean;
  isError:   boolean;
  refetch:   () => void;
}

export function useVerificationQueue(
  patientId:      string | null | undefined,
  codeTypeFilter?: string,
): UseVerificationQueueReturn {
  const params = new URLSearchParams();
  if (codeTypeFilter && codeTypeFilter !== 'all') {
    params.set('codeType', codeTypeFilter);
  }
  const qs = params.toString() ? `?${params.toString()}` : '';

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: verificationKeys.queue(patientId ?? '', codeTypeFilter),
    queryFn:  () =>
      apiGet<VerificationQueueItem[]>(
        `/api/patients/${patientId}/codes/verification-queue${qs}`,
      ),
    enabled:   !!patientId,
    staleTime: 30_000,
  });

  return {
    items:     data ?? [],
    isLoading,
    isError,
    refetch,
  };
}
