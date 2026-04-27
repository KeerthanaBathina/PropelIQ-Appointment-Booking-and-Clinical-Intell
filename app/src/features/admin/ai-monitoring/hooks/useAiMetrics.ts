/**
 * React Query hooks for the AI Monitoring Dashboard (US_072).
 *
 * useAiMetricsSummary():    GET /api/admin/ai-metrics/summary    staleTime=60s
 * useAiMetricsTimeSeries(): GET /api/admin/ai-metrics/time-series
 * useAiMetricAlerts():      GET /api/admin/ai-metrics/alerts
 * useAcknowledgeAlert():    PUT /api/admin/ai-metrics/alerts/{id}/acknowledge
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiGet, apiPut } from '@/lib/apiClient';
import type {
  AiMetricAlert,
  AiMetricsSummary,
  AiMetricsTimeSeries,
  Granularity,
  AccuracyMetricType,
} from '../types';

// ─── Query keys ──────────────────────────────────────────────────────────────

export const AI_METRICS_QUERY_KEYS = {
  summary:    ['ai-metrics-summary']    as const,
  alerts:     ['ai-metrics-alerts']     as const,
  timeSeries: (metricType: string, startDate: string, endDate: string, granularity: string) =>
    ['ai-metrics-time-series', metricType, startDate, endDate, granularity] as const,
};

// ─── Constants ────────────────────────────────────────────────────────────────

const SIXTY_SECONDS = 60 * 1_000;

// ─── Fetchers ─────────────────────────────────────────────────────────────────

async function fetchSummary(): Promise<AiMetricsSummary> {
  return apiGet<AiMetricsSummary>('/api/admin/ai-metrics/summary');
}

async function fetchTimeSeries(
  metricType: AccuracyMetricType,
  startDate: string,
  endDate: string,
  granularity: Granularity,
): Promise<AiMetricsTimeSeries> {
  const params = new URLSearchParams({
    metricType,
    startDate,
    endDate,
    granularity,
  });
  return apiGet<AiMetricsTimeSeries>(`/api/admin/ai-metrics/time-series?${params.toString()}`);
}

async function fetchAlerts(): Promise<AiMetricAlert[]> {
  return apiGet<AiMetricAlert[]>('/api/admin/ai-metrics/alerts');
}

async function acknowledgeAlert(alertId: string): Promise<void> {
  return apiPut<void>(`/api/admin/ai-metrics/alerts/${alertId}/acknowledge`, {});
}

// ─── Hooks ────────────────────────────────────────────────────────────────────

export function useAiMetricsSummary() {
  return useQuery<AiMetricsSummary>({
    queryKey:  AI_METRICS_QUERY_KEYS.summary,
    queryFn:   fetchSummary,
    staleTime: SIXTY_SECONDS,
  });
}

export function useAiMetricsTimeSeries(
  metricType: AccuracyMetricType,
  startDate: string,
  endDate: string,
  granularity: Granularity,
) {
  return useQuery<AiMetricsTimeSeries>({
    queryKey: AI_METRICS_QUERY_KEYS.timeSeries(metricType, startDate, endDate, granularity),
    queryFn:  () => fetchTimeSeries(metricType, startDate, endDate, granularity),
    staleTime: SIXTY_SECONDS,
    enabled:   Boolean(metricType && startDate && endDate),
  });
}

export function useAiMetricAlerts() {
  return useQuery<AiMetricAlert[]>({
    queryKey:  AI_METRICS_QUERY_KEYS.alerts,
    queryFn:   fetchAlerts,
    staleTime: SIXTY_SECONDS,
  });
}

export function useAcknowledgeAlert() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, string>({
    mutationFn: acknowledgeAlert,
    onSuccess: () => {
      // Invalidate both alerts list and summary (activeAlertCount changes)
      void queryClient.invalidateQueries({ queryKey: AI_METRICS_QUERY_KEYS.alerts });
      void queryClient.invalidateQueries({ queryKey: AI_METRICS_QUERY_KEYS.summary });
    },
  });
}
