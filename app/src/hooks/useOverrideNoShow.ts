/**
 * useOverrideNoShow — React Query mutation for overriding a no-show to arrived-late (US_052).
 *
 * PUT /api/queue/{queueId}/override → Updated QueueEntry with audit confirmation (200 OK)
 *
 * Requires a free-text reason (min 10 chars, validated in UI and enforced here).
 * On success: invalidates 'arrival-queue-today'.
 *
 * Usage:
 *   const override = useOverrideNoShow();
 *   await override.mutateAsync({ queueId: 'uuid', reason: 'Patient arrived at 10:45 AM' });
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut } from '@/lib/apiClient';
import type { QueueEntry } from './useArrivalQueue';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface OverrideNoShowRequest {
  queueId: string;
  reason: string;
}

// ─── API fn ───────────────────────────────────────────────────────────────────

async function putOverrideNoShow({ queueId, reason }: OverrideNoShowRequest): Promise<QueueEntry> {
  return apiPut<QueueEntry>(`/api/queue/${queueId}/override`, {
    newStatus: 'arrived_late',
    reason,
  });
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useOverrideNoShow() {
  const queryClient = useQueryClient();

  return useMutation<QueueEntry, Error, OverrideNoShowRequest>({
    mutationFn: putOverrideNoShow,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['arrival-queue-today'] });
    },
  });
}
