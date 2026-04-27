/**
 * useAdminUsers — React Query hooks for admin user management (US_058 AC-3).
 *
 * useAdminUsers()            → GET /api/admin/users
 * useSetUserStatus()         → PUT /api/admin/users/:id/status
 * useInviteUser()            → POST /api/admin/users/invite
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiGet, apiPost, apiPut } from '@/lib/apiClient';
import type {
  AdminUsersResponse,
  AdminUserStatus,
  InviteUserRequest,
} from '@/types/adminConfig';

export function useAdminUsers() {
  return useQuery<AdminUsersResponse>({
    queryKey: ['admin-users'],
    queryFn:  () => apiGet('/api/admin/users'),
    staleTime: 60_000, // 1 min — user list changes infrequently
  });
}

export function useSetUserStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: AdminUserStatus }) =>
      apiPut<void>(`/api/admin/users/${id}/status`, { status }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-users'] }),
  });
}

export function useInviteUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: InviteUserRequest) =>
      apiPost<void>('/api/admin/users/invite', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-users'] }),
  });
}
