/**
 * useOverrideCode — React Query mutation for overriding an AI-suggested code
 * with a staff-selected replacement and mandatory clinical justification
 * (US_049 AC-3, FR-049).
 *
 * PUT /api/codes/{codeId}/override
 * Body: { new_code_value, new_description, justification }
 *
 * On success: invalidates verification-queue and verification-progress queries
 * for the patient so the UI reflects the overridden status immediately.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut } from '@/lib/apiClient';
import { verificationKeys } from './useVerificationQueue';

// ─── DTOs ─────────────────────────────────────────────────────────────────────

export interface OverrideCodeRequest {
  codeId:          string;
  new_code_value:  string;
  new_description: string;
  justification:   string;
}

export interface OverrideCodeResponse {
  code_id:             string;
  status:              string;  // "overridden"
  original_code_value: string;
  new_code_value:      string;
  justification:       string;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useOverrideCode(patientId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ codeId, ...body }: OverrideCodeRequest) =>
      apiPut<OverrideCodeResponse>(`/api/codes/${codeId}/override`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: verificationKeys.queue(patientId) });
      queryClient.invalidateQueries({ queryKey: verificationKeys.progress(patientId) });
    },
  });
}
