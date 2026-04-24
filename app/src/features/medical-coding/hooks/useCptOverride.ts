/**
 * useCptOverride — React Query mutation for overriding a CPT code suggestion (US_048).
 *
 * PUT /api/coding/cpt/override
 * Body: { medicalCodeId, replacementCode, justification }
 *
 * Override actions are audit-logged server-side per HIPAA compliance requirements.
 *
 * On success: invalidates the pending CPT codes query for the patient.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut } from '@/lib/apiClient';
import type { CptOverrideRequest } from '../types/cpt.types';
import { cptKeys } from './useCptCodes';

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useCptOverride(patientId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: CptOverrideRequest) =>
      apiPut<void>('/api/coding/cpt/override', request),

    onSuccess: () => {
      // Invalidate + refetch so the table reflects the new "Overridden" status
      void queryClient.invalidateQueries({
        queryKey: cptKeys.pending(patientId),
      });
    },
  });
}
