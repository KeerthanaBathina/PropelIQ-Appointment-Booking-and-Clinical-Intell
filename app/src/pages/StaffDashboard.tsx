/**
 * StaffDashboard — SCR-010
 *
 * Role-specific operational hub for Staff users (UXR-403 secondary accent treatment).
 * Refactored to use sub-components and the unified useStaffDashboard hook (US_057).
 *
 * Layout (wireframe-SCR-010-staff-dashboard.html):
 *   sidebar nav | header (breadcrumb + patient search + avatar)
 *   main: stat cards → quick actions → [schedule table | pending tasks]
 *
 * US_057 AC-1: Stats (today's appts, queue count, pending reviews, completed).
 * US_057 AC-2: Schedule table shows patient, time, type, status badge, no-show risk.
 * US_057 AC-3: PendingTasksPanel navigates to SCR-012/013/014 on click.
 * US_057 AC-4: useStaffDashboard polls every 5 s for real-time updates.
 * US_022 AC-1: Walk-in Registration button opens WalkInRegistrationModal.
 * US_026 AC-2/AC-3: No-show risk score + estimated badge in schedule.
 * UXR-003: Breadcrumb navigation in header.
 * UXR-005: Patient search bar always visible in header.
 * UXR-502: Skeleton loading placeholders for >300 ms loads.
 */

import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Container from '@mui/material/Container';
import Divider from '@mui/material/Divider';
import Grid from '@mui/material/Grid';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import Link from '@mui/material/Link';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Paper from '@mui/material/Paper';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import DashboardIcon from '@mui/icons-material/Dashboard';
import DescriptionOutlinedIcon from '@mui/icons-material/DescriptionOutlined';
import PeopleOutlinedIcon from '@mui/icons-material/PeopleOutlined';
import QueueOutlinedIcon from '@mui/icons-material/QueueOutlined';
import SearchIcon from '@mui/icons-material/Search';
import { useState } from 'react';
import { Link as RouterLink, useNavigate } from 'react-router-dom';
import LastLoginBanner from '@/components/auth/LastLoginBanner';
import WalkInRegistrationModal from '@/components/staff/WalkInRegistrationModal';
import { useAuthStore } from '@/hooks/useAuth';
import { useStaffDashboard } from '@/hooks/useStaffDashboard';
import PendingTasksPanel from './StaffDashboard/components/PendingTasksPanel';
import QuickActions from './StaffDashboard/components/QuickActions';
import ScheduleTable from './StaffDashboard/components/ScheduleTable';
import StatCards from './StaffDashboard/components/StatCards';

// ─── Sidebar nav items ────────────────────────────────────────────────────────

const NAV_ITEMS = [
  { label: 'Dashboard', icon: <DashboardIcon />,            href: '/staff/dashboard'       },
  { label: 'Queue',     icon: <QueueOutlinedIcon />,        href: '/staff/queue'           },
  { label: 'Documents', icon: <DescriptionOutlinedIcon />,  href: '/staff/documents'       },
  { label: 'Patients',  icon: <PeopleOutlinedIcon />,       href: '/staff/patients/search' },
] as const;

const SIDEBAR_WIDTH = 220;

// ─── Component ────────────────────────────────────────────────────────────────

