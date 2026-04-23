/**
 * agreementRateApi — typed API client functions for agreement rate endpoints.
 * (US_050, FR-067, FR-068, AIR-Q09)
 *
 * All endpoints are Admin-only (RBAC enforced by backend).
 * Auth token is injected by the shared apiGet helper from lib/apiClient.
 *
 * DTOs are intentionally snake_case to match the backend [JsonPropertyName]
 * attributes from AgreementRateModels.cs without any transformation layer.
 */

import { apiGet } from '@/lib/apiClient';

// ─── Response DTOs (match AgreementRateModels.cs) ─────────────────────────────

export interface AgreementRateDto {
  calculation_date:               string;   // "yyyy-MM-dd"
  daily_agreement_rate:           number;   // [0.0, 100.0] percentage
  rolling_30day_rate:             number | null;
  total_codes_verified:           number;
  codes_approved_without_override: number;
  codes_overridden:               number;
  codes_partially_overridden:     number;
  meets_minimum_threshold:        boolean;
  target_rate:                    number;   // always 98.0
}

export interface CodingDiscrepancyDto {
  discrepancy_id:         string;           // Guid
  patient_id:             string;           // Guid
  ai_suggested_code:      string;
  staff_selected_code:    string;
  code_type:              string;           // "ICD10" | "CPT"
  discrepancy_type:       string;           // "FullOverride" | "PartialOverride" | "MultipleCodes"
  override_justification: string | null;
  detected_at:            string;           // ISO-8601
}

export interface AgreementAlertDto {
  alert_date:             string;           // "yyyy-MM-dd"
  current_rate:           number;           // [0.0, 100.0]
  target_rate:            number;           // 98.0
  disagreement_patterns:  string[];
}

// ─── API Functions ─────────────────────────────────────────────────────────────

/**
 * GET /api/coding/agreement-rate
 * Returns the most-recent daily agreement-rate metric.
 * Resolves to null when the backend returns 404 (no data yet).
 */
export async function getLatestMetrics(): Promise<AgreementRateDto | null> {
  return apiGet<AgreementRateDto>('/api/coding/agreement-rate');
}

/**
 * GET /api/coding/agreement-rate/history?from={from}&to={to}
 * Date strings must be "yyyy-MM-dd".
 * Returns 422 from the server when the range exceeds 90 days.
 */
export async function getMetricsHistory(
  from: string,
  to: string,
): Promise<AgreementRateDto[]> {
  return apiGet<AgreementRateDto[]>(
    `/api/coding/agreement-rate/history?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
  );
}

/**
 * GET /api/coding/discrepancies?from={from}&to={to}
 * Both params are optional. Date strings "yyyy-MM-dd".
 * Returns 422 when date range exceeds 90 days.
 */
export async function getDiscrepancies(
  from?: string,
  to?: string,
): Promise<CodingDiscrepancyDto[]> {
  const params = new URLSearchParams();
  if (from) params.set('from', from);
  if (to)   params.set('to',   to);
  const qs = params.toString() ? `?${params.toString()}` : '';
  return apiGet<CodingDiscrepancyDto[]>(`/api/coding/discrepancies${qs}`);
}

/**
 * GET /api/coding/agreement-rate/alerts
 * Returns all below-threshold alert records ordered most-recent first.
 */
export async function getAlerts(): Promise<AgreementAlertDto[]> {
  return apiGet<AgreementAlertDto[]>('/api/coding/agreement-rate/alerts');
}
