/**
 * useExtractedDataVerification — US_041 AC-4, EC-2
 *
 * React Query hooks for:
 *   1. Fetching extracted data rows for a document or patient (with confidence scores).
 *   2. Single-item verification mutation — POST /api/extracted-data/:id/verify
 *   3. Bulk verification mutation — POST /api/extracted-data/bulk-verify
 *
 * After a successful mutation, the hook invalidates the extracted-data query for the
 * parent document/patient so verification counts and status chips refresh immediately.
 *
 * Error handling: surfaces ApiError to callers; no retry on mutations (idempotency
 * semantics assumed by the backend endpoint design).
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiGet, apiPost } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

/** Verification status stored on each extracted data row (US_041 AC-4). */
export type VerificationStatus = 'pending' | 'verified' | 'corrected';

/** Single extracted data row as returned by the review API. */
export interface ExtractedDataRow {
  extractedDataId: string;
  documentId:      string;
  dataType:        'Medication' | 'Diagnosis' | 'Procedure' | 'Allergy';
  /** Structured clinical data — shape varies by dataType. */
  dataContent:     Record<string, string | undefined>;
  /** Numeric confidence in [0, 1]. Null when the model could not assign a score (EC-1). */
  confidenceScore: number | null;
  /** True when the backend flagged this row for mandatory review (confidence < 0.80 or null). */
  flaggedForReview: boolean;
  verificationStatus: VerificationStatus;
  /** ISO 8601 timestamp of verification — null if not yet verified. */
  verifiedAt:      string | null;
  /** Display name of the verifying staff member — null if not yet verified. */
  verifiedByName:  string | null;
  /** Page number within the source document (US_040 AC-5). */
  pageNumber:      number;
  /** Coarse region within the page (US_040 AC-5). */
  extractionRegion: string;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const extractedDataKeys = {
  byDocument: (documentId: string) =>
    ['extracted-data', 'document', documentId] as const,
  byPatient: (patientId: string) =>
    ['extracted-data', 'patient', patientId] as const,
};

// ─── Fetch hooks ──────────────────────────────────────────────────────────────

/**
 * Fetches all extracted data rows for a specific document.
 * Used on SCR-012 post-parsing review.
 */
export function useExtractedDataByDocument(documentId: string | null | undefined) {
  return useQuery({
    queryKey: extractedDataKeys.byDocument(documentId ?? ''),
    queryFn: () =>
      apiGet<ExtractedDataRow[]>(`/api/extracted-data?documentId=${documentId}`),
    enabled: !!documentId,
    staleTime: 30_000,
  });
}

/**
 * Fetches all extracted data rows for a patient across all documents.
 * Used on SCR-013 profile tabs.
 */
export function useExtractedDataByPatient(patientId: string | null | undefined) {
  return useQuery({
    queryKey: extractedDataKeys.byPatient(patientId ?? ''),
    queryFn: () =>
      apiGet<ExtractedDataRow[]>(`/api/extracted-data?patientId=${patientId}`),
    enabled: !!patientId,
    staleTime: 30_000,
  });
}

/**
 * Fetches flagged item counts across a list of parsed document IDs in a single batch query.
 * Returns a map of documentId → flagged count. Used on SCR-012 to show the
 * confidence-review affordance next to each parsed document (US_041 AC-2).
 */
export function useDocumentsFlaggedCounts(documentIds: string[]) {
  return useQuery({
    queryKey: ['extracted-data', 'flagged-counts', documentIds] as const,
    queryFn: async () => {
      if (documentIds.length === 0) return {} as Record<string, number>;
      const params = documentIds.map(id => `documentIds=${encodeURIComponent(id)}`).join('&');
      const rows = await apiGet<ExtractedDataRow[]>(`/api/extracted-data/flagged-counts?${params}`);
      const map: Record<string, number> = {};
      for (const row of rows) {
        map[row.documentId] = (map[row.documentId] ?? 0) + 1;
      }
      return map;
    },
    enabled: documentIds.length > 0,
    staleTime: 30_000,
  });
}

// ─── Verification mutations ───────────────────────────────────────────────────

interface VerifyItemRequest {
  /** 'verified' when the staff member confirms the data as-is; 'corrected' after editing. */
  action: 'verified' | 'corrected';
}

interface VerifyItemResponse {
  extractedDataId:    string;
  verificationStatus: VerificationStatus;
  verifiedAt:         string;
  verifiedByName:     string;
}

/**
 * Single-item verification mutation (US_041 AC-4).
 * Invalidates extracted-data queries for the row's document and patient after success.
 */
export function useVerifyExtractedItem(documentId: string, patientId?: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ extractedDataId, action }: { extractedDataId: string } & VerifyItemRequest) =>
      apiPost<VerifyItemResponse>(
        `/api/extracted-data/${encodeURIComponent(extractedDataId)}/verify`,
        { action },
      ),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: extractedDataKeys.byDocument(documentId) });
      if (patientId) {
        queryClient.invalidateQueries({ queryKey: extractedDataKeys.byPatient(patientId) });
      }
    },
  });
}

interface BulkVerifyRequest {
  extractedDataIds: string[];
}

interface BulkVerifyResponse {
  verifiedCount: number;
  verifiedAt:    string;
  verifiedByName: string;
}

/**
 * Bulk verification mutation (US_041 EC-2).
 * Invalidates extracted-data queries for the source document and patient after success.
 */
export function useBulkVerifyExtractedData(documentId: string, patientId?: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: BulkVerifyRequest) =>
      apiPost<BulkVerifyResponse>('/api/extracted-data/bulk-verify', request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: extractedDataKeys.byDocument(documentId) });
      if (patientId) {
        queryClient.invalidateQueries({ queryKey: extractedDataKeys.byPatient(patientId) });
      }
    },
  });
}
