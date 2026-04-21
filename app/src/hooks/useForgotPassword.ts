import { useMutation } from '@tanstack/react-query';
import { apiPost, type ApiError } from '@/lib/apiClient';

interface ForgotPasswordPayload {
  email: string;
}

/**
 * React Query mutation for POST /api/auth/forgot-password.
 *
 * Backend always returns 200 regardless of whether the email is registered
 * (anti-enumeration). The FE shows the same success message in both cases (AC-1 edge case).
 *
 * Error codes handled by the caller:
 *   429 — rate limited → "Too many requests, please wait."
 *   5xx — server error → generic retry message
 */
export function useForgotPassword() {
  return useMutation<void, ApiError, ForgotPasswordPayload>({
    mutationFn: (payload) => apiPost<void>('/api/auth/forgot-password', payload),
  });
}
