/**
 * usePatientSearch — React Query hook for SCR-016 Patient Search (US_062 AC-1, AC-2).
 *
 * GET /api/staff/patients/search
 *   ?q=<term>&provider=<id>&status=<Active|Inactive|All>&page=<n>&pageSize=<n>
 *   → PatientSearchResponse
 *
 * Design decisions:
 *   - Query is disabled when the search term is empty and no filters are active,
 *     so the page stays in its "Default / no results yet" state on first load.
 *   - Query IS enabled when provider or status filters are set even with an empty
 *     term, so staff can browse by provider/status without typing (AC-1).
 *   - staleTime: 30 s — short-lived reference data; aligns with backend Redis TTL.
 *   - retry: 1 — surfaces genuine API errors quickly without hiding transient blips.
 *   - Partial match highlighting is handled in the component layer (see PatientSearchPage)
 *     by splitting/wrapping matched segments; the API simply returns full names.
 *
 * Query key: ['patient-search', params]
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet, ApiError } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

/** Status filter for the patient search form. */
export type PatientStatusFilter = 'All' | 'Active' | 'Inactive';

/** Parameters for the patient search API (AC-1). */
export interface PatientSearchParams {
  /** Free-text search term (name, MRN, DOB, phone, or email). */
  term: string;
  /** Filter by assigned provider. Empty string = all providers. */
  provider?: string;
  /** Filter by account status. Default 'All'. */
  status?: PatientStatusFilter;
  /** 1-based page number. Default 1. */
  page?: number;
  /** Page size. Default 20. */
  pageSize?: number;
}

/** A single row in the patient search results table (AC-2). */
export interface PatientSearchRow {
  /** UUID patient identifier used to navigate to the profile (AC-2). */
  patientId: string;
  /** Full display name (may be highlighted by the component layer). */
  fullName: string;
  /** Medical Record Number, e.g. "MRN-00412". */
  mrn: string;
  /** ISO-8601 date of birth string (YYYY-MM-DD). */
  dateOfBirth: string;
  /** Formatted phone number, e.g. "(555) 123-4567". */
  phone: string;
  /** Assigned provider display name, e.g. "Dr. Chen". */
  provider: string;
  /** ISO-8601 datetime of the most recent appointment, or null if none. */
  lastVisitAt: string | null;
  /** Account status — "Active" renders with success chip; "Inactive" with neutral chip + 0.7 opacity. */
  status: 'Active' | 'Inactive';
}

/** Paginated response from GET /api/staff/patients/search. */
export interface PatientSearchResponse {
  patients: PatientSearchRow[];
  /** Total number of matching patients (used to compute page count). */
  total: number;
  page: number;
  pageSize: number;
}

/** Available provider option returned by GET /api/staff/providers/list. */
export interface ProviderOption {
  id: string;
  displayName: string;
}

// ─── Query keys ───────────────────────────────────────────────────────────────

export const patientSearchKeys = {
  search: (params: PatientSearchParams) =>
    ['patient-search', params] as const,
  providers: () =>
    ['staff-provider-list'] as const,
};

// ─── Constants ────────────────────────────────────────────────────────────────

const STALE_TIME_MS    = 30_000; // 30 seconds
const PROVIDER_STALE   = 5 * 60_000; // 5 minutes (provider list changes rarely)

// ─── Fetch fns ────────────────────────────────────────────────────────────────

async function fetchPatients(
  params: PatientSearchParams,
): Promise<PatientSearchResponse> {
  const qs = new URLSearchParams();
  if (params.term.trim()) qs.set('q', params.term.trim());
  if (params.provider)    qs.set('provider', params.provider);
  if (params.status && params.status !== 'All') qs.set('status', params.status);
  qs.set('page',     String(params.page     ?? 1));
  qs.set('pageSize', String(params.pageSize ?? 20));
  return apiGet<PatientSearchResponse>(`/api/staff/patients/search?${qs.toString()}`);
}

async function fetchProviders(): Promise<ProviderOption[]> {
  return apiGet<ProviderOption[]>('/api/staff/providers/list');
}

// ─── Hooks ────────────────────────────────────────────────────────────────────

/**
 * Primary search hook (AC-1).  Active when any search term or filter is present.
 */
export function usePatientSearch(params: PatientSearchParams) {
  return useQuery<PatientSearchResponse, ApiError>({
    queryKey:  patientSearchKeys.search(params),
    queryFn:   () => fetchPatients(params),
    enabled:   true,  // always run — shows all patients when no filter is set
    staleTime: STALE_TIME_MS,
    retry:     1,
  });
}

/**
 * Provider list hook — used to populate the "Provider" dropdown filter.
 * Long staleTime since the provider list changes infrequently.
 */
export function useProviderList() {
  return useQuery<ProviderOption[], ApiError>({
    queryKey:  patientSearchKeys.providers(),
    queryFn:   fetchProviders,
    staleTime: PROVIDER_STALE,
    retry:     1,
  });
}
