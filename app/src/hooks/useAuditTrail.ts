/**
 * useAuditTrail — React Query fetch hook for the immutable per-code audit trail
 * (US_049 AC-4, FR-049).
 *
 * GET /api/codes/{codeId}/audit-trail
 *
 * Enabled only when codeId is provided (lazy — fetched on row expansion).
 *
 * Query key: ['code-audit-trail', codeId]
 * Stale time: 30 s — audit entries are append-only so caching is safe.
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import { verificationKeys } from './useVerificationQueue';

// ─── DTOs ─────────────────────────────────────────────────────────────────────

export interface CodingAuditEntry {
  log_id:         string;
  action:         string;  // "Approved" | "Overridden" | "DeprecatedBlocked" | "Revalidated"
  old_code_value: string;
  new_code_value: string;
  justification:  string | null;
  user_id:        string | null;
  /** ISO 8601 timestamp string — entries ordered descending (newest first). */
  timestamp:      string;
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseAuditTrailReturn {
  entries:   CodingAuditEntry[];
  isLoading: boolean;
}

export function useAuditTrail(
  codeId: string | null | undefined,
): UseAuditTrailReturn {
  const { data, isLoading } = useQuery({
    queryKey: verificationKeys.auditTrail(codeId ?? ''),
    queryFn:  () => apiGet<CodingAuditEntry[]>(`/api/codes/${codeId}/audit-trail`),
    enabled:  !!codeId,
    staleTime: 30_000,
  });

  return {
    entries:   data ?? [],
    isLoading,
  };
}
