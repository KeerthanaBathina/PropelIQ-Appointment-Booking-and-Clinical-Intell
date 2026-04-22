/**
 * usePatientProfile — React Query fetch hook for the consolidated Patient Profile 360
 * view (US_043 AC-1, FR-043, SCR-013).
 *
 * GET /api/patients/:patientId/profile → PatientProfile360Dto
 *
 * staleTime: 5 minutes — aligns with the backend Redis cache TTL (NFR-030) so the
 * client never re-fetches more aggressively than the server invalidates.
 *
 * 404 handling: when the backend returns 404 (profile not yet consolidated) the query
 * resolves to `undefined` rather than throwing, enabling the caller to render an
 * "empty / not consolidated yet" state instead of an error state.
 *
 * Query key: ['patient-profile-360', patientId]
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet, ApiError } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

/** Unified clinical data point from the consolidated patient profile. */
export interface ProfileDataPointDto {
  extractedDataId: string;
  dataType: 'Medication' | 'Diagnosis' | 'Procedure' | 'Allergy';
  normalizedValue: string;
  rawText: string;
  unit: string | null;
  sourceSnippet: string | null;
  /** Confidence score in [0, 1]. Null when model could not assign a score. */
  confidenceScore: number | null;
  sourceDocumentId: string;
  sourceDocumentName: string;
  sourceDocumentCategory: string;
  pageNumber: number | null;
  extractionRegion: string | null;
  sourceAttribution: string | null;
  flaggedForReview: boolean;
  verificationStatus: 'Pending' | 'Verified' | 'Corrected';
  verifiedAtUtc: string | null;
}

/** Consolidated Patient Profile 360 response. */
export interface PatientProfile360Dto {
  patientId: string;
  patientName: string;
  dateOfBirth: string;
  currentVersionNumber: number;
  lastConsolidatedAt: string | null;
  pendingReviewCount: number;
  conflictCount: number;
  medications: ProfileDataPointDto[];
  diagnoses: ProfileDataPointDto[];
  procedures: ProfileDataPointDto[];
  allergies: ProfileDataPointDto[];
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const patientProfileKeys = {
  profile: (patientId: string) => ['patient-profile-360', patientId] as const,
};

// ─── API fn ───────────────────────────────────────────────────────────────────

async function fetchPatientProfile(
  patientId: string,
): Promise<PatientProfile360Dto | undefined> {
  try {
    return await apiGet<PatientProfile360Dto>(`/api/patients/${patientId}/profile`);
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return undefined;
    }
    throw err;
  }
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UsePatientProfileReturn {
  profile: PatientProfile360Dto | undefined;
  isLoading: boolean;
  isError: boolean;
  error: Error | null;
  refetch: () => void;
}

export function usePatientProfile(patientId: string | null | undefined): UsePatientProfileReturn {
  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: patientProfileKeys.profile(patientId ?? ''),
    queryFn: () => fetchPatientProfile(patientId!),
    enabled: !!patientId,
    staleTime: 5 * 60_000, // 5 min — matches BE Redis TTL (NFR-030)
    retry: (failureCount, err) => {
      // Never retry 404 (profile not yet consolidated)
      if (err instanceof ApiError && err.status === 404) return false;
      return failureCount < 3;
    },
  });

  return {
    profile: data,
    isLoading,
    isError,
    error: error as Error | null,
    refetch,
  };
}
