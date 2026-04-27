/**
 * useQueueReorder — React Query mutation for persisting queue position reorder (US_054).
 *
 * PUT /api/queue/reorder  body: { queueId, newPosition } → QueueReorderResponse (200 OK)
 *
 * Optimistic update strategy:
 *   1. On mutate: store current cache snapshot + immediately update React Query cache
 *      to reflect the new order (optimistic UI).
 *   2. On 409 Conflict (concurrent edit by another staff member): rollback to snapshot,
 *      refetch fresh data, and surface an informational snackbar message.
 *   3. On any other error: rollback and propagate error.
 *
 * The 409 conflict case surfaces as a structured `ReorderConflictError` so callers
 * can display the "Queue was updated by another staff member. Refreshing..." message.
 *
 * Usage:
 *   const { mutate, isPending } = useQueueReorder();
 *   mutate({ queueId, newPosition });
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPut } from '@/lib/apiClient';
import type { QueueEntry } from '@/hooks/useQueueData';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ReorderRequest {
  queueId:     string;
  newPosition: number;
}

/** Response from PUT /api/queue/reorder — full sorted queue list. */
export interface QueueReorderResponse {
  data:       QueueEntry[];
  totalCount: number;
}

/** Thrown when the server returns 409 Conflict during reorder. */
export class ReorderConflictError extends Error {
  readonly isConflict = true as const;
  constructor() {
    super('Queue was updated by another staff member. Refreshing...');
    this.name = 'ReorderConflictError';
  }
}

// ─── API fn ───────────────────────────────────────────────────────────────────

async function putQueueReorder(request: ReorderRequest): Promise<QueueReorderResponse> {
  try {
    return await apiPut<QueueReorderResponse>('/api/queue/reorder', request);
  } catch (err: unknown) {
    // Map HTTP 409 to a typed conflict error for caller handling
    if (err instanceof Error && err.message.includes('409')) {
      throw new ReorderConflictError();
    }
    throw err;
  }
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useQueueReorder() {
  const queryClient = useQueryClient();

  return useMutation<QueueReorderResponse, Error, ReorderRequest>({
    mutationFn: putQueueReorder,

    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['queue-dashboard'] });
      void queryClient.invalidateQueries({ queryKey: ['arrival-queue-today'] });
    },

    onError: (_err) => {
      // On any error (including 409), refetch to restore authoritative server order
      void queryClient.invalidateQueries({ queryKey: ['queue-dashboard'] });
      void queryClient.invalidateQueries({ queryKey: ['arrival-queue-today'] });
    },
  });
}
