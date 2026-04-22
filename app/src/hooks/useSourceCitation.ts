/**
 * useSourceCitation — React Query fetch hook for the source document citation
 * panel (US_043 AC-3, SCR-013 SourceCitationPanel).
 *
 * GET /api/patients/:patientId/profile/data-points/:extractedDataId/citation
 *   → SourceCitationDto
 *
 * Enabled only when both patientId and extractedDataId are set.
 *
 * Query key: ['patient-profile-citation', patientId, extractedDataId]
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface SourceCitationDto {
  extractedDataId: string;
  documentId: string;
  documentName: string;
  documentCategory: string;
  uploadDate: string;
  pageNumber: number | null;
  extractionRegion: string | null;
  sourceSnippet: string | null;
  sourceAttribution: string | null;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const sourceCitationKeys = {
  citation: (patientId: string, extractedDataId: string) =>
    ['patient-profile-citation', patientId, extractedDataId] as const,
};

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseSourceCitationReturn {
  citation: SourceCitationDto | undefined;
  isLoading: boolean;
  isError: boolean;
}

export function useSourceCitation(
  patientId: string | null | undefined,
  extractedDataId: string | null | undefined,
): UseSourceCitationReturn {
  const { data, isLoading, isError } = useQuery({
    queryKey: sourceCitationKeys.citation(patientId ?? '', extractedDataId ?? ''),
    queryFn: () =>
      apiGet<SourceCitationDto>(
        `/api/patients/${patientId}/profile/data-points/${extractedDataId}/citation`,
      ),
    enabled: !!patientId && !!extractedDataId,
    staleTime: 60_000,
  });

  return {
    citation: data,
    isLoading,
    isError,
  };
}
