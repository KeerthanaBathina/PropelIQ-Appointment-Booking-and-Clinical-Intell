/**
 * useConflicts — React Query fetch hook for the patient conflict list (US_044 AC-2, AC-3, FR-053).
 *
 * GET /api/patients/:patientId/conflicts?status=&severity=&type=&page=&pageSize=
 *
 * Query key: ['patient-conflicts', patientId, filters]
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ConflictListDto {
  conflictId: string;
  conflictType: string;
  severity: 'Critical' | 'High' | 'Medium' | 'Low';
  status: 'Detected' | 'UnderReview' | 'Resolved' | 'Dismissed';
  isUrgent: boolean;
  patientName: string;
  conflictDescription: string;
  sourceDocumentCount: number;
  aiConfidenceScore: number;
  createdAt: string;
}

export interface ConflictPagedResult {
  items: ConflictListDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ConflictFilters {
  status?: string;
  severity?: string;
  type?: string;
  page?: number;
  pageSize?: number;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const conflictKeys = {
  list: (patientId: string, filters: ConflictFilters) =>
    ['patient-conflicts', patientId, filters] as const,
  detail: (patientId: string, conflictId: string) =>
    ['patient-conflict-detail', patientId, conflictId] as const,
  summary: (patientId: string) =>
    ['patient-conflict-summary', patientId] as const,
};

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseConflictsReturn {
  data: ConflictPagedResult | undefined;
  isLoading: boolean;
  isError: boolean;
  refetch: () => void;
}

export function useConflicts(
  patientId: string | null | undefined,
  filters: ConflictFilters = {},
): UseConflictsReturn {
  const params = new URLSearchParams();
  if (filters.status)   params.set('status', filters.status);
  if (filters.severity) params.set('severity', filters.severity);
  if (filters.type)     params.set('type', filters.type);
  if (filters.page)     params.set('page', String(filters.page));
  if (filters.pageSize) params.set('pageSize', String(filters.pageSize));
  const qs = params.toString() ? `?${params.toString()}` : '';

  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: conflictKeys.list(patientId ?? '', filters),
    queryFn: () =>
      apiGet<ConflictPagedResult>(`/api/patients/${patientId}/conflicts${qs}`),
    enabled: !!patientId,
    staleTime: 30_000,
  });

  return { data, isLoading, isError, refetch };
}
