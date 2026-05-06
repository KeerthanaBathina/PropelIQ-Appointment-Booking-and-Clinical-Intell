import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Container from '@mui/material/Container';
import Grid from '@mui/material/Grid';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import CalendarMonthIcon from '@mui/icons-material/CalendarMonth';
import PsychologyIcon from '@mui/icons-material/Psychology';
import SecurityIcon from '@mui/icons-material/Security';
import PeopleIcon from '@mui/icons-material/People';
import MonitorHeartIcon from '@mui/icons-material/MonitorHeart';
import InsightsIcon from '@mui/icons-material/Insights';
import { useNavigate } from 'react-router-dom';

const FEATURES = [
  {
    icon: <CalendarMonthIcon sx={{ fontSize: 40, color: '#7B1FA2' }} />,
    title: 'Smart Scheduling',
    description: 'Book, reschedule, and manage appointments with real-time slot availability and instant confirmation.',
  },
  {
    icon: <PsychologyIcon sx={{ fontSize: 40, color: '#7B1FA2' }} />,
    title: 'AI Clinical Intake',
    description: 'Conversational AI guides patients through intake, automatically extracting structured clinical data.',
  },
  {
    icon: <MonitorHeartIcon sx={{ fontSize: 40, color: '#7B1FA2' }} />,
    title: 'Patient Records',
    description: 'Unified patient profiles with visit history, documents, vitals, and care timelines in one place.',
  },
  {
    icon: <InsightsIcon sx={{ fontSize: 40, color: '#7B1FA2' }} />,
    title: 'Clinical Intelligence',
    description: 'Real-time dashboards and analytics to support evidence-based clinical decisions.',
  },
  {
    icon: <PeopleIcon sx={{ fontSize: 40, color: '#7B1FA2' }} />,
    title: 'Staff Workflows',
    description: 'Streamlined tools for care teams — document upload, patient search, and task management.',
  },
  {
    icon: <SecurityIcon sx={{ fontSize: 40, color: '#7B1FA2' }} />,
    title: 'HIPAA-Ready Security',
    description: 'Role-based access control, audit logging, and encryption at rest and in transit.',
  },
];