export default function StaffDashboard() {
  const navigate = useNavigate();
  const [walkInOpen, setWalkInOpen]   = useState(false);
  const [searchQuery, setSearchQuery] = useState('');

  const email = useAuthStore(s => s.email);
  // Derive avatar initials from email prefix (e.g. "ec" → "EC")
  const avatarInitials = email
    ? email.split('@')[0].slice(0, 2).toUpperCase()
    : 'ST';

  const { data, isLoading, isError } = useStaffDashboard();

  function handlePatientSearch(e: React.FormEvent) {
    e.preventDefault();
    if (searchQuery.trim()) {
      navigate(`/staff/patients/search?q=${encodeURIComponent(searchQuery.trim())}`);
    } else {
      navigate('/staff/patients/search');
    }
  }

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'grey.50' }}>

      {/* ── Sidebar navigation (wireframe staff-sidebar, UXR-403) ── */}
      <Box
        component="nav"
        aria-label="Staff navigation"
        sx={{
          width:           SIDEBAR_WIDTH,
          flexShrink:      0,
          bgcolor:         'background.paper',
          borderRight:     '1px solid',
          borderColor:     'divider',
          display:         { xs: 'none', md: 'flex' },
          flexDirection:   'column',
        }}
      >
        {/* Logo / brand */}
        <Box sx={{ px: 3, py: 2.5 }}>
          <Typography variant="h6" color="secondary.main" fontWeight={700}>
            UPACIP
          </Typography>
        </Box>
        <Divider />
        <List dense sx={{ pt: 1 }}>
          {NAV_ITEMS.map(({ label, icon, href }) => {
            const isActive = window.location.pathname === href;
            return (
              <ListItem key={label} disablePadding>
                <ListItemButton
                  component={RouterLink}
                  to={href}
                  selected={isActive}
                  aria-current={isActive ? 'page' : undefined}
                  sx={{
                    mx: 1,
                    borderRadius: 1,
                    '&.Mui-selected': {
                      borderLeft: '3px solid',
                      borderColor: 'secondary.main',
                      bgcolor: 'secondary.50',
                      color: 'secondary.main',
                      '& .MuiListItemIcon-root': { color: 'secondary.main' },
                    },
                  }}
                >
                  <ListItemIcon sx={{ minWidth: 36 }}>{icon}</ListItemIcon>
                  <ListItemText primary={label} />
                </ListItemButton>
              </ListItem>
            );
          })}
        </List>
      </Box>

      {/* ── Main area ── */}
      <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>

        {/* ── Header (UXR-003 breadcrumb + UXR-005 search + avatar) ── */}
        <Box
          component="header"
          role="banner"
          sx={{
            display:     'flex',
            alignItems:  'center',
            justifyContent: 'space-between',
            px:          3,
            py:          1.5,
            bgcolor:     'background.paper',
            borderBottom: '1px solid',
            borderColor: 'divider',
            gap:         2,
            flexWrap:    'wrap',
          }}
        >
          {/* Breadcrumb (UXR-003) */}
          <Breadcrumbs aria-label="Breadcrumb navigation">
            <Link component={RouterLink} to="/dashboard" underline="hover" color="inherit" variant="body2">
              Home
            </Link>
            <Typography variant="body2" color="text.primary" aria-current="page">
              Staff Dashboard
            </Typography>
          </Breadcrumbs>

          {/* Patient search (UXR-005) */}
          <Box component="form" onSubmit={handlePatientSearch} sx={{ display: 'flex', alignItems: 'center' }}>
            <TextField
              id="search-patient"
              size="small"
              placeholder="Search patients…"
              value={searchQuery}
              onChange={e => setSearchQuery(e.target.value)}
              aria-label="Search patients"
              inputProps={{ 'aria-label': 'Search patients' }}
              sx={{ width: { xs: 160, sm: 250 } }}
              InputProps={{
                endAdornment: (
                  <InputAdornment position="end">
                    <IconButton type="submit" size="small" aria-label="Submit patient search">
                      <SearchIcon fontSize="small" />
                    </IconButton>
                  </InputAdornment>
                ),
              }}
            />
          </Box>

          {/* Avatar */}
          <Avatar
            sx={{ bgcolor: 'secondary.50', color: 'secondary.700', width: 36, height: 36, fontSize: '0.85rem' }}
            aria-label={`User: ${email ?? 'Staff'}`}
          >
            {avatarInitials}
          </Avatar>
        </Box>

        {/* ── Last login banner (US_016 AC-4) ── */}
        <LastLoginBanner />

        {/* ── Page content ── */}
        <Container maxWidth="xl" sx={{ mt: 3, mb: 6, flex: 1 }}>
          <Typography variant="h5" component="h1" sx={{ mb: 3, fontWeight: 600 }}>
            Staff Dashboard
          </Typography>

          {/* Stat cards (AC-1) */}
          <StatCards stats={data?.stats} isLoading={isLoading} />

          {/* Quick actions (AC-1: Walk-in, View Queue) */}
          <QuickActions onWalkIn={() => setWalkInOpen(true)} />

          {/* Two-column grid: schedule | pending tasks */}
          <Grid container spacing={3}>
            {/* Today's Schedule (AC-2, US_026) */}
            <Grid item xs={12} md={7}>
              <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
                <Typography variant="h6" sx={{ mb: 2 }}>Today's Schedule</Typography>
                <ScheduleTable rows={data?.schedule} isLoading={isLoading} isError={isError} />
              </Paper>
            </Grid>

            {/* Pending Tasks (AC-3) */}
            <Grid item xs={12} md={5}>
              <Paper variant="outlined" sx={{ p: 3, borderRadius: 2 }}>
                <Typography variant="h6" sx={{ mb: 2 }}>Pending Tasks</Typography>
                <PendingTasksPanel tasks={data?.pendingTasks} isLoading={isLoading} />
              </Paper>
            </Grid>
          </Grid>
        </Container>
      </Box>

      {/* Walk-in Registration Modal (US_022 AC-1) */}
      <WalkInRegistrationModal
        open={walkInOpen}
        onClose={() => setWalkInOpen(false)}
      />
    </Box>
  );
}

