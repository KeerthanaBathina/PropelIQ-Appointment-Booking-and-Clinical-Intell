/**
 * useSelectValue — React Query mutation hook for selecting the correct data value
 * from conflicting sources (US_045 AC-2, FR-054).
 *
 * PUT /api/patients/:patientId/conflicts/:conflictId/select-value
 *
 * On success: invalidates conflict list, detail, summary, resolution-progress, and
 * verification-status queries so all UI panels refresh without a manual reload.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut } from '@/lib/apiClient';
import { conflictKeys } from './useConflicts';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface SelectValueRequest {
  patientId: string;
  conflictId: string;
  selectedExtractedDataId: string;
  resolutionNotes: string;
}

// ─── Query-key helpers (re-exported for sibling hooks) ────────────────────────

export const resolutionKeys = {
  progress: (patientId: string) =>
    ['conflict-resolution-progress', patientId] as const,
  verificationStatus: (patientId: string) =>
    ['patient-verification-status', patientId] as const,
};

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useSelectValue() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      patientId,
      conflictId,
      selectedExtractedDataId,
      resolutionNotes,
    }: SelectValueRequest) =>
      apiPut<void>(
        `/api/patients/${patientId}/conflicts/${conflictId}/select-value`,
        { selectedExtractedDataId, resolutionNotes },
      ),

    onSuccess: (_data, variables) => {
      const { patientId, conflictId } = variables;

      queryClient.invalidateQueries({ queryKey: ['patient-conflicts', patientId] });
      queryClient.invalidateQueries({ queryKey: conflictKeys.detail(patientId, conflictId) });
      queryClient.invalidateQueries({ queryKey: conflictKeys.summary(patientId) });
      queryClient.invalidateQueries({ queryKey: resolutionKeys.progress(patientId) });
      queryClient.invalidateQueries({ queryKey: resolutionKeys.verificationStatus(patientId) });
      queryClient.invalidateQueries({ queryKey: ['patient-profile-360', patientId] });
    },
  });
}
