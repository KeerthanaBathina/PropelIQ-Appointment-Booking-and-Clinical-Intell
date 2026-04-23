/**
 * useAgreementRate — React Query hooks for agreement rate data.
 * (US_050, AC-2, AC-3, AC-4, FR-067, FR-068, NFR-030)
 *
 * staleTime: 5 minutes per NFR-030 (admin dashboard data caching requirement).
 * All hooks are Admin-only; the backend enforces RBAC — callers outside admin
 * context will receive a 403 which the shared apiGet interceptor handles.
 *
 * Hooks exported:
 *   useLatestAgreementRate()              — latest daily metric snapshot
 *   useAgreementRateHistory(from, to)     — date-range history for trend chart
 *   useDiscrepancies(from?, to?)          — discrepancy records
 *   useAgreementAlerts()                  — below-threshold alert list
 */

import { useQuery } from '@tanstack/react-query';
import {
  getLatestMetrics,
  getMetricsHistory,
  getDiscrepancies,
  getAlerts,
  type AgreementRateDto,
  type CodingDiscrepancyDto,
  type AgreementAlertDto,
} from '@/api/agreementRateApi';

// ─── Re-export DTOs so consumers only need one import location ─────────────────
export type { AgreementRateDto, CodingDiscrepancyDto, AgreementAlertDto };

// ─── Query key factory ─────────────────────────────────────────────────────────

export const agreementRateKeys = {
  all:          ['agreement-rate'] as const,
  latest:       () => [...agreementRateKeys.all, 'latest']           as const,
  history:      (from: string, to: string) =>
    [...agreementRateKeys.all, 'history', from, to]                  as const,
  discrepancies:(from?: string, to?: string) =>
    [...agreementRateKeys.all, 'discrepancies', from ?? '', to ?? ''] as const,
  alerts:       () => [...agreementRateKeys.all, 'alerts']           as const,
};

// ─── staleTime constant ───────────────────────────────────────────────────────

const STALE_5_MIN = 5 * 60 * 1_000; // 5 minutes (NFR-030)

// ─── Hooks ────────────────────────────────────────────────────────────────────

/**
 * Fetches the most-recently computed daily agreement-rate metric.
 * Returns `data = null` when no metric rows exist yet (404 from server).
 * Exposes loading / error / refetch for all 5 screen states (UXR-502, UXR-601).
 */
export function useLatestAgreementRate() {
  const query = useQuery<AgreementRateDto | null, Error>({
    queryKey:  agreementRateKeys.latest(),
    queryFn:   getLatestMetrics,
    staleTime: STALE_5_MIN,
    retry:     1,
  });

  return {
    data:      query.data ?? null,
    isLoading: query.isLoading,
    isError:   query.isError,
    refetch:   query.refetch,
  };
}

/**
 * Fetches agreement-rate history for a date range.
 * Only enabled when both `from` and `to` are non-empty.
 *
 * @param from - "yyyy-MM-dd" start date
 * @param to   - "yyyy-MM-dd" end date
 */
export function useAgreementRateHistory(from: string, to: string) {
  const enabled = from.length > 0 && to.length > 0;

  const query = useQuery<AgreementRateDto[], Error>({
    queryKey:  agreementRateKeys.history(from, to),
    queryFn:   () => getMetricsHistory(from, to),
    staleTime: STALE_5_MIN,
    enabled,
    retry:     1,
  });

  return {
    data:      query.data ?? [],
    isLoading: query.isLoading && enabled,
    isError:   query.isError,
    refetch:   query.refetch,
  };
}

/**
 * Fetches discrepancy records for an optional date range.
 *
 * @param from - optional "yyyy-MM-dd"
 * @param to   - optional "yyyy-MM-dd"
 */
export function useDiscrepancies(from?: string, to?: string) {
  const query = useQuery<CodingDiscrepancyDto[], Error>({
    queryKey:  agreementRateKeys.discrepancies(from, to),
    queryFn:   () => getDiscrepancies(from, to),
    staleTime: STALE_5_MIN,
    retry:     1,
  });

  return {
    data:      query.data ?? [],
    isLoading: query.isLoading,
    isError:   query.isError,
    refetch:   query.refetch,
  };
}

/**
 * Fetches all active below-threshold alert records.
 * Alerts are ordered most-recent first by the server.
 */
export function useAgreementAlerts() {
  const query = useQuery<AgreementAlertDto[], Error>({
    queryKey:  agreementRateKeys.alerts(),
    queryFn:   getAlerts,
    staleTime: STALE_5_MIN,
    retry:     1,
  });

  return {
    data:      query.data ?? [],
    isLoading: query.isLoading,
    isError:   query.isError,
    refetch:   query.refetch,
  };
}
