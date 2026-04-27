/**
 * useMarkArrived — React Query mutation for marking a patient as arrived (US_052 AC-1).
 *
 * POST /api/queue/arrive → Updated QueueEntry (201 Created)
 *
 * Error handling:
 *   - 409 Conflict: Patient already marked as arrived — message forwarded to caller
 *     for display in MUI Snackbar (duplicate arrival prevention, task checklist item 7).
 *
 * On success: invalidates 'arrival-queue-today' so the queue table re-fetches.
 *
 * Usage:
 *   const markArrived = useMarkArrived();
 *   await markArrived.mutateAsync({ appointmentId: 'uuid' });
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPost, ApiError } from '@/lib/apiClient';
import type { QueueEntry } from './useArrivalQueue';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface MarkArrivedRequest {
  appointmentId: string;
}

export interface MarkArrivedError {
  status: number;
  message: string;
  isDuplicate: boolean;
}

// ─── API fn ───────────────────────────────────────────────────────────────────

async function postMarkArrived(request: MarkArrivedRequest): Promise<QueueEntry> {
  try {
    return await apiPost<QueueEntry>('/api/queue/arrive', request);
  } catch (err) {
    if (err instanceof ApiError) {
      // Parse 409 "Patient already marked as arrived at [timestamp]" body
      let message = err.message;
      try {
        const parsed = JSON.parse(err.message) as { message?: string };
        if (parsed.message) message = parsed.message;
      } catch {
        // message is plain text — use as-is
      }
      const arrivedError: MarkArrivedError = {
        status: err.status,
        message,
        isDuplicate: err.status === 409,
      };
      throw arrivedError;
    }
    throw err;
  }
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useMarkArrived() {
  const queryClient = useQueryClient();

  return useMutation<QueueEntry, MarkArrivedError, MarkArrivedRequest>({
    mutationFn: postMarkArrived,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['arrival-queue-today'] });
    },
  });
}
