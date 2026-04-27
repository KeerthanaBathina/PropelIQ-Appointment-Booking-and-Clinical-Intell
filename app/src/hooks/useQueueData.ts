/**
 * useQueueData — React Query hook for the real-time arrival queue dashboard (US_053).
 *
 * GET /api/queue/today?provider=&status=&page=N → QueueDataResponse
 *
 * Supports:
 *   - Provider filter (string, "all" = no filter)
 *   - Status filter  (string, "all" = no filter)
 *   - Page number    (1-based, 25 items per page — EC-1)
 *
 * Auto-refreshes every 5 seconds per UXR-103 (queue must refresh within 5 s).
 * Exposes `dataUpdatedAt` for the "Last updated" timestamp (EC-2).
 * Exposes `refetch` for the manual refresh button (EC-2).
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

export type QueueStatus =
  | 'waiting'
  | 'in_visit'
  | 'completed'
  | 'no_show'
  | 'arrived_late'
  | 'scheduled'
  | 'cancelled';

export type QueuePriority = 'normal' | 'urgent';

export interface QueueEntry {
  queueId:          string;
  appointmentId:    string;
  patientName:      string;
  /** ISO-8601 UTC appointment time */
  appointmentTime:  string;
  /** ISO-8601 UTC arrival timestamp; null when not yet arrived */
  arrivalTimestamp: string | null;
  /** Server-computed wait time in whole minutes */
  waitTimeMinutes:  number;
  priority:         QueuePriority;
  status:           QueueStatus;
  appointmentStatus: string;
  queuePosition:    number;
  /** Provider display name; null for walk-ins */
  providerName:     string | null;
  /** Appointment type label, e.g. "Checkup", "Follow-up" */
  appointmentType:  string | null;
  // ── US_055 — Auto no-show detection fields ────────────────────────────────
  /** True when the no-show status was set automatically by the system (US_055 AC-2). */
  isAutoNoShow?:        boolean;
  /** True when the auto no-show was detected after the standard window (delayed detection edge-case). */
  isDelayedDetection?:  boolean;
}

export interface QueueDataResponse {
  data:       QueueEntry[];
  totalCount: number;
}

export interface QueueFilters {
  provider:        string;   // "all" or provider name
  appointmentType: string;   // "all" or appointment type label (US_056 AC-2)
  status:          string;   // "all" or QueueStatus value
  page:            number;   // 1-based
}

export const PAGE_SIZE = 25; // EC-1 — 25 entries per page

// ─── API function ─────────────────────────────────────────────────────────────

async function fetchQueueData(filters: QueueFilters): Promise<QueueDataResponse> {
  const params = new URLSearchParams();
  if (filters.provider        !== 'all') params.set('provider',        filters.provider);
  if (filters.appointmentType !== 'all') params.set('appointmentType', filters.appointmentType);
  if (filters.status          !== 'all') params.set('status',          filters.status);
  params.set('page', String(filters.page));

  const qs = params.toString();
  return apiGet<QueueDataResponse>(`/api/queue/today${qs ? `?${qs}` : ''}`);
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useQueueData(filters: QueueFilters) {
  return useQuery<QueueDataResponse>({
    queryKey: ['queue-dashboard', filters.provider, filters.appointmentType, filters.status, filters.page],
    queryFn:  () => fetchQueueData(filters),
    // Auto-refresh every 5 seconds (UXR-103)
    refetchInterval: 5_000,
    // Always re-validate on window focus for real-time accuracy
    staleTime: 0,
    // Keep previous page data visible while the next page loads
    placeholderData: (prev) => prev,
  });
}
