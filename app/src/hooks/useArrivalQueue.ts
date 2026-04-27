/**
 * useArrivalQueue — React Query fetch hook for the real-time arrival queue (US_052).
 *
 * GET /api/queue/today → ArrivalQueueResponse
 *
 * Auto-refreshes every 5 seconds to satisfy UXR-103 (queue updates ≤5 s).
 * staleTime is intentionally 0 so data is always re-fetched on window focus.
 *
 * Query key: ['arrival-queue-today']
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

export type QueueEntryStatus =
  | 'waiting'
  | 'in_visit'
  | 'completed'
  | 'no_show'
  | 'arrived_late';

export type AppointmentStatus =
  | 'scheduled'
  | 'completed'
  | 'cancelled'
  | 'no-show';

export type QueueEntryPriority = 'normal' | 'urgent';

export interface QueueEntry {
  queueId: string;
  appointmentId: string;
  patientName: string;
  /** ISO-8601 UTC appointment time */
  appointmentTime: string;
  /** ISO-8601 UTC arrival timestamp, null when patient has not arrived */
  arrivalTimestamp: string | null;
  /** Server-computed wait time in minutes */
  waitTimeMinutes: number;
  priority: QueueEntryPriority;
  status: QueueEntryStatus;
  appointmentStatus: AppointmentStatus;
}

export interface ArrivalQueueResponse {
  data: QueueEntry[];
  totalCount: number;
}

// ─── API fn ───────────────────────────────────────────────────────────────────

async function fetchArrivalQueue(): Promise<ArrivalQueueResponse> {
  return apiGet<ArrivalQueueResponse>('/api/queue/today');
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useArrivalQueue() {
  return useQuery<ArrivalQueueResponse>({
    queryKey: ['arrival-queue-today'],
    queryFn: fetchArrivalQueue,
    // Auto-refresh every 5 seconds (UXR-103: queue must refresh within 5 s)
    refetchInterval: 5_000,
    // Always re-validate so the timer-driven view stays fresh
    staleTime: 0,
  });
}
