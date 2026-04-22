/**
 * useManualFallback — React Query hooks for the manual fallback workflow (US_046).
 *
 * Hooks exported:
 *
 *   useAiHealthStatus      — GET /api/health/ai — polls AI service availability
 *                            every 60 s; updates Zustand store on status change.
 *
 *   useLowConfidenceItems  — GET /api/patients/:id/profile/low-confidence
 *                            Returns data points with confidence < 0.80 for the
 *                            manual review form (AC-1).
 *
 *   useConfirmManualEntry  — POST /api/patients/:id/profile/manual-verify
 *                            Saves a manually confirmed entry with "manual-verified"
 *                            status and staff attribution (AC-3).
 *                            On success: invalidates profile and low-confidence queries.
 */

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { apiGet, apiPost, ApiError } from '@/lib/apiClient';
import { patientProfileKeys } from '@/hooks/usePatientProfile';
import { useManualFallbackStore } from '@/stores/manualFallbackStore';

// ─── Types ────────────────────────────────────────────────────────────────────

/** Server response for GET /api/health/ai */
export interface AiHealthStatus {
  isAvailable: boolean;
  lastCheckedAt: string;
  message: string | null;
}

/** A single low-confidence extracted data point returned for manual review (AC-1). */
export interface LowConfidenceItemDto {
  extractedDataId: string;
  dataType: 'Medication' | 'Diagnosis' | 'Procedure' | 'Allergy';
  normalizedValue: string;
  rawText: string;
  unit: string | null;
  /** Confidence in [0, 1]. Always < 0.80 for items in this list. */
  confidenceScore: number;
  sourceDocumentId: string;
  sourceDocumentName: string;
  /** ISO 8601 date string; null when only partial date available (edge case). */
  recordDate: string | null;
  /** True when the server could only parse a partial date (month/year only). */
  isIncompleteDate: boolean;
  /** Chronological plausibility violation description, or null when no violation. */
  dateConflictExplanation: string | null;
}

export interface LowConfidenceListDto {
  items: LowConfidenceItemDto[];
  totalCount: number;
}

/** Request body for POST /api/patients/:id/profile/manual-verify */
export interface ManualVerifyRequest {
  patientId: string;
  extractedDataId: string;
  confirmedValue: string;
  confirmedDate: string | null;
  resolutionNotes: string;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const manualFallbackKeys = {
  aiHealth: () => ['ai-health-status'] as const,
  lowConfidence: (patientId: string) =>
    ['patient-low-confidence', patientId] as const,
};

// ─── useAiHealthStatus ────────────────────────────────────────────────────────

export function useAiHealthStatus() {
  const setAiUnavailable = useManualFallbackStore((s) => s.setAiUnavailable);

  const query = useQuery({
    queryKey: manualFallbackKeys.aiHealth(),
    queryFn: async () => {
      try {
        return await apiGet<AiHealthStatus>('/api/health/ai');
      } catch (err) {
        // Treat any error (including network failure) as AI unavailable.
        if (err instanceof ApiError && err.status >= 500) {
          return { isAvailable: false, lastCheckedAt: new Date().toISOString(), message: 'Service error' } satisfies AiHealthStatus;
        }
        throw err;
      }
    },
    refetchInterval: 60_000,  // poll every 60 s
    staleTime: 55_000,
  });

  // Sync availability into Zustand store so any component can read it without
  // drilling props. Side effect runs after each successful health check.
  useEffect(() => {
    if (query.data != null) {
      setAiUnavailable(!query.data.isAvailable);
    }
  }, [query.data, setAiUnavailable]);

  return query;
}

// ─── useLowConfidenceItems ────────────────────────────────────────────────────

export function useLowConfidenceItems(patientId: string | null | undefined) {
  const { data, isLoading, isError } = useQuery({
    queryKey: manualFallbackKeys.lowConfidence(patientId ?? ''),
    queryFn: () =>
      apiGet<LowConfidenceListDto>(
        `/api/patients/${patientId}/profile/low-confidence`,
      ),
    enabled: !!patientId,
    staleTime: 30_000,
  });

  return { items: data?.items ?? [], totalCount: data?.totalCount ?? 0, isLoading, isError };
}

// ─── useConfirmManualEntry ────────────────────────────────────────────────────

export function useConfirmManualEntry() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (req: ManualVerifyRequest) =>
      apiPost<void>(
        `/api/patients/${req.patientId}/profile/manual-verify`,
        {
          extractedDataId: req.extractedDataId,
          confirmedValue: req.confirmedValue,
          confirmedDate: req.confirmedDate,
          resolutionNotes: req.resolutionNotes,
        },
      ),

    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: patientProfileKeys.profile(variables.patientId),
      });
      queryClient.invalidateQueries({
        queryKey: manualFallbackKeys.lowConfidence(variables.patientId),
      });
    },
  });
}
