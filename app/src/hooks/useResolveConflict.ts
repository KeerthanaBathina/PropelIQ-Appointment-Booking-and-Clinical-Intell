/**
 * useResolveConflict — React Query mutation hook for resolving or dismissing a
 * clinical conflict (US_044 AC-2, AC-3, FR-053).
 *
 * PUT /api/patients/:patientId/conflicts/:conflictId/resolve
 * PUT /api/patients/:patientId/conflicts/:conflictId/dismiss
 *
 * On success: invalidates conflict list, detail, and summary queries for the patient
 * so the UI reflects the updated status immediately.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut } from '@/lib/apiClient';
import { conflictKeys } from './useConflicts';

// ─── Types ────────────────────────────────────────────────────────────────────

export type ConflictAction = 'resolve' | 'dismiss';

export interface ResolveConflictRequest {
  patientId: string;
  conflictId: string;
  action: ConflictAction;
  resolutionNotes: string;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useResolveConflict() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ patientId, conflictId, action, resolutionNotes }: ResolveConflictRequest) =>
      apiPut<void>(
        `/api/patients/${patientId}/conflicts/${conflictId}/${action}`,
        { resolutionNotes },
      ),
    onSuccess: (_data, variables) => {
      // Invalidate conflict list and detail so the UI refreshes without a manual reload.
      queryClient.invalidateQueries({
        queryKey: ['patient-conflicts', variables.patientId],
      });
      queryClient.invalidateQueries({
        queryKey: conflictKeys.detail(variables.patientId, variables.conflictId),
      });
      queryClient.invalidateQueries({
        queryKey: conflictKeys.summary(variables.patientId),
      });
      // Also invalidate the patient profile to refresh the conflictCount badge.
      queryClient.invalidateQueries({
        queryKey: ['patient-profile-360', variables.patientId],
      });
    },
  });
}
