/**
 * useQueuePriority — React Query mutation for updating a queue entry's priority (US_054).
 *
 * PUT /api/queue/{queueId}/priority → Updated QueueEntry (200 OK)
 *
 * On success: invalidates both 'queue-dashboard' and 'arrival-queue-today' query caches
 * so the table re-renders with the new priority tier ordering immediately.
 *
 * Usage:
 *   const { mutate } = useQueuePriority();
 *   mutate({ queueId: 'uuid', priority: 'urgent' });
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut } from '@/lib/apiClient';
import type { QueueEntry } from '@/hooks/useQueueData';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface UpdatePriorityRequest {
  queueId:  string;
  priority: 'urgent' | 'normal';
}

// ─── API fn ───────────────────────────────────────────────────────────────────

async function putQueuePriority({ queueId, priority }: UpdatePriorityRequest): Promise<QueueEntry> {
  return apiPut<QueueEntry>(`/api/queue/${queueId}/priority`, { priority });
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useQueuePriority() {
  const queryClient = useQueryClient();

  return useMutation<QueueEntry, Error, UpdatePriorityRequest>({
    mutationFn: putQueuePriority,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['queue-dashboard'] });
      void queryClient.invalidateQueries({ queryKey: ['arrival-queue-today'] });
    },
  });
}
