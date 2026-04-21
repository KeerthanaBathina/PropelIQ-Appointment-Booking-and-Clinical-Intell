import { useMutation } from '@tanstack/react-query';
import { apiPost, type ApiError } from '@/lib/apiClient';

interface ResetPasswordPayload {
  token: string;
  email: string;
  newPassword: string;
}

/**
 * React Query mutation for POST /api/auth/reset-password.
 *
 * HTTP status codes handled by the caller:
 *   200 — success → show success confirmation (AC-4)
 *   400 — invalid token → show "Invalid reset link" with resend option
 *   410 — expired token → show "Link expired" with resend option (AC-3)
 *   422 — password complexity failure → show specific validation errors
 *   5xx — server error → show generic retry message
 */
export function useResetPassword() {
  return useMutation<void, ApiError, ResetPasswordPayload>({
    mutationFn: (payload) => apiPost<void>('/api/auth/reset-password', payload),
  });
}
