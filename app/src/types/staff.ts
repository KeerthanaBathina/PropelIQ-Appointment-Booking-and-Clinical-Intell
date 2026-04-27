/**
 * TypeScript interfaces for the Staff Account Management API (US_061 AC-1 – AC-4).
 *
 * Endpoints:
 *   GET  /api/admin/users           — paginated/filtered staff list
 *   POST /api/admin/users           — create staff account with temp password + email invite
 *   PUT  /api/admin/users/:id/deactivate  — disable account (preserve history)
 *   PUT  /api/admin/users/:id/reactivate  — restore account with previous role/permissions
 */

import type { AdminUserRole, AdminUserStatus } from './adminConfig';

// ─── Re-export shared role/status literals ────────────────────────────────────

export type { AdminUserRole, AdminUserStatus };

// ─── Staff account record (AC-2 columns) ─────────────────────────────────────

export interface StaffAccount {
  id: string;
  fullName: string;
  email: string;
  role: AdminUserRole;
  /** Optional subtitle shown under name, e.g. "Staff — Provider". */
  roleSubtitle?: string;
  status: AdminUserStatus;
  /** ISO 8601 UTC — null when the user has never logged in. */
  lastLoginAt: string | null;
  /** ISO 8601 UTC — account creation timestamp. */
  createdAt: string;
}

// ─── List API ─────────────────────────────────────────────────────────────────

export interface StaffListFilter {
  /** Free-text search on name or email (client-side filter applied to loaded list). */
  search: string;
  /** Filter by role; empty string = all roles. */
  role: AdminUserRole | '';
  /** Filter by account status; empty string = all statuses. */
  status: AdminUserStatus | '';
}

export interface StaffListResponse {
  users: StaffAccount[];
  /** Total unfiltered count for pagination meta. */
  total: number;
}

// ─── Create API (AC-1) ────────────────────────────────────────────────────────

export interface CreateStaffRequest {
  fullName: string;
  email: string;
  role: AdminUserRole;
}

// ─── Status mutation API (AC-3, AC-4) ────────────────────────────────────────

/** Shape of the 409 / 422 error response body returned by deactivate guards. */
export interface StaffGuardError {
  /** Machine-readable code: "SELF_DEACTIVATION" | "LAST_ADMIN" */
  code: string;
  message: string;
}
