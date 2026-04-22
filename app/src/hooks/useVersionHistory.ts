/**
 * useVersionHistory — React Query fetch hook for the patient profile version
 * history panel (US_043 SCR-013 VersionHistoryPanel).
 *
 * GET /api/patients/:patientId/profile/versions → VersionHistoryDto[]
 *
 * Query key: ['patient-profile-versions', patientId]
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface VersionHistoryDto {
  versionNumber: number;
  createdAt: string;
  consolidatedByUserName: string | null;
  consolidationType: 'Full' | 'Incremental';
  sourceDocumentCount: number;
  dataSnapshot: Record<string, unknown> | null;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const versionHistoryKeys = {
  list: (patientId: string) => ['patient-profile-versions', patientId] as const,
};

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseVersionHistoryReturn {
  versions: VersionHistoryDto[];
  isLoading: boolean;
  isError: boolean;
  refetch: () => void;
}

export function useVersionHistory(patientId: string | null | undefined): UseVersionHistoryReturn {
  const { data, isLoading, isError, refetch } = useQuery({
    queryKey: versionHistoryKeys.list(patientId ?? ''),
    queryFn: () =>
      apiGet<VersionHistoryDto[]>(`/api/patients/${patientId}/profile/versions`),
    enabled: !!patientId,
    staleTime: 30_000,
  });

  return {
    versions: data ?? [],
    isLoading,
    isError,
    refetch,
  };
}
