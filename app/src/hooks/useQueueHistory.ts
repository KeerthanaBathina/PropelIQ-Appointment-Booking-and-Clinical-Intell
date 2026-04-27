/**
 * useQueueHistory — React Query hook for queue history analytics (US_056 AC-3).
 *
 * GET /api/queue/history?startDate=YYYY-MM-DD&endDate=YYYY-MM-DD
 *
 * Returns daily metrics for the selected date range:
 *   - avgWaitTimeMinutes  — mean wait time across all entries for the day
 *   - noShowCount         — count of no-show entries
 *   - patientThroughput   — count of completed/in-visit patients
 *   - totalEntries        — total queue entries
 *
 * When the date range returns no data, the hook returns an empty metrics array so
 * the UI can display the "No data available for the selected period" empty state.
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

/** Per-day metrics row returned by GET /api/queue/history. */
export interface QueueDailyMetrics {
  /** YYYY-MM-DD date string */
  date:                 string;
  avgWaitTimeMinutes:   number;
  noShowCount:          number;
  patientThroughput:    number;
  totalEntries:         number;
}

/** Summary aggregate across the requested date range. */
export interface QueueHistorySummary {
  avgWaitTimeMinutes: number;
  noShowCount:        number;
  patientThroughput:  number;
  totalEntries:       number;
}

/** Full response from GET /api/queue/history. */
export interface QueueHistoryResponse {
  startDate: string;
  endDate:   string;
  metrics:   QueueDailyMetrics[];
  summary:   QueueHistorySummary;
}

// ─── API function ─────────────────────────────────────────────────────────────

async function fetchQueueHistory(
  startDate: string,
  endDate:   string,
): Promise<QueueHistoryResponse> {
  const params = new URLSearchParams({ startDate, endDate });
  return apiGet<QueueHistoryResponse>(`/api/queue/history?${params.toString()}`);
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

/**
 * Fetch queue history for a date range.
 *
 * @param startDate YYYY-MM-DD string
 * @param endDate   YYYY-MM-DD string
 *
 * Query is disabled when either date is empty.
 */
export function useQueueHistory(startDate: string, endDate: string) {
  const enabled = startDate.length > 0 && endDate.length > 0 && startDate <= endDate;

  return useQuery<QueueHistoryResponse>({
    queryKey: ['queue-history', startDate, endDate],
    queryFn:  () => fetchQueueHistory(startDate, endDate),
    enabled,
    staleTime: 60_000,      // history data is stable — revalidate after 1 min
    retry:     1,
  });
}
