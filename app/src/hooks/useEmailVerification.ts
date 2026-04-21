import { useMutation } from '@tanstack/react-query';
import { apiPost, type ApiError } from '@/lib/apiClient';

export function useVerifyEmail() {
  return useMutation<void, ApiError, string>({
    mutationFn: (token) => apiPost<void>('/api/auth/verify-email', { token }),
  });
}

export function useResendVerification() {
  return useMutation<void, ApiError, string>({
    mutationFn: (email) => apiPost<void>('/api/auth/resend-verification', { email }),
  });
}
