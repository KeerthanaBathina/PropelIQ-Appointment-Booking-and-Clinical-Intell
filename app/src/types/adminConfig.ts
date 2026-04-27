/**
 * TypeScript interfaces for the Admin Config & User Management API (US_058 AC-3, AC-4).
 *
 * Endpoints (task_004_be):
 *   GET/PUT  /api/admin/config/slots
 *   GET/PUT  /api/admin/config/notifications
 *   GET/PUT  /api/admin/config/hours
 *   GET/PUT  /api/admin/config/risk-thresholds
 *   GET      /api/admin/users
 *   PUT      /api/admin/users/:id/status
 *   POST     /api/admin/users/invite
 */

// ─── Slot Templates ───────────────────────────────────────────────────────────

export interface SlotCell {
  /** Day index 0=Mon … 4=Fri */
  day: number;
  /** Time label e.g. "9:00 AM" */
  time: string;
  available: boolean;
}

export interface ProviderSlotTemplate {
  providerId: string;
  providerName: string;
  slots: SlotCell[];
}

export interface SlotTemplatesConfig {
  providers: ProviderSlotTemplate[];
}

// ─── Notification Templates ───────────────────────────────────────────────────

export type NotificationChannel = 'Email' | 'SMS' | 'Email + SMS' | 'In-App';
export type NotificationStatus  = 'Active' | 'Draft' | 'Disabled';

/** Valid variable placeholders that may appear in notification bodies. */
export type TemplateVariableName = 'patient_name' | 'date' | 'time' | 'provider';

export interface TemplateVariable {
  name: TemplateVariableName;
  label: string;
  placeholder: string;  // e.g. "{{patient_name}}"
  sampleValue: string;
}

export const TEMPLATE_VARIABLES: TemplateVariable[] = [
  { name: 'patient_name', label: 'Patient Name', placeholder: '{{patient_name}}', sampleValue: 'Jane Doe'       },
  { name: 'date',         label: 'Date',         placeholder: '{{date}}',         sampleValue: 'May 12, 2026'   },
  { name: 'time',         label: 'Time',         placeholder: '{{time}}',         sampleValue: '2:30 PM'        },
  { name: 'provider',     label: 'Provider',     placeholder: '{{provider}}',     sampleValue: 'Dr. Emily Chen' },
];

export const ALLOWED_PLACEHOLDER_NAMES = new Set<string>(
  TEMPLATE_VARIABLES.map(v => v.name),
);

export interface NotificationTemplate {
  id: string;
  name: string;
  channel: NotificationChannel;
  trigger: string;
  status: NotificationStatus;
  /** Email subject line (empty for SMS). */
  subject?: string;
  bodyTemplate?: string;
  updatedAt?: string;
  updatedBy?: string;
}

export interface UpdateNotificationTemplateRequest {
  channel: NotificationChannel;
  subject?: string;
  bodyTemplate: string;
  status: NotificationStatus;
}

export interface NotificationTemplatesConfig {
  templates: NotificationTemplate[];
}

// ─── Business Hours & Holidays ────────────────────────────────────────────────

export interface DayHours {
  day: string;
  openTime: string | null;   // null = closed
  closeTime: string | null;
}

export type HolidayStatus = 'Closed' | 'Half Day';

export interface Holiday {
  id: string;
  name: string;
  date: string; // ISO date "YYYY-MM-DD"
  status: HolidayStatus;
}

export interface BusinessHoursConfig {
  regularHours: DayHours[];
  holidays: Holiday[];
}

// ─── Risk Thresholds ──────────────────────────────────────────────────────────

export interface RiskThresholdsConfig {
  /** Score at or above which a patient is flagged high-risk [0–100]. */
  highRiskThreshold: number;
  /** Score at or above which a patient is flagged medium-risk [0–100]. */
  mediumRiskThreshold: number;
  /**
   * Minimum number of historical appointments required before the AI model
   * score is used (below this the rule-based estimate is applied).
   */
  minAppointmentsForAiScore: number;
  /** Automatically send outreach SMS when high-risk flagged. */
  autoOutreach: boolean;
}

// ─── User Management ──────────────────────────────────────────────────────────

export type AdminUserRole   = 'Admin' | 'Staff';
export type AdminUserStatus = 'Active' | 'Inactive';

export interface AdminUser {
  id: string;
  fullName: string;
  email: string;
  role: AdminUserRole;
  roleSubtitle?: string;  // e.g. "Staff — Provider"
  status: AdminUserStatus;
  lastLoginAt: string | null; // ISO 8601 UTC
}

export interface AdminUsersResponse {
  users: AdminUser[];
}

export interface InviteUserRequest {
  email: string;
  role: AdminUserRole;
  fullName: string;
}

// ─── US_059 Slot Template API types ──────────────────────────────────────────

export interface SlotTemplateBlockDto {
  blockId?: string;
  /** Time string "HH:mm:ss", e.g. "09:00:00" */
  startTime: string;
  endTime: string;
  appointmentType: string;
  isAvailable: boolean;
}

export interface UpsertSlotTemplateRequest {
  /** Omit for a new template; include for optimistic-concurrency check. */
  version?: number;
  blocks: SlotTemplateBlockDto[];
}

export interface SlotTemplateResponse {
  slotTemplateId: string;
  providerId: string;
  /** 0 = Sunday … 6 = Saturday */
  dayOfWeek: number;
  version: number;
  createdAt: string;
  updatedAt: string;
  blocks: SlotTemplateBlockDto[];
}

export interface AffectedAppointmentDto {
  appointmentId: string;
  bookingReference: string | null;
  appointmentTime: string;
  providerName: string | null;
  appointmentType: string | null;
}

export interface AffectedAppointmentsResponse {
  count: number;
  appointments: AffectedAppointmentDto[];
}

// ─── US_059 Business Hours + Holidays API types ───────────────────────────────

export interface BusinessHoursEntryDto {
  businessHoursId: string;
  /** 0 = Sunday … 6 = Saturday */
  dayOfWeek: number;
  /** "HH:mm:ss" or null when closed */
  openTime: string | null;
  closeTime: string | null;
  isClosed: boolean;
}

export interface UpdateBusinessHoursRequest {
  entries: BusinessHoursEntryDto[];
}

export interface HolidayResponse {
  holidayId: string;
  /** ISO date "YYYY-MM-DD" */
  date: string;
  name: string;
  isRecurring: boolean;
  isHalfDay: boolean;
  createdAt: string;
}

export interface CreateHolidayRequest {
  /** ISO date "YYYY-MM-DD" */
  date: string;
  name: string;
  isRecurring: boolean;
  isHalfDay: boolean;
}

export interface AddHolidayResponse {
  holiday: HolidayResponse;
  affectedAppointmentCount: number;
  affectedAppointments: AffectedAppointmentDto[];
}
