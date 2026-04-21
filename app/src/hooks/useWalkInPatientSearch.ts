/**
 * useWalkInPatientSearch — React Query hook for staff patient search (US_022 AC-2).
 *
 * GET /api/staff/patients/search?q=<term>&field=<name|dob|phone>
 *   → PatientSearchResult[]
 *
 * Design decisions:
 *   - Query is disabled when the search term is fewer than 2 characters to
 *     prevent unnecessary round-trips and accidental full-table scans.
 *   - staleTime: 30 seconds — patient search results are short-lived reference
 *     data that staff members expect to be current during a session.
 *   - Returns full error to callers so the modal can surface staff-only
 *     restriction (403) and other recoverable API errors (UXR-601, EC-1).
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet, ApiError } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

/** Field by which to constrain the patient search. */
export type PatientSearchField = 'name' | 'dob' | 'phone';

/** A single patient match returned by the patient search endpoint (AC-2). */
export interface PatientSearchResult {
  /** Unique patient identifier. */
  patientId: string;
  /** Full display name. */
  fullName: string;
  /** ISO-8601 date of birth (YYYY-MM-DD). */
  dateOfBirth: string;
  /** Formatted phone number. */
  phone: string;
  /** Patient email address. */
  email: string;
}

export interface PatientSearchParams {
  /** Search term typed by the staff member. */
  term: string;
  /** Which field to search against. Defaults to name. */
  field?: PatientSearchField;
}

// ─── Constants ────────────────────────────────────────────────────────────────

const MIN_TERM_LENGTH = 2;
const STALE_TIME_MS   = 30_000; // 30 seconds

// ─── Fetch fn ─────────────────────────────────────────────────────────────────

async function searchPatients(
  params: Required<PatientSearchParams>,
): Promise<PatientSearchResult[]> {
  const qs = new URLSearchParams({
    q:     params.term,
    field: params.field,
  }).toString();
  return apiGet<PatientSearchResult[]>(`/api/staff/patients/search?${qs}`);
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useWalkInPatientSearch(params: PatientSearchParams) {
  const field: PatientSearchField = params.field ?? 'name';
  const enabled = params.term.trim().length >= MIN_TERM_LENGTH;

  return useQuery<PatientSearchResult[], ApiError>({
    queryKey:  ['walkInPatientSearch', params.term.trim(), field],
    queryFn:   () => searchPatients({ term: params.term.trim(), field }),
    enabled,
    staleTime: STALE_TIME_MS,
    retry:     false, // surface 403 staff-only restriction immediately
  });
}
