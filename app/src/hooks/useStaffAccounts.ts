/**
 * useStaffAccounts — React Query hooks for staff account management (US_061 AC-1 – AC-4).
 *
 * useStaffAccounts()       → GET  /api/admin/users
 * useCreateStaff()         → POST /api/admin/users
 * useDeactivateStaff()     → PUT  /api/admin/users/:id/deactivate
 * useReactivateStaff()     → PUT  /api/admin/users/:id/reactivate
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiGet, apiPost, apiPut } from '@/lib/apiClient';
import type { CreateStaffRequest, StaffListResponse } from '@/types/staff';

export const STAFF_QUERY_KEY = ['admin-staff-accounts'] as const;

// ─── List ─────────────────────────────────────────────────────────────────────

export function useStaffAccounts() {
  return useQuery<StaffListResponse>({
    queryKey: STAFF_QUERY_KEY,
    queryFn:  () => apiGet('/api/admin/users'),
    staleTime: 60_000, // 1 min — user list changes infrequently
  });
}

// ─── Create (AC-1) ────────────────────────────────────────────────────────────

export function useCreateStaff() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateStaffRequest) =>
      apiPost<void>('/api/admin/users', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: STAFF_QUERY_KEY }),
  });
}

// ─── Deactivate (AC-3) ───────────────────────────────────────────────────────

export function useDeactivateStaff() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiPut<void>(`/api/admin/users/${id}/deactivate`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: STAFF_QUERY_KEY }),
  });
}

// ─── Reactivate (AC-4) ───────────────────────────────────────────────────────

export function useReactivateStaff() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiPut<void>(`/api/admin/users/${id}/reactivate`, {}),
    onSuccess: () => qc.invalidateQueries({ queryKey: STAFF_QUERY_KEY }),
  });
}
