import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Typography from '@mui/material/Typography';
import BlockOutlinedIcon from '@mui/icons-material/BlockOutlined';
import DashboardOutlinedIcon from '@mui/icons-material/DashboardOutlined';
import LogoutOutlinedIcon from '@mui/icons-material/LogoutOutlined';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '@/hooks/useAuth';

/**
 * SCR-AccessDenied — displayed when a user navigates to a route outside
 * their role (AC-1).  Provides two recovery actions:
 *   1. "Go to Dashboard" — routes back to /dashboard (SCR-002) for re-routing.
 *   2. "Sign Out" — clears auth state and redirects to /login.
 */
export default function AccessDeniedPage() {
  const navigate = useNavigate();
  const { clearAuth } = useAuth();

  function handleGoToDashboard() {
    navigate('/dashboard', { replace: true });
  }

  function handleSignOut() {
    clearAuth();
    navigate('/login', { replace: true });
  }

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
      <Card
        elevation={1}
        sx={{
          maxWidth: 520,
          width: '100%',
          borderRadius: 2,
        }}
      >
        <CardContent sx={{ p: 4, textAlign: 'center' }}>
          {/* Icon */}
          <BlockOutlinedIcon
            sx={{ fontSize: '3.5rem', color: 'error.main', mb: 2 }}
            aria-hidden="true"
          />

          {/* Heading */}
          <Typography variant="h5" component="h1" sx={{ fontWeight: 700, mb: 1 }}>
            Access Denied
          </Typography>

          {/* MUI Alert with error severity (AC-1) */}
          <Alert severity="error" sx={{ textAlign: 'left', mb: 3 }}>
            You do not have permission to view this page. If you believe this is an error, please
            contact your system administrator.
          </Alert>

          {/* Actions */}
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
            <Button
              variant="contained"
              startIcon={<DashboardOutlinedIcon />}
              onClick={handleGoToDashboard}
              fullWidth
              aria-label="Go to your dashboard"
            >
              Go to Dashboard
            </Button>
            <Button
              variant="outlined"
              color="inherit"
              startIcon={<LogoutOutlinedIcon />}
              onClick={handleSignOut}
              fullWidth
              aria-label="Sign out and switch accounts"
            >
              Sign Out
            </Button>
          </Box>
        </CardContent>
      </Card>
    </Box>
  );
}
