/**
 * cpt.types.ts — TypeScript interfaces for CPT procedure code data (US_048).
 *
 * Mirrors backend CptMappingResponseDto / CptCodeDto from UPACIP.Api/Models.
 */

// ─── Code DTO ─────────────────────────────────────────────────────────────────

export type CptCodeStatus = 'Pending' | 'Approved' | 'Overridden';

export interface CptCodeDto {
  medicalCodeId: string | null;
  codeValue: string;
  description: string;
  confidenceScore: number;
  justification: string;
  relevanceRank: number | null;
  /** "Pending" | "Approved" | "Overridden" */
  status: CptCodeStatus;
  /** True when multiple CPT codes map to the same procedure (bundled set). */
  isBundled: boolean;
  /** Group identifier for bundled code sets (null when not bundled). */
  bundleGroupId: string | null;
  /** Overriding user display name (non-null when status = "Overridden"). */
  overriddenByName: string | null;
  /** ISO date of override action. */
  overriddenAt: string | null;
  /** Replacement code value entered during override (if any). */
  overrideReplacementCode: string | null;
}

// ─── Response DTO ─────────────────────────────────────────────────────────────

export interface CptMappingResponseDto {
  patientId: string;
  codes: CptCodeDto[];
  lastCodingRunAt: string | null;
}

// ─── Approve request ──────────────────────────────────────────────────────────

export interface CptApproveRequest {
  medicalCodeId: string;
}

// ─── Override request ─────────────────────────────────────────────────────────

export interface CptOverrideRequest {
  medicalCodeId: string;
  replacementCode: string;
  justification: string;
}
