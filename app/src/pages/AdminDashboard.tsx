/**
 * AdminDashboard — SCR-015
 *
 * System Administration hub for Admin users (US_058, UXR-403 error accent treatment).
 *
 * Layout (wireframe-SCR-015-admin-dashboard.html):
 *   sidebar nav (error accent) | header (breadcrumb + avatar)
 *   main: LastLoginBanner → MetricsOverview → TrendChart → (config tabs placeholder)
 *
 * US_058 AC-1: 5-KPI MetricsOverview (active users, daily appts, no-show rate,
 *              AI agreement rate, uptime).
 * US_058 AC-2: TrendChart with 7-day / 30-day toggle.
 * UXR-003: Breadcrumb navigation in header.
 * UXR-403: error.main accent applied to sidebar active state + metric values.
 * UXR-502: Skeleton loading placeholders for >300 ms loads.
 * Edge case: stale data badge + "Data as of [ts]" caption when backend unavailable.
 * Edge case: metric cards collapse to Accordion on mobile (<sm breakpoint).
 */

import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Divider from '@mui/material/Divider';
import Link from '@mui/material/Link';
import List from '@mui/material/List';
import ListItem from '@mui/material/ListItem';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Typography from '@mui/material/Typography';
import HomeOutlinedIcon from '@mui/icons-material/HomeOutlined';
import PeopleOutlinedIcon from '@mui/icons-material/PeopleOutlined';
import SearchIcon from '@mui/icons-material/Search';
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined';
import SpeedOutlinedIcon from '@mui/icons-material/SpeedOutlined';
import { Link as RouterLink } from 'react-router-dom';
import LastLoginBanner from '@/components/auth/LastLoginBanner';
import ConfigTabs from '@/components/admin/ConfigTabs';
import MetricsOverview from '@/components/admin/MetricsOverview';
import StaffAccountList from '@/components/admin/StaffAccountList';
import TrendChart from '@/components/admin/TrendChart';
import { useAdminMetrics } from '@/hooks/useAdminMetrics';
import { useAuthStore } from '@/hooks/useAuth';

// ─── Sidebar nav items ────────────────────────────────────────────────────────

const NAV_ITEMS = [
  { label: 'Admin',   icon: <SettingsOutlinedIcon />, href: '/admin/dashboard' },
  { label: 'Search',  icon: <SearchIcon />,            href: '/admin/patients'  },
  { label: 'Users',   icon: <PeopleOutlinedIcon />,    href: '/admin/users'     },
  { label: 'Home',    icon: <HomeOutlinedIcon />,       href: '/dashboard'       },
] as const;

const SIDEBAR_WIDTH = 220;

// ─── Component ────────────────────────────────────────────────────────────────

export default function AdminDashboard() {
  const email = useAuthStore(s => s.email);
  const avatarInitials = email
    ? email.split('@')[0].slice(0, 2).toUpperCase()
    : 'AD';

  const { data: metricsData, isLoading: metricsLoading } = useAdminMetrics();

  const snapshot  = metricsData?.metrics;
  const isStale   = metricsData?.isStale ?? false;
  const staleAt   = metricsData?.generatedAt;

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'grey.50' }}>

      {/* ── Sidebar (wireframe admin-sidebar, UXR-403 error accent) ── */}
      <Box
        component="nav"
        aria-label="Admin navigation"
        sx={{
          width:         SIDEBAR_WIDTH,
          flexShrink:    0,
          bgcolor:       'background.paper',
          borderRight:   '1px solid',
          borderColor:   'error.100',
          display:       { xs: 'none', md: 'flex' },
          flexDirection: 'column',
        }}
      >
        {/* Brand */}
        <Box sx={{ px: 3, py: 2.5, display: 'flex', alignItems: 'center', gap: 1 }}>
          <SpeedOutlinedIcon sx={{ color: 'error.main', fontSize: 22 }} />
          <Typography variant="h6" sx={{ color: 'error.main', fontWeight: 700 }}>
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
                      borderLeft:   '3px solid',
                      borderColor:  'error.main',
                      bgcolor:      'error.50',
                      color:        'error.main',
                      '& .MuiListItemIcon-root': { color: 'error.main' },
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

        {/* ── Header (UXR-003 breadcrumb + avatar) ── */}
        <Box
          component="header"
          role="banner"
          sx={{
            display:        'flex',
            alignItems:     'center',
            justifyContent: 'space-between',
            px:             3,
            py:             1.5,
            bgcolor:        'background.paper',
            borderBottom:   '1px solid',
            borderColor:    'divider',
            gap:            2,
            flexWrap:       'wrap',
          }}
        >
          {/* Breadcrumb (UXR-003) */}
          <Breadcrumbs aria-label="Breadcrumb navigation">
            <Link component={RouterLink} to="/dashboard" underline="hover" color="inherit" variant="body2">
              Home
            </Link>
            <Typography variant="body2" color="text.primary" aria-current="page">
              Admin Dashboard
            </Typography>
          </Breadcrumbs>

          {/* Avatar — error background per UXR-403 admin treatment */}
          <Avatar
            sx={{
              bgcolor:  'error.50',
              color:    'error.700',
              width:    36,
              height:   36,
              fontSize: '0.85rem',
            }}
            aria-label={`User: ${email ?? 'Admin'}`}
          >
            {avatarInitials}
          </Avatar>
        </Box>

        {/* ── Last login banner (US_016 AC-4) ── */}
        <LastLoginBanner />

        {/* ── Content ── */}
        <Box component="main" role="main" sx={{ flex: 1, overflowY: 'auto', p: 3 }}>

          <Typography variant="h5" component="h1" gutterBottom fontWeight={600}>
            System Administration
          </Typography>

          {/* ── Metrics section (US_058 AC-1) ── */}
          <Typography variant="subtitle2" color="text.secondary" sx={{ mb: 1.5, textTransform: 'uppercase', letterSpacing: '0.06em', fontSize: '0.7rem' }}>
            System Metrics
          </Typography>

          <MetricsOverview
            metrics={snapshot}
            isLoading={metricsLoading}
            isStale={isStale}
            staleAt={staleAt}
          />

          {/* ── Trend charts section (US_058 AC-2) ── */}
          <Typography variant="subtitle2" color="text.secondary" sx={{ mb: 1.5, textTransform: 'uppercase', letterSpacing: '0.06em', fontSize: '0.7rem' }}>
            Metric Trends
          </Typography>

          <TrendChart />

          {/* ── Configuration tabs (US_058 AC-4) ── */}
          <Box sx={{ mt: 4 }}>
            <Typography variant="subtitle2" color="text.secondary" sx={{ mb: 1.5, textTransform: 'uppercase', letterSpacing: '0.06em', fontSize: '0.7rem' }}>
              Configuration
            </Typography>
            <ConfigTabs />
          </Box>

          {/* ── User Management (US_061 AC-1 – AC-4) ── */}
          <Box sx={{ mt: 4 }}>
            <Typography variant="subtitle2" color="text.secondary" sx={{ mb: 1.5, textTransform: 'uppercase', letterSpacing: '0.06em', fontSize: '0.7rem' }}>
              User Management
            </Typography>
            <StaffAccountList />
          </Box>

        </Box>
      </Box>
    </Box>
  );
}
