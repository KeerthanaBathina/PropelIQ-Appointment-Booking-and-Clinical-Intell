/**
 * useMfaSetup — React Query hooks for MFA enrollment (US_016).
 *
 * GET  /api/auth/mfa/setup          → returns otpAuthUrl + manualEntryKey + backupCodes
 * POST /api/auth/mfa/verify-setup   → verifies first TOTP code to complete enrollment
 */

import { useMutation, useQuery } from '@tanstack/react-query';
import { apiGet, apiPost, type ApiError } from '@/lib/apiClient';

// ─── Response shapes ──────────────────────────────────────────────────────────

export interface MfaSetupData {
  /** otpauth:// URI suitable for QR code generation. */
  otpAuthUrl: string;
  /** Base32 manual entry key shown below the QR code. */
  manualEntryKey: string;
  /** One-time backup codes. Shown in step 3 of setup flow. */
  backupCodes: string[];
}

interface VerifySetupPayload {
  /** 6-digit TOTP code entered after scanning the QR code. */
  totpCode: string;
}

// ─── Hooks ────────────────────────────────────────────────────────────────────

/**
 * Fetches the MFA setup data (QR code URI + manual key + backup codes).
 * Only enabled when the setup modal is open to avoid unnecessary API calls.
 */
export function useMfaSetupData(enabled: boolean) {
  return useQuery<MfaSetupData, ApiError>({
    queryKey: ['mfa-setup'],
    queryFn: () => apiGet<MfaSetupData>('/api/auth/mfa/setup'),
    enabled,
    staleTime: Infinity, // setup data should not be re-fetched during the flow
    retry: false,
  });
}

/**
 * Verifies the first TOTP code during enrollment to complete MFA setup.
 * On success the backend marks MFA as enabled for the user.
 */
export function useVerifyMfaSetup() {
  return useMutation<void, ApiError, VerifySetupPayload>({
    mutationFn: (payload) => apiPost<void>('/api/auth/mfa/verify-setup', payload),
  });
}
