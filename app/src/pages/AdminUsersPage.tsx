/**
 * AdminUsersPage — /admin/users
 *
 * Full-page user management view for Admin users (US_058 AC-3, US_061 AC-1 – AC-4).
 * Reuses the same sidebar + header shell as AdminDashboard.
 * Main content: UserManagementPanel (list, invite, activate/deactivate).
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
import UserManagementPanel from '@/components/admin/UserManagementPanel';
import { useAuthStore } from '@/hooks/useAuth';

const NAV_ITEMS = [
  { label: 'Admin',  icon: <SettingsOutlinedIcon />, href: '/admin/dashboard' },
  { label: 'Search', icon: <SearchIcon />,            href: '/admin/patients'  },
  { label: 'Users',  icon: <PeopleOutlinedIcon />,    href: '/admin/users'     },
  { label: 'Home',   icon: <HomeOutlinedIcon />,      href: '/dashboard'       },
] as const;

const SIDEBAR_WIDTH = 220;

export default function AdminUsersPage() {
  const email = useAuthStore(s => s.email);
  const avatarInitials = email
    ? email.split('@')[0].slice(0, 2).toUpperCase()
    : 'AD';

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'grey.50' }}>

      {/* ── Sidebar ── */}
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
        <Box sx={{ px: 3, py: 2.5, display: 'flex', alignItems: 'center', gap: 1 }}>
          <SpeedOutlinedIcon sx={{ color: 'error.main', fontSize: 22 }} />
          <Typography variant="h6" sx={{ color: 'error.main', fontWeight: 700 }}>
            UPACIP
          </Typography>
        </Box>
        <Divider />
        <List dense sx={{ pt: 1 }}>
          {NAV_ITEMS.map(({ label, icon, href }) => {
            const isActive = globalThis.location.pathname === href;
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
                      borderLeft:  '3px solid',
                      borderColor: 'error.main',
                      bgcolor:     'error.50',
                      color:       'error.main',
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

        {/* ── Header ── */}
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
          <Breadcrumbs aria-label="Breadcrumb navigation">
            <Link component={RouterLink} to="/dashboard" underline="hover" color="inherit" variant="body2">
              Home
            </Link>
            <Link component={RouterLink} to="/admin/dashboard" underline="hover" color="inherit" variant="body2">
              Admin
            </Link>
            <Typography variant="body2" color="text.primary" aria-current="page">
              Users
            </Typography>
          </Breadcrumbs>

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

        {/* ── Last login banner ── */}
        <LastLoginBanner />

        {/* ── Content ── */}
        <Box component="main" role="main" sx={{ flex: 1, overflowY: 'auto', p: 3 }}>
          <Typography variant="h5" component="h1" gutterBottom fontWeight={600}>
            User Management
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 3 }}>
            Manage staff and admin accounts — invite, activate, or deactivate users.
          </Typography>

          <UserManagementPanel />
        </Box>

      </Box>
    </Box>
  );
}
