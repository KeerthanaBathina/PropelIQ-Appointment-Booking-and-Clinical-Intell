/**
 * useClinicalDocumentParsingStatus (US_039 AC-2, AC-3, AC-5)
 *
 * Polls the backend for AI parsing status updates for a given set of document IDs.
 *
 * Polling strategy:
 *   - Runs only when `documentIds` is non-empty.
 *   - 10-second refetch interval — short enough to feel responsive during queue bursts,
 *     conservative enough to avoid hammering the API (NFR-005).
 *   - Stops polling automatically when all documents reach a terminal state
 *     (parsed, failed) so it does not run indefinitely after processing completes.
 *   - Pauses polling when the tab is hidden (TanStack Query window-focus behaviour).
 *
 * Returned `parsingStatusMap` is keyed by documentId so callers can look up the status
 * of any individual document in O(1).
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

/**
 * Processing pipeline states returned by the backend.
 *
 * Lifecycle: uploaded → queued → parsing → parsed | failed
 *
 * `uploaded`  — file stored; not yet enqueued (US_038 terminal state).
 * `queued`    — enqueued for AI processing (US_039).
 * `parsing`   — AI parser is actively extracting data (US_039 AC-2).
 * `parsed`    — extraction complete; results ready for review (US_039 AC-3).
 * `failed`    — all retry attempts exhausted (US_039 AC-5).
 */
export type ParsingStatus =
  | 'uploaded'
  | 'queued'
  | 'parsing'
  | 'parsed'
  | 'failed';

/** Terminal states — polling stops once all documents reach one of these. */
const TERMINAL_STATES = new Set<ParsingStatus>(['uploaded', 'parsed', 'failed']);

/** Per-document status row returned by GET /api/documents/:id/parsing-status */
export interface DocumentParsingStatusRow {
  documentId: string;
  status: ParsingStatus;
  /**
   * URL to the extracted results page — populated only when `status === 'parsed'`.
   * Used by the Review Results action (AC-3).
   */
  reviewUrl?: string;
  /** ISO 8601 timestamp of the most recent status transition. */
  updatedAt: string;
  /**
   * Set to `true` by the backend when Redis is unavailable and the system has
   * fallen back to synchronous processing (EC-1).
   */
  degradedMode?: boolean;
}

/** Batch response shape for GET /api/documents/parsing-status?ids=... */
interface ParsingStatusBatchResponse {
  documents: DocumentParsingStatusRow[];
}

export type ParsingStatusMap = Record<string, DocumentParsingStatusRow>;

// ─── Hook ─────────────────────────────────────────────────────────────────────

const POLL_INTERVAL_MS = 10_000;

/**
 * Polls parsing status for the supplied `documentIds`.
 *
 * @param documentIds  Document IDs to track (empty array disables polling).
 * @param patientId    Used to scope the API request; required by the backend endpoint.
 */
export function useClinicalDocumentParsingStatus(
  documentIds: string[],
  patientId: string,
): {
  parsingStatusMap: ParsingStatusMap;
  isPolling: boolean;
  degradedMode: boolean;
} {
  const enabled = documentIds.length > 0 && patientId.length > 0;

  const { data } = useQuery<ParsingStatusBatchResponse>({
    queryKey: ['documentParsingStatus', patientId, documentIds.join(',')],
    queryFn: () =>
      apiGet<ParsingStatusBatchResponse>(
        `/api/documents/parsing-status?patientId=${patientId}&ids=${documentIds.join(',')}`,
      ),
    enabled,
    // Refetch every 10s while any document is in a non-terminal state.
    refetchInterval: (query) => {
      const rows = query.state.data?.documents ?? [];
      const allTerminal =
        rows.length > 0 &&
        rows.every(r => TERMINAL_STATES.has(r.status));
      return allTerminal ? false : POLL_INTERVAL_MS;
    },
    // Keep stale data visible while a background refetch is in-flight.
    staleTime: POLL_INTERVAL_MS / 2,
  });

  const rows = data?.documents ?? [];

  const parsingStatusMap: ParsingStatusMap = Object.fromEntries(
    rows.map(r => [r.documentId, r]),
  );

  const allTerminal =
    rows.length > 0 && rows.every(r => TERMINAL_STATES.has(r.status));

  const degradedMode = rows.some(r => r.degradedMode === true);

  return {
    parsingStatusMap,
    isPolling: enabled && !allTerminal,
    degradedMode,
  };
}
