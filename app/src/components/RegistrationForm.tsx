import { useRef, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Checkbox from '@mui/material/Checkbox';
import CircularProgress from '@mui/material/CircularProgress';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormHelperText from '@mui/material/FormHelperText';
import Grid from '@mui/material/Grid';
import Link from '@mui/material/Link';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { Link as RouterLink } from 'react-router-dom';
import {
  registrationSchema,
  type RegistrationFormData,
} from '@/validation/registrationSchema';
import PasswordStrengthIndicator from '@/components/PasswordStrengthIndicator';
import { useCheckEmail } from '@/hooks/useRegistration';

interface Props {
  onSubmit: (data: RegistrationFormData) => void;
  isSubmitting: boolean;
  serverError?: string;
}

export default function RegistrationForm({ onSubmit, isSubmitting, serverError }: Props) {
  const {
    control,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<RegistrationFormData>({
    resolver: zodResolver(registrationSchema),
    // Validate on blur initially; re-validate on every change after first blur (UXR-501)
    mode: 'onBlur',
    reValidateMode: 'onChange',
    defaultValues: {
      firstName: '',
      lastName: '',
      email: '',
      phone: '',
      dob: '',
      password: '',
      termsAccepted: false,
    },
  });

  const passwordValue = watch('password');

  // Email availability check state
  const checkEmail = useCheckEmail();
  const [emailHelperText, setEmailHelperText] = useState('');
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  function handleEmailBlur(email: string) {
    if (!email || errors.email) {
      setEmailHelperText('');
      return;
    }
    setEmailHelperText('Checking availability...');
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      checkEmail.mutate(email, {
        onSuccess: () => setEmailHelperText(''),
        // Duplicate email is surfaced on form submission (AC-4); clear here
        onError: () => setEmailHelperText(''),
      });
    }, 300);
  }

  return (
    <Box
      component="form"
      onSubmit={handleSubmit(onSubmit)}
      noValidate
      aria-label="Patient registration form"
    >
      {serverError && (
        <Alert severity="error" sx={{ mb: 2 }} role="alert">
          {serverError}
        </Alert>
      )}

      {/* First name / Last name — two-column on desktop, single-column on mobile (UXR-303) */}
      <Grid container spacing={2}>
        <Grid item xs={12} sm={6}>
          <Controller
            name="firstName"
            control={control}
            render={({ field }) => (
              <TextField
                {...field}
                id="first-name"
                label="First Name"
                placeholder="John"
                fullWidth
                required
                error={!!errors.firstName}
                helperText={errors.firstName?.message}
                autoComplete="given-name"
                inputProps={{ 'aria-required': 'true', 'aria-label': 'First name' }}
              />
            )}
          />
        </Grid>
        <Grid item xs={12} sm={6}>
          <Controller
            name="lastName"
            control={control}
            render={({ field }) => (
              <TextField
                {...field}
                id="last-name"
                label="Last Name"
                placeholder="Doe"
                fullWidth
                required
                error={!!errors.lastName}
                helperText={errors.lastName?.message}
                autoComplete="family-name"
                inputProps={{ 'aria-required': 'true', 'aria-label': 'Last name' }}
              />
            )}
          />
        </Grid>
      </Grid>

      <Box sx={{ mt: 2 }}>
        <Controller
          name="email"
          control={control}
          render={({ field }) => (
            <TextField
              {...field}
              id="reg-email"
              label="Email Address"
              type="email"
              placeholder="e.g., john@email.com"
              fullWidth
              required
              error={!!errors.email}
              helperText={
                errors.email?.message ??
                (emailHelperText || "We'll send a verification email to this address.")
              }
              autoComplete="email"
              inputProps={{ 'aria-required': 'true', 'aria-label': 'Email address' }}
              onBlur={(e) => {
                field.onBlur();
                handleEmailBlur(e.target.value);
              }}
            />
          )}
        />
      </Box>

      <Box sx={{ mt: 2 }}>
        <Controller
          name="phone"
          control={control}
          render={({ field }) => (
            <TextField
              {...field}
              id="reg-phone"
              label="Phone Number"
              type="tel"
              placeholder="(555) 123-4567"
              fullWidth
              required
              error={!!errors.phone}
              helperText={errors.phone?.message}
              autoComplete="tel"
              inputProps={{ 'aria-required': 'true', 'aria-label': 'Phone number' }}
            />
          )}
        />
      </Box>

      <Box sx={{ mt: 2 }}>
        <Controller
          name="dob"
          control={control}
          render={({ field }) => (
            <TextField
              {...field}
              id="reg-dob"
              label="Date of Birth"
              type="date"
              fullWidth
              required
              error={!!errors.dob}
              helperText={errors.dob?.message}
              autoComplete="bdate"
              InputLabelProps={{ shrink: true }}
              inputProps={{ 'aria-required': 'true', 'aria-label': 'Date of birth' }}
            />
          )}
        />
      </Box>

      <Box sx={{ mt: 2 }}>
        <Controller
          name="password"
          control={control}
          render={({ field }) => (
            <TextField
              {...field}
              id="reg-password"
              label="Password"
              type="password"
              placeholder="Create a strong password"
              fullWidth
              required
              error={!!errors.password}
              helperText={
                errors.password?.message ??
                'Use 8+ characters with a mix of letters, numbers, and symbols.'
              }
              autoComplete="new-password"
              inputProps={{ 'aria-required': 'true', 'aria-label': 'Password' }}
            />
          )}
        />
        <PasswordStrengthIndicator password={passwordValue} />
      </Box>

      <Box sx={{ mt: 2 }}>
        <Controller
          name="termsAccepted"
          control={control}
          render={({ field }) => (
            <>
              <FormControlLabel
                control={
                  <Checkbox
                    id="terms"
                    checked={field.value}
                    onChange={(e) => field.onChange(e.target.checked)}
                    onBlur={field.onBlur}
                    inputRef={field.ref}
                    color="primary"
                    inputProps={
                      {
                        'aria-required': 'true',
                        'aria-label': 'Accept Terms of Service and Privacy Policy',
                      } as React.InputHTMLAttributes<HTMLInputElement>
                    }
                  />
                }
                label={
                  <Typography variant="body2">
                    I agree to the{' '}
                    <Link href="#" color="primary" underline="hover">
                      Terms of Service
                    </Link>{' '}
                    and{' '}
                    <Link href="#" color="primary" underline="hover">
                      Privacy Policy
                    </Link>
                  </Typography>
                }
              />
              {errors.termsAccepted && (
                <FormHelperText error sx={{ ml: 2 }}>
                  {errors.termsAccepted.message}
                </FormHelperText>
              )}
            </>
          )}
        />
      </Box>

      <Button
        id="register-btn"
        type="submit"
        variant="contained"
        fullWidth
        disabled={isSubmitting}
        sx={{ mt: 3, py: 1.5 }}
        aria-label={isSubmitting ? 'Creating account…' : 'Create account'}
      >
        {isSubmitting ? <CircularProgress size={24} color="inherit" /> : 'Create Account'}
      </Button>

      <Typography variant="body2" align="center" sx={{ mt: 2, color: 'text.secondary' }}>
        Already have an account?{' '}
        <Link
          id="back-login"
          component={RouterLink}
          to="/login"
          color="primary"
          underline="none"
        >
          Sign in
        </Link>
      </Typography>
    </Box>
  );
}