function PlaceholderPage() {
  const navigate = useNavigate();
  return (
    <Box sx={{ minHeight: '100vh', bgcolor: '#FAFAFA', display: 'flex', flexDirection: 'column' }}>
      {/* ── Top nav ───────────────────────────────────────────────────────── */}
      <AppBar position="static" sx={{ background: 'linear-gradient(135deg, #4A148C 0%, #7B1FA2 100%)', boxShadow: '0 2px 12px rgba(74,20,140,0.3)' }}>
        <Toolbar sx={{ px: { xs: 2, md: 6 } }}>
          <MonitorHeartIcon sx={{ mr: 1.5, fontSize: 26 }} />
          <Typography variant="h6" component="div" sx={{ flexGrow: 1, fontWeight: 700, letterSpacing: 0.5 }}>
            UPACIP
          </Typography>
          <Button color="inherit" variant="outlined" onClick={() => navigate('/login')}
            sx={{ borderColor: 'rgba(255,255,255,0.6)', mr: 1, '&:hover': { borderColor: '#fff', bgcolor: 'rgba(255,255,255,0.1)' } }}>
            Sign In
          </Button>
          <Button color="inherit" variant="contained" onClick={() => navigate('/register')}
            sx={{ bgcolor: '#fff', color: '#7B1FA2', fontWeight: 700, '&:hover': { bgcolor: '#F3E5F5' } }}>
            Register
          </Button>
        </Toolbar>
      </AppBar>

      {/* ── Hero ──────────────────────────────────────────────────────────── */}
      <Box sx={{
        background: 'linear-gradient(160deg, #4A148C 0%, #7B1FA2 45%, #1565C0 100%)',
        color: '#fff',
        py: { xs: 10, md: 14 },
        px: 2,
        textAlign: 'center',
        position: 'relative',
        overflow: 'hidden',
        '&::before': {
          content: '""',
          position: 'absolute',
          inset: 0,
          backgroundImage: 'radial-gradient(circle at 20% 50%, rgba(255,255,255,0.06) 0%, transparent 60%), radial-gradient(circle at 80% 20%, rgba(255,255,255,0.04) 0%, transparent 50%)',
        },
      }}>
        <Container maxWidth="md" sx={{ position: 'relative' }}>
          <Box sx={{
            display: 'inline-block',
            bgcolor: 'rgba(255,255,255,0.15)',
            borderRadius: 6,
            px: 2.5,
            py: 0.5,
            mb: 3,
            backdropFilter: 'blur(8px)',
            border: '1px solid rgba(255,255,255,0.2)',
          }}>
            <Typography variant="caption" sx={{ fontWeight: 600, letterSpacing: 1.5, textTransform: 'uppercase', fontSize: '0.7rem' }}>
              Unified Patient Access &amp; Clinical Intelligence Platform
            </Typography>
          </Box>
          <Typography variant="h2" component="h1" sx={{ fontWeight: 800, lineHeight: 1.15, mb: 3, fontSize: { xs: '2.2rem', md: '3.4rem' } }}>
            Better Care Starts with&nbsp;
            <Box component="span" sx={{ color: '#CE93D8' }}>Smarter Tools</Box>
          </Typography>
          <Typography variant="h6" sx={{ color: 'rgba(255,255,255,0.8)', mb: 5, maxWidth: 560, mx: 'auto', fontWeight: 400, lineHeight: 1.7 }}>
            One platform for appointment booking, AI-powered clinical intake, patient records, and care team workflows.
          </Typography>
          <Box sx={{ display: 'flex', gap: 2, justifyContent: 'center', flexWrap: 'wrap' }}>
            <Button variant="contained" size="large" onClick={() => navigate('/login')}
              sx={{ bgcolor: '#fff', color: '#7B1FA2', fontWeight: 700, px: 4, py: 1.5, fontSize: '1rem', borderRadius: 2, boxShadow: '0 4px 20px rgba(0,0,0,0.25)', '&:hover': { bgcolor: '#F3E5F5', transform: 'translateY(-1px)', boxShadow: '0 6px 24px rgba(0,0,0,0.3)' }, transition: 'all 0.2s' }}>
              Get Started
            </Button>
            <Button variant="outlined" size="large" onClick={() => navigate('/register')}
              sx={{ borderColor: 'rgba(255,255,255,0.7)', color: '#fff', px: 4, py: 1.5, fontSize: '1rem', borderRadius: 2, '&:hover': { borderColor: '#fff', bgcolor: 'rgba(255,255,255,0.1)' } }}>
              Create Account
            </Button>
          </Box>
        </Container>
      </Box>


      {/* ── Feature cards ─────────────────────────────────────────────────── */}
      <Container maxWidth="lg" sx={{ py: { xs: 6, md: 10 } }}>
        <Typography variant="h4" component="h2" sx={{ textAlign: 'center', fontWeight: 700, mb: 1, color: '#1A1A2E' }}>
          Everything your clinic needs
        </Typography>
        <Typography variant="body1" color="text.secondary" sx={{ textAlign: 'center', mb: 6, maxWidth: 500, mx: 'auto' }}>
          From first contact to clinical documentation — UPACIP covers the full patient journey.
        </Typography>
        <Grid container spacing={3}>
          {FEATURES.map((f) => (
            <Grid item xs={12} sm={6} md={4} key={f.title}>
              <Card elevation={0} sx={{
                height: '100%',
                border: '1px solid #EEEEEE',
                borderRadius: 3,
                p: 1,
                transition: 'all 0.2s',
                '&:hover': {
                  borderColor: '#CE93D8',
                  boxShadow: '0 8px 32px rgba(123,31,162,0.1)',
                  transform: 'translateY(-3px)',
                },
              }}>
                <CardContent>
                  <Box sx={{ mb: 2 }}>{f.icon}</Box>
                  <Typography variant="h6" sx={{ fontWeight: 700, mb: 1, color: '#1A1A2E' }}>{f.title}</Typography>
                  <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1.7 }}>{f.description}</Typography>
                </CardContent>
              </Card>
            </Grid>
          ))}
        </Grid>
      </Container>

      {/* ── CTA banner ────────────────────────────────────────────────────── */}
      <Box sx={{ background: 'linear-gradient(135deg, #4A148C 0%, #7B1FA2 100%)', py: { xs: 7, md: 9 }, textAlign: 'center', px: 2 }}>
        <Typography variant="h4" sx={{ color: '#fff', fontWeight: 700, mb: 2 }}>
          Ready to transform patient care?
        </Typography>
        <Typography variant="body1" sx={{ color: 'rgba(255,255,255,0.8)', mb: 4 }}>
          Sign in with your credentials or register for a new account.
        </Typography>
        <Button variant="contained" size="large" onClick={() => navigate('/login')}
          sx={{ bgcolor: '#fff', color: '#7B1FA2', fontWeight: 700, px: 5, py: 1.5, borderRadius: 2, '&:hover': { bgcolor: '#F3E5F5' } }}>
          Sign In Now
        </Button>
      </Box>

      {/* ── Footer ────────────────────────────────────────────────────────── */}
      <Box component="footer" sx={{ bgcolor: '#1A1A2E', py: 3, textAlign: 'center' }}>
        <Typography variant="caption" sx={{ color: 'rgba(255,255,255,0.4)' }}>
          © 2026 UPACIP — Unified Patient Access &amp; Clinical Intelligence Platform. All rights reserved.
        </Typography>
      </Box>
    </Box>
  );
}

export default PlaceholderPage;
