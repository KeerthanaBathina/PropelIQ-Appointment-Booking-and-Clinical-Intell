/**
 * useStaffDashboard — React Query hook for the unified Staff Dashboard data feed (US_057).
 *
 * GET /api/staff/dashboard → StaffDashboardData
 *
 * Response contains:
 *   - stats: DashboardStats (today's appointments, queue count, pending reviews, completed)
 *   - schedule: ScheduleAppointment[] (today's schedule with status + no-show risk)
 *   - pendingTasks: PendingTask[] (unreviewed coding/conflict/document tasks)
 *
 * Polling: refetchInterval = 5 000 ms — satisfies AC-4 ("dashboard updates within 5 seconds").
 * staleTime is set just below the polling interval so every poll result is used.
 *
 * Query key: ['staff-dashboard']
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';
import type { StaffDashboardData } from '@/types/staffDashboard';

async function fetchDashboard(): Promise<StaffDashboardData> {
  return apiGet<StaffDashboardData>('/api/staff/dashboard');
}

export function useStaffDashboard() {
  return useQuery<StaffDashboardData>({
    queryKey:        ['staff-dashboard'],
    queryFn:         fetchDashboard,
    refetchInterval: 5_000,   // AC-4: real-time updates within 5 s
    staleTime:       4_000,   // treat data as fresh for 4 s to avoid double-renders
  });
}
