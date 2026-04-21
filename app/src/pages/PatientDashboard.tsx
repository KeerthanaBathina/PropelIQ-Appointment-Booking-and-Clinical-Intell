/**
 * PatientDashboard — SCR-005
 *
 * Role-specific dashboard for Patient users.
 * Displays LastLoginBanner (AC-4, US_016) below the top navigation bar.
 * Feature content is implemented in future tasks (US_017+).
 */

import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Container from '@mui/material/Container';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import LastLoginBanner from '@/components/auth/LastLoginBanner';

export default function PatientDashboard() {
  return (
    <Box sx={{ flexGrow: 1 }}>
      <AppBar position="static" sx={{ bgcolor: 'primary.main' }}>
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            UPACIP — Patient Portal
          </Typography>
        </Toolbar>
      </AppBar>

      <LastLoginBanner />

      <Container maxWidth="lg" sx={{ mt: 4 }}>
        <Typography variant="h5" component="h1" gutterBottom>
          Welcome to your Patient Portal
        </Typography>
        <Typography variant="body1" color="text.secondary">
          Appointments, intake records, and health information will appear here.
        </Typography>
      </Container>
    </Box>
  );
}
