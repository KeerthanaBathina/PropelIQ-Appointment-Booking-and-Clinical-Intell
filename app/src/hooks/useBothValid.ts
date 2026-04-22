/**
 * useBothValid — React Query mutation hook for the "Both Valid — Different Dates"
 * conflict resolution path (US_045 EC-2, FR-054).
 *
 * PUT /api/patients/:patientId/conflicts/:conflictId/both-valid
 *
 * On success: invalidates conflict list, detail, summary, resolution-progress, and
 * verification-status queries.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut } from '@/lib/apiClient';
import { conflictKeys } from './useConflicts';
import { resolutionKeys } from './useSelectValue';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface BothValidRequest {
  patientId: string;
  conflictId: string;
  explanation: string;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useBothValid() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ patientId, conflictId, explanation }: BothValidRequest) =>
      apiPut<void>(
        `/api/patients/${patientId}/conflicts/${conflictId}/both-valid`,
        { explanation },
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
