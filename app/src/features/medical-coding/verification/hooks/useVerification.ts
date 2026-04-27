/**
 * useVerification — React Query hooks for US_075 verification enforcement API.
 *
 * Endpoints (all under /api/staff/verification/):
 *   GET  pending?patientId=    → fetch pending VerificationItem list
 *   POST approve               → approve a single record
 *   POST modify                → modify + approve a single record
 *   POST reject                → reject a single record
 *   POST batch-approve         → approve multiple records in one request
 *   GET  audit-trail/{id}      → fetch VerificationAuditEntry list for one record
 *
 * Query key strategy:
 *   ['verification-enforcement', 'pending', patientId] — pending queue
 *   ['verification-enforcement', 'audit-trail', recordId] — per-item audit trail
 *
 * All mutations invalidate the pending queue on success so the UI stays in sync
 * without requiring a manual page refresh.
 *
 * staffUserId is NOT included in request bodies — the backend extracts it from
 * JWT Bearer claims on the server side.
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiGet, apiPost } from '@/lib/apiClient';
import type {
  BatchVerificationRequest,
  PendingVerificationResponse,
  VerificationAuditEntry,
  VerificationRequest,
} from '../types';

// ─── Query keys ───────────────────────────────────────────────────────────────

export const enforcementKeys = {
  pending:    (patientId: string) =>
    ['verification-enforcement', 'pending', patientId] as const,
  auditTrail: (recordId: string) =>
    ['verification-enforcement', 'audit-trail', recordId] as const,
};

// ─── Pending queue ────────────────────────────────────────────────────────────

export interface UseVerificationQueueReturn {
  items: PendingVerificationResponse | undefined;
  isLoading: boolean;
  isError: boolean;
  /** True when the error indicates the AI service is unavailable (HTTP 503). */
  isAiUnavailable: boolean;
  refetch: () => void;
}

export function useVerificationQueue(
  patientId: string | null | undefined,
): UseVerificationQueueReturn {
  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: enforcementKeys.pending(patientId ?? ''),
    queryFn:  () =>
      apiGet<PendingVerificationResponse>(
        `/api/staff/verification/pending?patientId=${patientId}`,
      ),
    enabled:   !!patientId,
    staleTime: 30_000,
  });

  // Treat HTTP 503 as AI-unavailable per UXR-605
  const isAiUnavailable =
    isError &&
    error !== null &&
    typeof error === 'object' &&
    'status' in error &&
    (error as { status: number }).status === 503;

  return { items: data, isLoading, isError, isAiUnavailable, refetch };
}

// ─── Approve single ───────────────────────────────────────────────────────────

export function useApproveVerification(patientId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: Pick<VerificationRequest, 'recordId' | 'recordType'>) =>
      apiPost<void>('/api/staff/verification/approve', request),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: enforcementKeys.pending(patientId),
      });
    },
  });
}

// ─── Modify + approve ─────────────────────────────────────────────────────────

export function useModifyVerification(patientId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: Required<VerificationRequest>) =>
      apiPost<void>('/api/staff/verification/modify', request),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: enforcementKeys.pending(patientId),
      });
    },
  });
}

// ─── Reject ───────────────────────────────────────────────────────────────────

export function useRejectVerification(patientId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: Pick<VerificationRequest, 'recordId' | 'recordType' | 'justification'>) =>
      apiPost<void>('/api/staff/verification/reject', request),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: enforcementKeys.pending(patientId),
      });
    },
  });
}

// ─── Batch approve ────────────────────────────────────────────────────────────

export function useBatchApprove(patientId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: BatchVerificationRequest) =>
      apiPost<void>('/api/staff/verification/batch-approve', request),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: enforcementKeys.pending(patientId),
      });
    },
  });
}

// ─── Audit trail (lazy — fetched only when expanded) ─────────────────────────

export interface UseVerificationAuditTrailReturn {
  entries: VerificationAuditEntry[];
  isLoading: boolean;
  isError: boolean;
}

export function useVerificationAuditTrail(
  recordId: string | null | undefined,
  enabled: boolean,
): UseVerificationAuditTrailReturn {
  const { data, isLoading, isError } = useQuery({
    queryKey: enforcementKeys.auditTrail(recordId ?? ''),
    queryFn:  () =>
      apiGet<VerificationAuditEntry[]>(
        `/api/staff/verification/audit-trail/${recordId}`,
      ),
    enabled:   !!recordId && enabled,
    staleTime: 60_000,
  });

  return { entries: data ?? [], isLoading, isError };
}
