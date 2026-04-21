/**
 * useStaffAppointments — React Query fetch hook for staff appointment list with
 * no-show risk scores (US_026, FR-014, task_004_be_no_show_risk_integration_api).
 *
 * GET /api/staff/appointments/today → array of StaffAppointment
 *
 * The backend resolves the provider's patient list server-side from the JWT.
 * noShowRiskScore and isRiskEstimated are populated by the risk integration
 * layer (task_004). A null score means the score has not yet been computed.
 *
 * staleTime: 60 s — risk scores are refreshed on the next staff page load or
 * explicit invalidation; EC-1 ("next staff refresh cycle") is satisfied without
 * requiring manual browser recalculation.
 *
 * Query key: ['staff-appointments-today']
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

export type StaffAppointmentStatus =
  | 'Scheduled'
  | 'Arrived'
  | 'InVisit'
  | 'Completed'
  | 'Cancelled'
  | 'NoShow';

export interface StaffAppointment {
  /** Appointment UUID */
  id: string;
  /** Booking reference BK-YYYYMMDD-XXXXXX */
  bookingReference: string | null;
  /** ISO 8601 UTC timestamp, e.g. "2026-04-21T10:00:00Z" */
  appointmentTime: string;
  patientName: string;
  providerName: string;
  appointmentType: string;
  status: StaffAppointmentStatus;
  /**
   * No-show risk score [0, 100] or null when not yet computed.
   * Backend clamps to 100 (EC-2).
   */
  noShowRiskScore: number | null;
  /**
   * True when the score was derived from rule-based defaults because the
   * patient has fewer than 3 prior appointments (AC-3).
   */
  isRiskEstimated: boolean;
  /** Position in the arrival queue (1-based); null when not queued. */
  queuePosition: number | null;
  /** How long the patient has been waiting in minutes; null when not arrived. */
  waitMinutes: number | null;
}

// ─── API fn ───────────────────────────────────────────────────────────────────

async function fetchStaffAppointments(): Promise<StaffAppointment[]> {
  return apiGet<StaffAppointment[]>('/api/staff/appointments/today');
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useStaffAppointments() {
  return useQuery<StaffAppointment[]>({
    queryKey: ['staff-appointments-today'],
    queryFn:  fetchStaffAppointments,
    // 60 s stale time — satisfies EC-1: updated score visible on next refresh
    // cycle without requiring manual browser recalculation.
    staleTime: 60_000,
  });
}
