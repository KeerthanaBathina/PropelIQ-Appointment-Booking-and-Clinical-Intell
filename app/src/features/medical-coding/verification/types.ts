/**
 * types.ts — TypeScript interfaces for US_075 Verification Enforcement DTOs.
 *
 * Mirrors backend VerificationRequestDto / VerificationAuditEntryDto from
 * UPACIP.Service/Verification/Dtos and the VerificationEnforcementService
 * return shapes.
 *
 * VerificationStatus values match the backend CodeVerificationStatus /
 * VerificationStatus enum strings, kebab-cased for frontend consistency.
 */

// ─── Status union ─────────────────────────────────────────────────────────────

/** Frontend representation of backend PendingVerification / Verified / Modified / Rejected. */
export type VerificationStatus =
  | 'pending-verification'
  | 'verified'
  | 'modified'
  | 'rejected';

// ─── Item DTO ─────────────────────────────────────────────────────────────────

/**
 * Single AI-generated record awaiting human verification.
 * Returned by GET /api/staff/verification/pending.
 */
export interface VerificationItem {
  /** Record Guid — either a MedicalCode.Id or ExtractedData.Id. */
  id: string;
  /** "MedicalCode" | "ExtractedData" */
  recordType: 'MedicalCode' | 'ExtractedData';
  /** Current code or extracted value (AI-suggested). */
  codeValue: string;
  /** Human-readable description of the code / extracted field. */
  description: string;
  /** AI confidence score in [0.0, 1.0]. */
  confidenceScore: number;
  /** Current verification lifecycle state. */
  verificationStatus: VerificationStatus;
  /** AI-generated clinical justification text. */
  aiJustification: string;
  /** Optional source document attribution. */
  sourceAttribution?: string;
}

// ─── Pending response ─────────────────────────────────────────────────────────

export interface PendingVerificationResponse {
  patientId: string;
  items: VerificationItem[];
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

/**
 * Body for single-item approve / modify / reject actions.
 * staffUserId is NOT included — the backend extracts it from JWT claims.
 */
export interface VerificationRequest {
  recordId: string;
  recordType: string;
  /** Staff-entered replacement value (required for modify action). */
  newValue?: string;
  /** Staff-entered clinical justification (required for modify / reject). */
  justification?: string;
}

/** Body for batch approve. */
export interface BatchVerificationRequest {
  recordIds: string[];
  recordType: string;
}

// ─── Audit entry DTO ──────────────────────────────────────────────────────────

/**
 * A single verification event returned by the audit trail endpoint.
 * Returned by GET /api/staff/verification/audit-trail/{recordId}.
 */
export interface VerificationAuditEntry {
  recordId: string;
  recordType: string;
  /** Backend user Guid of the staff member who acted. */
  staffUserId: string;
  /** Display name of the staff member (resolved by backend). */
  staffName: string;
  /** ISO 8601 UTC timestamp of the verification action. */
  verifiedAt: string;
  /** "Approved" | "Modified" | "Rejected" */
  action: string;
  /** Original AI-suggested value before any modification. */
  originalAiValue: string;
  /** Final value after staff action (same as originalAiValue for approve). */
  finalValue: string;
  /** Staff-entered justification (present for modify / reject). */
  justification?: string;
}
