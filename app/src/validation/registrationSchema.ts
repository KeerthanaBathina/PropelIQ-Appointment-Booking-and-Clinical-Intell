import { z } from 'zod';

// Password complexity regex constants
const PASSWORD_UPPER = /[A-Z]/;
const PASSWORD_NUMBER = /[0-9]/;
const PASSWORD_SPECIAL = /[^A-Za-z0-9]/;

export const registrationSchema = z.object({
  firstName: z
    .string()
    .min(1, 'First name is required')
    .max(50, 'First name must be 50 characters or fewer'),
  lastName: z
    .string()
    .min(1, 'Last name is required')
    .max(50, 'Last name must be 50 characters or fewer'),
  email: z
    .string()
    .min(1, 'Email is required')
    .email('Enter a valid email address'),
  phone: z
    .string()
    .min(1, 'Phone number is required')
    .regex(/^\+?[\d\s\-().]{7,20}$/, 'Enter a valid phone number'),
  dob: z
    .string()
    .min(1, 'Date of birth is required')
    .refine((val) => {
      const date = new Date(val);
      return !isNaN(date.getTime()) && date < new Date();
    }, 'Date of birth must be a past date'),
  password: z
    .string()
    .min(8, 'Password must be at least 8 characters')
    .refine((val) => PASSWORD_UPPER.test(val), 'Password must contain at least 1 uppercase letter')
    .refine((val) => PASSWORD_NUMBER.test(val), 'Password must contain at least 1 number')
    .refine((val) => PASSWORD_SPECIAL.test(val), 'Password must contain at least 1 special character'),
  termsAccepted: z
    .boolean()
    .refine((val) => val === true, 'You must accept the Terms of Service and Privacy Policy'),
});

export type RegistrationFormData = z.infer<typeof registrationSchema>;

export interface PasswordCriteria {
  length: boolean;
  uppercase: boolean;
  number: boolean;
  special: boolean;
}

export interface PasswordStrengthResult {
  /** 0 = empty, 1 = weak, 2 = fair, 3 = strong, 4 = excellent */
  level: 0 | 1 | 2 | 3 | 4;
  label: 'weak' | 'fair' | 'strong' | 'excellent';
  criteria: PasswordCriteria;
}

const STRENGTH_LABELS: PasswordStrengthResult['label'][] = [
  'weak',
  'weak',
  'fair',
  'strong',
  'excellent',
];

export function getPasswordStrength(password: string): PasswordStrengthResult {
  const criteria: PasswordCriteria = {
    length: password.length >= 8,
    uppercase: PASSWORD_UPPER.test(password),
    number: PASSWORD_NUMBER.test(password),
    special: PASSWORD_SPECIAL.test(password),
  };

  const passed = Object.values(criteria).filter(Boolean).length as 0 | 1 | 2 | 3 | 4;

  return { level: passed, label: STRENGTH_LABELS[passed], criteria };
}
