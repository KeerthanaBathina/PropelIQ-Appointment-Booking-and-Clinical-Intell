/**
 * usePayerValidation — React Query hook for payer rule validation results.
 * (US_051, AC-1, AC-2, FR-069, FR-070)
 *
 * GET /api/coding/payer-rules/{patientId}
 *   Returns validation results, claim denial risks, and bundling violations
 *   for a patient's assigned code set against their payer's rules.
 *   When payer rules are unavailable, the backend applies CMS default rules
 *   and sets is_manual_review_required = true (edge case).
 *
 * staleTime: 5 minutes per NFR-030 (payer rule sets are relatively static).
 * Error handling: loading/error/refetch exposed for all 5 screen states.
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet, apiPost } from '@/lib/apiClient';

// ─── DTOs (match backend PayerValidationModels.cs) ────────────────────────────

export interface CorrectiveActionDto {
  /** "AlternativeCode" | "AddModifier" | "DocumentationRequired" | "ManualReview" */
  action_type:        string;
  description:        string;
  /** Suggested replacement code value (when action_type = "AlternativeCode"). */
  suggested_code?:    string;
  /** Suggested modifier (e.g. "59", "25") when action_type = "AddModifier". */
  suggested_modifier?: string;
}

export interface PayerValidationResultDto {
  rule_id:            string;
  /** "error" = high denial risk; "warning" = review recommended; "info" = advisory */
  severity:           'error' | 'warning' | 'info';
  description:        string;
  /** Code values involved in this violation. */
  affected_codes:     string[];
  corrective_actions: CorrectiveActionDto[];
  /** True when payer-specific rules not found; CMS defaults applied instead. */
  is_cms_default:     boolean;
  payer_name?:        string;
}

export interface ClaimDenialRiskDto {
  /** Two or more code values forming the risky combination. */
  code_pair:             string[];
  /** "high" | "medium" | "low" */
  risk_level:            'high' | 'medium' | 'low';
  denial_reason:         string;
  /** 0.0–1.0 fraction of historical claims denied for this pair (optional). */
  historical_denial_rate?: number;
  corrective_actions:    CorrectiveActionDto[];
}

export interface BundlingRuleResultDto {
  column1_code:       string;
  column2_code:       string;
  /** CMS NCCI edit type (e.g. "Column1/Column2"). */
  cci_edit_type:      string;
  /** Modifier codes that allow separate billing (e.g. ["59", "X{EPSU}"]). */
  required_modifiers: string[];
  description:        string;
}

export interface PayerValidationResponse {
  patient_id:               string;
  payer_id:                 string;
  payer_name:               string;
  validation_results:       PayerValidationResultDto[];
  denial_risks:             ClaimDenialRiskDto[];
  bundling_violations:      BundlingRuleResultDto[];
  /** True when payer unknown — manual verification recommended. */
  is_manual_review_required: boolean;
}

// Request for POST /api/coding/resolve-conflict
export interface ConflictResolutionRequest {
  patient_id:          string;
  rule_id:             string;
  decision:            'UseClinicalCode' | 'UsePayerPreferredCode' | 'FlagForManualReview';
  clinical_code:       string;
  payer_preferred_code?: string;
  justification:       string;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const payerValidationKeys = {
  all:        ['payer-validation'] as const,
  byPatient:  (patientId: string) =>
    [...payerValidationKeys.all, patientId] as const,
};

// ─── Hooks ────────────────────────────────────────────────────────────────────

const STALE_5_MIN = 5 * 60 * 1_000;

/**
 * Fetches payer validation results for all codes assigned to the patient.
 * Returns null when the patient has no assigned codes (404 from server).
 */
export function usePayerValidation(patientId: string) {
  const enabled = patientId.length > 0;

  const query = useQuery<PayerValidationResponse | null, Error>({
    queryKey:  payerValidationKeys.byPatient(patientId),
    queryFn:   () =>
      apiGet<PayerValidationResponse>(`/api/coding/payer-rules/${encodeURIComponent(patientId)}`),
    staleTime: STALE_5_MIN,
    enabled,
    retry:     1,
  });

  // Derive a flat map: codeValue → worst severity for fast row-level lookup.
  const payerStatusByCode: Record<string, 'valid' | 'warning' | 'denial-risk'> = {};

  if (query.data) {
    // All codes start as valid unless there is a violation
    for (const result of query.data.validation_results) {
      for (const code of result.affected_codes) {
        const current   = payerStatusByCode[code];
        const incoming  = result.severity === 'error'   ? 'denial-risk'
                        : result.severity === 'warning' ? 'warning'
                        : 'valid';
        // Escalate: denial-risk > warning > valid
        if (!current || incoming === 'denial-risk' || (incoming === 'warning' && current === 'valid')) {
          payerStatusByCode[code] = incoming;
        }
      }
    }
    // Denial risks escalate to denial-risk
    for (const risk of query.data.denial_risks) {
      if (risk.risk_level === 'high') {
        for (const code of risk.code_pair) {
          payerStatusByCode[code] = 'denial-risk';
        }
      } else if (risk.risk_level === 'medium') {
        for (const code of risk.code_pair) {
          if (payerStatusByCode[code] !== 'denial-risk') {
            payerStatusByCode[code] = 'warning';
          }
        }
      }
    }
  }

  return {
    data:              query.data ?? null,
    isLoading:         query.isLoading && enabled,
    isError:           query.isError,
    refetch:           query.refetch,
    /** Per-code payer status map for inline table badges. */
    payerStatusByCode,
  };
}

/**
 * Sends a conflict resolution decision to the server.
 * Usage: call resolveConflict({ ... }) and await the promise.
 */
export async function resolveConflict(req: ConflictResolutionRequest): Promise<void> {
  await apiPost<void>('/api/coding/resolve-conflict', req);
}
