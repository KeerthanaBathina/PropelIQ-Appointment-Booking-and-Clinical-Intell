/**
 * useWaitThreshold — Fetches the configurable wait time alert threshold from the backend (US_055 AC-3).
 *
 * GET /api/queue/config/threshold → { thresholdMinutes: number }
 *
 * Polls every 30 seconds so that admin threshold changes propagate to all active
 * queue views within 30 seconds (AC-3 requirement).
 *
 * Syncs the resolved value to the Zustand queue store (`waitThresholdMinutes`) so that
 * all queue components share a single consistent threshold without prop-drilling.
 *
 * Falls back to DEFAULT_THRESHOLD (30 minutes) while loading or on error to ensure
 * the queue always has a sensible alert threshold.
 */

import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import { useQueueStore } from '@/stores/queueStore';

// ─── Types ────────────────────────────────────────────────────────────────────

interface ThresholdConfigResponse {
  /** Wait time alert threshold in whole minutes. */
  thresholdMinutes: number;
}

// ─── Constants ────────────────────────────────────────────────────────────────

/** Fallback threshold used while the API response is loading or when an error occurs. */
const DEFAULT_THRESHOLD = 30;

// ─── API fn ───────────────────────────────────────────────────────────────────

async function fetchThresholdConfig(): Promise<ThresholdConfigResponse> {
  return apiGet<ThresholdConfigResponse>('/api/queue/config/threshold');
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useWaitThreshold() {
  const setWaitThreshold = useQueueStore((s) => s.setWaitThreshold);

  const { data, isLoading, error } = useQuery<ThresholdConfigResponse>({
    queryKey: ['queue-threshold-config'],
    queryFn: fetchThresholdConfig,
    // Poll every 30 s so admin config changes propagate within 30 seconds (AC-3).
    refetchInterval: 30_000,
    // Treat data as fresh for 25 s to avoid redundant background fetches.
    staleTime: 25_000,
  });

  const thresholdMinutes = data?.thresholdMinutes ?? DEFAULT_THRESHOLD;

  // Sync to Zustand store so components that subscribe to the store stay in sync.
  useEffect(() => {
    setWaitThreshold(thresholdMinutes);
  }, [thresholdMinutes, setWaitThreshold]);

  return {
    thresholdMinutes,
    isLoading,
    error,
  };
}
