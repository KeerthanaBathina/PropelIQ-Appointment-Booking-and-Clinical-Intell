/**
 * useUpdateQueueStatus — React Query mutation for updating a queue entry status (US_052 AC-3).
 *
 * PUT /api/queue/{queueId}/status → Updated QueueEntry (200 OK)
 *
 * Used for: marking patient as cancelled.
 * On success: invalidates 'arrival-queue-today' to reflect the status change immediately.
 *
 * Usage:
 *   const updateStatus = useUpdateQueueStatus();
 *   await updateStatus.mutateAsync({ queueId: 'uuid', status: 'cancelled' });
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut } from '@/lib/apiClient';
import type { QueueEntry, QueueEntryStatus } from './useArrivalQueue';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface UpdateQueueStatusRequest {
  queueId: string;
  status: QueueEntryStatus;
}

// ─── API fn ───────────────────────────────────────────────────────────────────

async function putQueueStatus({ queueId, status }: UpdateQueueStatusRequest): Promise<QueueEntry> {
  return apiPut<QueueEntry>(`/api/queue/${queueId}/status`, { status });
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useUpdateQueueStatus() {
  const queryClient = useQueryClient();

  return useMutation<QueueEntry, Error, UpdateQueueStatusRequest>({
    mutationFn: putQueueStatus,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['arrival-queue-today'] });
    },
  });
}
