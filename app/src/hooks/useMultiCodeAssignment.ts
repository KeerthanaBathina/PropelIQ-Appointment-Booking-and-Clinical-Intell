/**
 * useMultiCodeAssignment — React Query mutation hook for multi-code assignment.
 * (US_051, AC-3, AC-4, FR-070)
 *
 * Mutations:
 *   assignMultipleCodes  → POST /api/coding/multi-assign
 *   validateBundling     → POST /api/coding/validate-bundling
 *
 * On successful assignment the payer-validation cache for the patient is
 * invalidated so the validation results refresh automatically.
 */

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiPost } from '@/lib/apiClient';
import { payerValidationKeys } from '@/hooks/usePayerValidation';
import type { BundlingRuleResultDto } from '@/hooks/usePayerValidation';

// ─── Request / Response DTOs ──────────────────────────────────────────────────

export interface CodeAssignmentEntry {
  code_value:     string;
  /** "ICD10" | "CPT" */
  code_type:      'ICD10' | 'CPT';
  description:    string;
  /** 1-based billing priority sequence. */
  sequence_order: number;
  justification?: string;
}

export interface MultiCodeAssignmentRequest {
  patient_id: string;
  codes:      CodeAssignmentEntry[];
}

export interface AssignedCodeDto {
  medical_code_id:     string;
  code_value:          string;
  code_type:           string;
  description:         string;
  sequence_order:      number;
  /** "Pending" | "Verified" | "Rejected" */
  verification_status: string;
}

export interface MultiCodeAssignmentResponse {
  patient_id:      string;
  assigned_codes:  AssignedCodeDto[];
  bundling_check:  BundlingRuleResultDto[];
}

export interface BundlingValidationRequest {
  patient_id:  string;
  code_values: string[];
}

export interface BundlingValidationResponse {
  patient_id:          string;
  bundling_violations: BundlingRuleResultDto[];
  is_valid:            boolean;
}

// ─── Hooks ────────────────────────────────────────────────────────────────────

/**
 * Assigns one or more codes to a patient encounter (AC-3).
 * Invalidates payer-validation cache on success so inline table badges refresh.
 */
export function useAssignMultipleCodes(patientId: string) {
  const queryClient = useQueryClient();

  const mutation = useMutation<
    MultiCodeAssignmentResponse,
    Error,
    MultiCodeAssignmentRequest
  >({
    mutationFn: (req) =>
      apiPost<MultiCodeAssignmentResponse>('/api/coding/multi-assign', req),
    onSuccess: () => {
      // Invalidate payer validation so table payer-status badges refresh
      void queryClient.invalidateQueries({
        queryKey: payerValidationKeys.byPatient(patientId),
      });
    },
  });

  return {
    assignCodes:  mutation.mutateAsync,
    isLoading:    mutation.isLoading,
    isError:      mutation.isError,
    error:        mutation.error,
    data:         mutation.data,
    reset:        mutation.reset,
  };
}

/**
 * Validates a complete code set against bundling rules (AC-4).
 * Call after all codes are verified before final submission.
 */
export function useValidateBundling(patientId: string) {
  const mutation = useMutation<
    BundlingValidationResponse,
    Error,
    BundlingValidationRequest
  >({
    mutationFn: (req) =>
      apiPost<BundlingValidationResponse>('/api/coding/validate-bundling', req),
  });

  return {
    validate:   mutation.mutateAsync,
    isLoading:  mutation.isLoading,
    isError:    mutation.isError,
    data:       mutation.data,
    reset:      mutation.reset,
    /** Shorthand to validate all provided code values for this patient. */
    validateCodes: (codeValues: string[]) =>
      mutation.mutateAsync({ patient_id: patientId, code_values: codeValues }),
  };
}
