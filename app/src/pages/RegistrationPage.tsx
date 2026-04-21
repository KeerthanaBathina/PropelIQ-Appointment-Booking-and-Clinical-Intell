import { useState } from 'react';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Typography from '@mui/material/Typography';
import RegistrationForm from '@/components/RegistrationForm';
import { useRegistration } from '@/hooks/useRegistration';
import { ApiError } from '@/lib/apiClient';
import type { RegistrationFormData } from '@/validation/registrationSchema';

type ScreenState = 'default' | 'loading' | 'success' | 'error';

export default function RegistrationPage() {
  const [screenState, setScreenState] = useState<ScreenState>('default');
  const [serverError, setServerError] = useState<string | undefined>();

  const mutation = useRegistration();

  function handleSubmit(data: RegistrationFormData) {
    setScreenState('loading');
    setServerError(undefined);

    mutation.mutate(data, {
      onSuccess: () => {
        setScreenState('success');
      },
      onError: (error) => {
        // AC-4: show generic message for duplicate email — do not reveal verification status
        if (error instanceof ApiError && error.status === 409) {
          setServerError('An account with this email already exists.');
        } else {
          setServerError('Something went wrong. Please try again.');
        }
        setScreenState('error');
      },
    });
  }

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        // neutral-100 per design system tokens
        bgcolor: '#F5F5F5',
        p: 3,
      }}
    >
      {/* neutral-0 card with shadow — max-width 520px per wireframe */}
      <Card sx={{ width: '100%', maxWidth: 520, boxShadow: 3, bgcolor: '#FFFFFF' }}>
        <CardContent sx={{ p: { xs: 3, sm: 4 } }}>
          {/* Brand logo */}
          <Typography
            variant="h6"
            sx={{ fontWeight: 700, color: 'primary.main', mb: 2 }}
            aria-label="UPACIP"
          >
            UPACIP
          </Typography>

          {screenState === 'success' ? (
            <CheckEmailState />
          ) : (
            <>
              <Typography
                variant="h5"
                component="h1"
                gutterBottom
                sx={{ fontWeight: 400 }}
              >
                Create your account
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
                Join to book appointments and manage your health.
              </Typography>
              <RegistrationForm
                onSubmit={handleSubmit}
                isSubmitting={screenState === 'loading'}
                serverError={screenState === 'error' ? serverError : undefined}
              />
            </>
          )}
        </CardContent>
      </Card>
    </Box>
  );
}

function CheckEmailState() {
  return (
    <Box sx={{ textAlign: 'center', py: 4 }} role="status" aria-live="polite">
      <Typography variant="h5" component="h1" gutterBottom>
        Check your email
      </Typography>
      <Typography variant="body2" color="text.secondary">
        We've sent a verification link to your email address. Please click the link to
        activate your account. The link expires in 1 hour.
      </Typography>
    </Box>
  );
}
