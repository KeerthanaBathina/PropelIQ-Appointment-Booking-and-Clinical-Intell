/**
 * useCodeSearch — debounced React Query hook for code library search
 * (US_049 AC-3, CodeOverrideModal).
 *
 * GET /api/codes/search?query={query}&type={codeType}&limit={limit}
 *
 * Debounce: 300 ms — prevents API calls on every keystroke.
 * Minimum query length: 2 characters before the API call is enabled.
 *
 * Query key: ['code-search', debouncedQuery, codeType]
 * Stale time: 60 s — search results are stable enough to cache briefly.
 */

import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import { verificationKeys } from './useVerificationQueue';

// ─── DTOs ─────────────────────────────────────────────────────────────────────

export interface CodeSearchResult {
  code_value:  string;
  description: string;
  category:    string;
}

// ─── Constants ────────────────────────────────────────────────────────────────

const DEBOUNCE_MS      = 300;
const MIN_QUERY_LENGTH = 2;

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseCodeSearchReturn {
  results:    CodeSearchResult[];
  isLoading:  boolean;
}

export function useCodeSearch(
  query:    string,
  codeType: string,
  limit     = 20,
): UseCodeSearchReturn {
  const [debouncedQuery, setDebouncedQuery] = useState(query);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedQuery(query), DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [query]);

  const trimmed = debouncedQuery.trim();
  const enabled = trimmed.length >= MIN_QUERY_LENGTH;

  const params = new URLSearchParams({
    query: trimmed,
    type:  codeType,
    limit: String(limit),
  });

  const { data, isLoading } = useQuery({
    queryKey: verificationKeys.search(trimmed, codeType),
    queryFn:  () => apiGet<CodeSearchResult[]>(`/api/codes/search?${params.toString()}`),
    enabled,
    staleTime: 60_000,
  });

  return {
    results:   data ?? [],
    isLoading: isLoading && enabled,
  };
}
