/**
 * useCptApprove — React Query mutation for approving a CPT code suggestion (US_048).
 *
 * PUT /api/coding/cpt/approve
 * Body: { medicalCodeId: string }
 *
 * On success: invalidates the pending CPT codes query for the patient so the
 * table refreshes with the updated "Approved" status.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut } from '@/lib/apiClient';
import type { CptApproveRequest } from '../types/cpt.types';
import { cptKeys } from './useCptCodes';

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useCptApprove(patientId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CptApproveRequest) =>
      apiPut<void>('/api/coding/cpt/approve', request),

    onSuccess: () => {
      // Invalidate + refetch so the table reflects the new "Approved" status
      void queryClient.invalidateQueries({
        queryKey: cptKeys.pending(patientId),
      });
    },
  });
}
