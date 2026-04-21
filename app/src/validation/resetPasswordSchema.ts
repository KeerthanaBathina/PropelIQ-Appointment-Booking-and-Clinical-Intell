import { z } from 'zod';

// Reuse the same regex constants as registrationSchema to keep rules DRY.
const PASSWORD_UPPER = /[A-Z]/;
const PASSWORD_NUMBER = /[0-9]/;
const PASSWORD_SPECIAL = /[^A-Za-z0-9]/;

export const resetPasswordSchema = z
  .object({
    newPassword: z
      .string()
      .min(8, 'Password must be at least 8 characters')
      .refine((v) => PASSWORD_UPPER.test(v), 'Password must contain at least 1 uppercase letter')
      .refine((v) => PASSWORD_NUMBER.test(v), 'Password must contain at least 1 number')
      .refine(
        (v) => PASSWORD_SPECIAL.test(v),
        'Password must contain at least 1 special character',
      ),
    confirmPassword: z.string().min(1, 'Please confirm your new password'),
  })
  .refine((data) => data.newPassword === data.confirmPassword, {
    message: 'Passwords do not match',
    path: ['confirmPassword'],
  });

export type ResetPasswordFormData = z.infer<typeof resetPasswordSchema>;

export const forgotPasswordSchema = z.object({
  email: z.string().min(1, 'Email is required').email('Enter a valid email address'),
});

export type ForgotPasswordFormData = z.infer<typeof forgotPasswordSchema>;
