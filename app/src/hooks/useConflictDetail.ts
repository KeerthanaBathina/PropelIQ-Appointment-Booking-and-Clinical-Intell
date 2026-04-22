/**
 * useConflictDetail — React Query fetch hook for a single conflict with all source
 * citations (US_044 AC-2, AIR-007, FR-053).
 *
 * GET /api/patients/:patientId/conflicts/:conflictId
 *
 * Query key: ['patient-conflict-detail', patientId, conflictId]
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import { conflictKeys } from './useConflicts';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ConflictSourceCitationDto {
  documentId: string;
  documentName: string;
  documentCategory: string;
  uploadDate: string;
  extractedDataId: string;
  dataType: string;
  normalizedValue: string | null;
  rawText: string | null;
  unit: string | null;
  sourceSnippet: string | null;
  confidenceScore: number;
  sourceAttributionText: string;
  pageNumber: number;
  extractionRegion: string;
}

export interface ConflictDetailDto {
  conflictId: string;
  patientId: string;
  patientName: string;
  conflictType: string;
  severity: 'Critical' | 'High' | 'Medium' | 'Low';
  status: 'Detected' | 'UnderReview' | 'Resolved' | 'Dismissed';
  isUrgent: boolean;
  conflictDescription: string;
  aiExplanation: string;
  aiConfidenceScore: number;
  sourceCitations: ConflictSourceCitationDto[];
  createdAt: string;
  resolvedByUserName: string | null;
  resolutionNotes: string | null;
  resolvedAt: string | null;
  profileVersionId: string | null;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseConflictDetailReturn {
  conflict: ConflictDetailDto | undefined;
  isLoading: boolean;
  isError: boolean;
}

export function useConflictDetail(
  patientId: string | null | undefined,
  conflictId: string | null | undefined,
): UseConflictDetailReturn {
  const { data, isLoading, isError } = useQuery({
    queryKey: conflictKeys.detail(patientId ?? '', conflictId ?? ''),
    queryFn: () =>
      apiGet<ConflictDetailDto>(
        `/api/patients/${patientId}/conflicts/${conflictId}`,
      ),
    enabled: !!patientId && !!conflictId,
    staleTime: 15_000,
  });

  return { conflict: data, isLoading, isError };
}
