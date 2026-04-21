import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import CardContent from '@mui/material/CardContent';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import LocalHospitalOutlinedIcon from '@mui/icons-material/LocalHospitalOutlined';
import PeopleAltOutlinedIcon from '@mui/icons-material/PeopleAltOutlined';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import { useAuth, type UserRole } from '@/hooks/useAuth';

// ─── Token colors (UXR-403) ───────────────────────────────────────────────────
const PATIENT_COLOR = '#1976D2'; // primary-500
const STAFF_COLOR = '#7B1FA2';   // secondary-500
const ADMIN_COLOR = '#616161';   // neutral-700

const REDIRECT_DELAY_MS = 3000;

interface RoleCardConfig {
  role: UserRole;
  id: string;
  label: string;
  description: string;
  iconColor: string;
  Icon: React.ElementType;
  targetPath: string;
  ariaLabel: string;
}

const ROLE_CARDS: RoleCardConfig[] = [
  {
    role: 'Patient',
    id: 'patient-dash',
    label: 'Patient Portal',
    description: 'Appointments, intake, and health records',
    iconColor: PATIENT_COLOR,
    Icon: LocalHospitalOutlinedIcon,
    targetPath: '/patient/dashboard',
    ariaLabel: 'Patient Dashboard',
  },
  {
    role: 'Staff',
    id: 'staff-dash',
    label: 'Staff Portal',
    description: 'Queue, documents, and clinical tools',
    iconColor: STAFF_COLOR,
    Icon: PeopleAltOutlinedIcon,
    targetPath: '/staff/dashboard',
    ariaLabel: 'Staff Dashboard',
  },
  {
    role: 'Admin',
    id: 'admin-dash',
    label: 'Admin Portal',
    description: 'Configuration, users, and monitoring',
    iconColor: ADMIN_COLOR,
    Icon: SettingsOutlinedIcon,
    targetPath: '/admin/dashboard',
    ariaLabel: 'Admin Dashboard',
  },
];

type ScreenState = 'loading' | 'default' | 'error';

export default function DashboardRouterPage() {
  const navigate = useNavigate();
  const { role, isAuthenticated } = useAuth();
  const [screenState, setScreenState] = useState<ScreenState>('loading');
  const [countdown, setCountdown] = useState(3);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Redirect to login if not authenticated
  useEffect(() => {
    if (!isAuthenticated) {
      navigate('/login', { replace: true });
    }
  }, [isAuthenticated, navigate]);

  // Resolve screen state once role is available
  useEffect(() => {
    if (!isAuthenticated) return;

    if (role) {
      setScreenState('default');
    } else {
      // Short decode delay — if role still absent, show error state
      const timeout = setTimeout(() => {
        if (!role) setScreenState('error');
      }, 400);
      return () => clearTimeout(timeout);
    }
  }, [role, isAuthenticated]);

  // Auto-redirect with countdown when in default state
  useEffect(() => {
    if (screenState !== 'default' || !role) return;

    const card = ROLE_CARDS.find((c) => c.role === role);
    if (!card) {
      setScreenState('error');
      return;
    }

    setCountdown(3);

    intervalRef.current = setInterval(() => {
      setCountdown((prev) => Math.max(prev - 1, 0));
    }, 1000);

    timerRef.current = setTimeout(() => {
      navigate(card.targetPath, { replace: true });
    }, REDIRECT_DELAY_MS);

    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [screenState, role, navigate]);

  const handleCardClick = (targetPath: string) => {
    if (timerRef.current) clearTimeout(timerRef.current);
    if (intervalRef.current) clearInterval(intervalRef.current);
    navigate(targetPath, { replace: true });
  };

  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        bgcolor: '#F5F5F5', // neutral-100
        px: 3,
      }}
    >
      <Box
        sx={{
          textAlign: 'center',
          maxWidth: 800,
          width: '100%',
          py: 6,
        }}
      >
        {/* Brand mark */}
        <Typography
          variant="h6"
          component="div"
          sx={{ color: PATIENT_COLOR, fontWeight: 700, mb: 1 }}
        >
          UPACIP
        </Typography>

        {/* Heading */}
        <Typography variant="h4" component="h1" sx={{ fontWeight: 700, mb: 1 }}>
          Welcome back
        </Typography>

        {/* Sub-heading */}
        {screenState === 'loading' && (
          <Skeleton variant="text" width={320} sx={{ mx: 'auto', mb: 2 }} />
        )}
        {screenState === 'default' && (
          <Typography variant="body1" sx={{ color: '#757575', mb: 0 }}>
            You are being redirected to your dashboard...
          </Typography>
        )}
        {screenState === 'error' && (
          <Typography variant="body1" color="error" sx={{ mb: 2 }}>
            Unable to determine your role. Please{' '}
            <Box
              component="a"
              href="/login"
              sx={{ color: PATIENT_COLOR, textDecoration: 'underline' }}
            >
              sign in again
            </Box>
            .
          </Typography>
        )}

        {/* Role cards grid — 3-col desktop / 1-col mobile (< 768px per wireframe) */}
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: {
              xs: '1fr',
              sm: 'repeat(3, 1fr)',
            },
            gap: 3,
            mt: 4,
          }}
        >
          {screenState === 'loading'
            ? ROLE_CARDS.map((c) => (
                <Skeleton key={c.id} variant="rounded" height={180} sx={{ borderRadius: 2 }} />
              ))
            : ROLE_CARDS.map((c) => {
                const isActive = role === c.role;
                return (
                  <Card
                    key={c.id}
                    id={c.id}
                    elevation={isActive ? 3 : 1}
                    sx={{
                      borderRadius: 2,
                      transition: 'box-shadow 150ms ease, transform 150ms ease',
                      border: isActive ? `2px solid ${c.iconColor}` : '2px solid transparent',
                      '&:hover': {
                        boxShadow: 4,
                        transform: 'translateY(-2px)',
                      },
                    }}
                  >
                    <CardActionArea
                      id={`${c.id}-btn`}
                      role="button"
                      aria-label={c.ariaLabel}
                      onClick={() => handleCardClick(c.targetPath)}
                      sx={{
                        py: 4,
                        px: 3,
                        textAlign: 'center',
                        '&:focus-visible': {
                          outline: `2px solid ${PATIENT_COLOR}`,
                          outlineOffset: 2,
                        },
                      }}
                    >
                      <CardContent sx={{ p: 0 }}>
                        <c.Icon
                          sx={{ fontSize: '3rem', color: c.iconColor, mb: 2 }}
                          aria-hidden="true"
                        />
                        <Typography variant="h6" component="h3" sx={{ fontWeight: 600 }}>
                          {c.label}
                        </Typography>
                        <Typography
                          variant="body2"
                          sx={{ color: '#757575', mt: 1 }}
                        >
                          {c.description}
                        </Typography>
                      </CardContent>
                    </CardActionArea>
                  </Card>
                );
              })}
        </Box>

        {/* Countdown message */}
        {screenState === 'default' && (
          <Typography
            variant="caption"
            component="p"
            sx={{ mt: 3, color: '#9E9E9E' }}
            aria-live="polite"
          >
            Auto-redirecting based on your role in {countdown} second
            {countdown !== 1 ? 's' : ''}...
          </Typography>
        )}
      </Box>
    </Box>
  );
}
