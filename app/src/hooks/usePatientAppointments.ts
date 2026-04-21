/**
 * usePatientAppointments — React Query fetch hook for patient appointment list (US_019).
 *
 * GET /api/appointments → array of PatientAppointment.
 *
 * The backend resolves the patient from the JWT — no patient ID in the request.
 * The `cancellable` flag is evaluated server-side against the 24-hour UTC window (EC-2).
 *
 * Query key: ['patient-appointments'] — invalidated by useCancelAppointment on success.
 */

import { useQuery } from '@tanstack/react-query';
import { apiGet } from '@/lib/apiClient';

// ─── Types ────────────────────────────────────────────────────────────────────

export type AppointmentStatus = 'Scheduled' | 'Completed' | 'Cancelled' | 'NoShow';

export interface PatientAppointment {
  /** Appointment UUID */
  id: string;
  /** Booking reference BK-YYYYMMDD-XXXXXX (AC-4) */
  bookingReference: string;
  /** ISO 8601 UTC timestamp e.g. "2026-04-21T10:00:00Z" */
  appointmentTime: string;
  providerName: string;
  appointmentType: string;
  status: AppointmentStatus;
  /**
   * True when the appointment may be cancelled (status=Scheduled AND
   * more than 24 hours remain before the appointment, evaluated in UTC by backend).
   * EC-2: NEVER re-compute this value on the client.
   */
  cancellable: boolean;
}

// ─── API fn ───────────────────────────────────────────────────────────────────

async function fetchPatientAppointments(): Promise<PatientAppointment[]> {
  return apiGet<PatientAppointment[]>('/api/appointments');
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function usePatientAppointments() {
  return useQuery<PatientAppointment[]>({
    queryKey: ['patient-appointments'],
    queryFn: fetchPatientAppointments,
    staleTime: 60_000, // 1 minute — appointment list doesn't change often
  });
}
