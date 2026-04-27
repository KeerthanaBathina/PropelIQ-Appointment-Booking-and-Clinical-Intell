/**
 * useAdminMetrics — React Query hooks for the Admin Metrics Dashboard (US_058, SCR-015).
 *
 * useAdminMetrics():
 *   GET /api/admin/metrics — current snapshot of the 5 KPIs.
 *   staleTime = 5 min (matches Redis TTL on the backend).
 *   On error the hook returns the last successful data (React Query default).
 *
 * useAdminMetricsTrends(period):
 *   GET /api/admin/metrics/trends?period=7d|30d — rolling trend series.
 *   staleTime = 5 min.
 *
 * Query keys: ['admin-metrics'], ['admin-metrics-trends', period]
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import type {
  AdminMetricsResponse,
  AdminMetricsTrendsResponse,
  TrendPeriod,
} from '@/types/adminMetrics';

// ─── Fetchers ─────────────────────────────────────────────────────────────────

async function fetchAdminMetrics(): Promise<AdminMetricsResponse> {
  return apiGet<AdminMetricsResponse>('/api/admin/metrics');
}

async function fetchAdminMetricsTrends(period: TrendPeriod): Promise<AdminMetricsTrendsResponse> {
  return apiGet<AdminMetricsTrendsResponse>(`/api/admin/metrics/trends?period=${period}`);
}

// ─── Hooks ────────────────────────────────────────────────────────────────────

const FIVE_MINUTES = 5 * 60 * 1_000;

export function useAdminMetrics() {
  return useQuery<AdminMetricsResponse>({
    queryKey: ['admin-metrics'],
    queryFn:  fetchAdminMetrics,
    staleTime: FIVE_MINUTES,
    // Keep previous data visible while reloading (AC-1 stale indicator)
    keepPreviousData: true,
  });
}

export function useAdminMetricsTrends(period: TrendPeriod) {
  return useQuery<AdminMetricsTrendsResponse>({
    queryKey: ['admin-metrics-trends', period],
    queryFn:  () => fetchAdminMetricsTrends(period),
    staleTime: FIVE_MINUTES,
    keepPreviousData: true,
  });
}
