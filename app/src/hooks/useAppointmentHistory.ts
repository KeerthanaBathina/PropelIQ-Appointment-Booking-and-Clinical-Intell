/**
 * useAppointmentHistory — React Query fetch hook for the patient appointment
 * history page (US_024, FR-024).
 *
 * GET /api/appointments → PatientAppointment[]
 *   The same endpoint used by the patient dashboard; the full list is returned
 *   and pagination + sorting are handled client-side so a dedicated history
 *   endpoint is not required until US_024 task_002 (BE) ships.
 *
 * Client-side responsibilities:
 *   - Default sort: newest-first (descending appointmentTime) matching AC-1.
 *   - Toggleable sort direction for the date column (AC-2).
 *   - Page size: 10 items per page (AC-3).
 *
 * Query key: ['patient-appointments'] — shared with useCancelAppointment and
 * useRescheduleAppointment so history auto-refreshes after mutations.
 */

import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import type { PatientAppointment } from './usePatientAppointments';

// ─── Constants ────────────────────────────────────────────────────────────────

export const PAGE_SIZE = 10;

// ─── Sort direction ───────────────────────────────────────────────────────────

export type SortDirection = 'asc' | 'desc';

// ─── API fn ───────────────────────────────────────────────────────────────────

async function fetchAppointmentHistory(): Promise<PatientAppointment[]> {
  return apiGet<PatientAppointment[]>('/api/appointments');
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export interface UseAppointmentHistoryReturn {
  /** Appointments for the current page, sorted per sortDirection. */
  pagedAppointments: PatientAppointment[];
  /** Total number of appointments (all statuses). */
  totalCount: number;
  /** Total number of pages at PAGE_SIZE = 10. */
  totalPages: number;
  /** Current 1-based page number. */
  page: number;
  /** Set the current page (clamped to valid range). */
  setPage: (p: number) => void;
  /** Current sort direction for the date column. */
  sortDirection: SortDirection;
  /** Toggle the date sort direction (desc ↔ asc). */
  toggleSort: () => void;
  isLoading: boolean;
  isError: boolean;
}

export function useAppointmentHistory(): UseAppointmentHistoryReturn {
  const [page, setPageRaw] = useState(1);
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc');

  const { data, isLoading, isError } = useQuery<PatientAppointment[]>({
    queryKey: ['patient-appointments'],
    queryFn:  fetchAppointmentHistory,
    staleTime: 60_000,
  });

  // ─── Sort + paginate (all client-side until BE pagination lands) ──────────
  const allAppointments = data ?? [];

  const sorted = useMemo(() => {
    return [...allAppointments].sort((a, b) => {
      const diff =
        new Date(a.appointmentTime).getTime() -
        new Date(b.appointmentTime).getTime();
      return sortDirection === 'desc' ? -diff : diff;
    });
  }, [allAppointments, sortDirection]);

  const totalCount = sorted.length;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const pagedAppointments = useMemo(() => {
    const start = (page - 1) * PAGE_SIZE;
    return sorted.slice(start, start + PAGE_SIZE);
  }, [sorted, page]);

  const setPage = (p: number) => {
    const clamped = Math.max(1, Math.min(p, totalPages));
    setPageRaw(clamped);
  };

  const toggleSort = () => {
    setSortDirection((prev) => (prev === 'desc' ? 'asc' : 'desc'));
    setPageRaw(1); // reset to page 1 when sort changes
  };

  return {
    pagedAppointments,
    totalCount,
    totalPages,
    page,
    setPage,
    sortDirection,
    toggleSort,
    isLoading,
    isError,
  };
}
