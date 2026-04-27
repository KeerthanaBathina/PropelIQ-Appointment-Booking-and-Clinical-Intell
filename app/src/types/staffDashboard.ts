/**
 * TypeScript interfaces for the Staff Dashboard API response (US_057, SCR-010).
 * Consumed by useStaffDashboard hook and StaffDashboard sub-components.
 *
 * Endpoint: GET /api/staff/dashboard
 */

// ─── Stats ────────────────────────────────────────────────────────────────────

export interface DashboardStats {
  /** Total appointments scheduled for today (all statuses). */
  todayAppointments: number;
  /** Patients currently waiting in the arrival queue. */
  inQueue: number;
  /** Pending staff review tasks (coding + conflicts + document parsing). */
  pendingReviews: number;
  /** Appointments completed so far today. */
  completedToday: number;
}

// ─── Schedule ─────────────────────────────────────────────────────────────────

/** Extended appointment status visible to staff (superset of patient-visible statuses). */
export type ScheduleStatus =
  | 'Scheduled'
  | 'Arrived'
  | 'InVisit'
  | 'Waiting'
  | 'Completed'
  | 'Cancelled'
  | 'NoShow';

export interface ScheduleAppointment {
  /** Appointment UUID. */
  id: string;
  /** ISO 8601 UTC timestamp, e.g. "2026-04-24T10:00:00Z". */
  appointmentTime: string;
  /** Patient UUID (used for navigation links to SCR-013). */
  patientId: string;
  patientName: string;
  appointmentType: string;
  status: ScheduleStatus;
  /**
   * No-show risk score [0–100] or null when not yet computed.
   * Green <30, Amber 30–69, Red ≥70 (US_026 AC-2).
   */
  noShowRiskScore: number | null;
  /**
   * True when score is rule-based (patient has < 3 prior appointments).
   * UI renders "Est." prefix (US_026 AC-3).
   */
  isRiskEstimated: boolean;
}

// ─── Pending Tasks ────────────────────────────────────────────────────────────

/** Category determines the navigation target when a task is clicked (US_057 AC-3). */
export type PendingTaskCategory =
  | 'DocumentReview'    // → SCR-013 /staff/patients/:patientId
  | 'CodeApproval'      // → SCR-014 /staff/coding
  | 'ConflictResolution'; // → SCR-013 /staff/patients/:patientId

export interface PendingTask {
  /** Task UUID. */
  id: string;
  category: PendingTaskCategory;
  /** Patient UUID for building navigation URL. */
  patientId: string;
  patientName: string;
  /** Short description shown below the task category label. */
  description: string;
}

// ─── Root response ────────────────────────────────────────────────────────────

export interface StaffDashboardData {
  stats: DashboardStats;
  schedule: ScheduleAppointment[];
  pendingTasks: PendingTask[];
}
