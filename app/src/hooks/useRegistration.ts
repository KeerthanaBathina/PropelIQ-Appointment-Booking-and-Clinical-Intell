import { useMutation } from '@tanstack/react-query';
import { apiPost, type ApiError } from '@/lib/apiClient';
import type { RegistrationFormData } from '@/validation/registrationSchema';

interface RegisterPayload {
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  dateOfBirth: string;
  password: string;
  acceptedTerms: boolean;
}

function buildRegisterPayload(data: RegistrationFormData): RegisterPayload {
  return {
    firstName: data.firstName,
    lastName: data.lastName,
    email: data.email,
    phone: data.phone,
    dateOfBirth: data.dob,
    password: data.password,
    acceptedTerms: data.termsAccepted,
  };
}

export function useRegistration() {
  return useMutation<void, ApiError, RegistrationFormData>({
    mutationFn: (data) => apiPost<void>('/api/auth/register', buildRegisterPayload(data)),
  });
}

export function useCheckEmail() {
  return useMutation<void, ApiError, string>({
    mutationFn: (email) => apiPost<void>('/api/auth/check-email', { email }),
  });
}
